using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.AreaLogic.Cutscenes;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Area;
using Kingmaker.Blueprints.Root;
using Kingmaker.Controllers;
using Kingmaker.Controllers.Dialog;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.EntitySystem.Persistence.Scenes;
using Kingmaker.GameModes;
using Kingmaker.QA;
using Kingmaker.ResourceManagement;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Kingmaker.View;
using Kingmaker.View.Spawners;
using UnityEngine;
using UnityModManagerNet;
using WrathLifecycleFix;
using WrathPerformanceLab;

namespace WrathDeferredNpcViews
{
    public sealed class Settings : UnityModManager.ModSettings
    {
        public bool ProfileOnly;
        public bool AllNonPartyNpcs = true;
        public float PrewarmRadius = 45;
        public override void Save(UnityModManager.ModEntry entry) => Save(this, entry);
    }

    public static class Main
    {
        private const string Id = "WrathDeferredNpcViews";
        private static string patchOwner = Id;
        private static UnityModManager.ModEntry entry;
        private static Harmony harmony;
        private static Settings settings;
        private const int Capacity = 4096;
        private static WeakWorkSet<UnitEntityData> work = new WeakWorkSet<UnitEntityData>(Capacity);
        private static readonly FieldInfo dummyFlag = AccessTools.Field(typeof(UnitEntityData), "<HasDummyView>k__BackingField");
        private static readonly FieldInfo sleepExtra = AccessTools.Field(typeof(SpawnerOptimizedUnit), "m_SleepWhenFarAway");
        private static readonly FieldInfo interactions = AccessTools.Field(typeof(UnitPartInteractions), "m_Interactions");
        private static bool enabled, session, barrier, createPatched, spawnPatched;
        private static int bypass, generation;
        private static double nextScan, nextLog, nextRetry;
        private static long deferred, warmed, failed, capacitySkips, createCalls, createTicks, spawnCalls, spawnTicks;
        private static BoundedCsv csv;
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        private sealed class SpawnScope
        {
            internal BlueprintUnit Blueprint;
            internal bool Eligible, Chosen;
            internal SpawnScope Parent;
            internal UnitEntityData Created;
        }
        [ThreadStatic] private static SpawnScope spawn;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            if (!Initialize(modEntry, Id)) return false;
            entry.OnToggle = Toggle;
            entry.OnUnload = Unload;
            entry.OnUpdate = Update;
            entry.OnGUI = GUI;
            entry.OnSaveGUI = e => settings.Save(e);
            return true;
        }

        internal static bool Initialize(UnityModManager.ModEntry modEntry, string owner, Settings providedSettings = null, string logPrefix = "")
        {
            entry = modEntry;
            patchOwner = owner;
            try
            {
                if (!GameCompatibility.Matches(typeof(UnitEntityData).Assembly.Location))
                    throw new InvalidOperationException("Unsupported Assembly-CSharp hash");
                if (dummyFlag?.FieldType != typeof(bool) || sleepExtra?.FieldType != typeof(bool) || interactions == null)
                    throw new InvalidOperationException("Unsupported dummy/spawner/interaction fields");
                settings = providedSettings ?? UnityModManager.ModSettings.Load<Settings>(entry);
                if (float.IsNaN(settings.PrewarmRadius) || float.IsInfinity(settings.PrewarmRadius)) settings.PrewarmRadius = 45;
                settings.PrewarmRadius = Mathf.Clamp(settings.PrewarmRadius, 25, 100);
                string folder = Path.Combine(entry.Path, "Logs");
                Directory.CreateDirectory(folder);
                csv = new BoundedCsv(Path.Combine(folder, logPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".csv"),
                    2 * 1024 * 1024, "elapsed_s", "event", "pending", "deferred_total", "warmed_total", "failures", "capacity_skips",
                    "create_calls", "create_total_ms", "spawn_calls", "spawn_total_ms", "profile_only", "barrier");
                harmony = new Harmony(patchOwner);
                Install();
                enabled = true;
                Log("enabled_next_drezen_load");
                return true;
            }
            catch (Exception ex)
            {
                harmony?.UnpatchAll(patchOwner); csv?.Dispose(); enabled = false;
                entry.Logger.LogException(ex); return false;
            }
        }

        private static void Patch(MethodBase method, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
        {
            if (method == null) throw new MissingMethodException("Required compatibility hook missing");
            HarmonyMethod H(string name) => name == null ? null : new HarmonyMethod(typeof(Main), name) { priority = Priority.Last };
            harmony.Patch(method, H(prefix), H(postfix), H(transpiler), H(finalizer));
        }

        private static void Install()
        {
            createPatched = spawnPatched = false;
            Patch(AccessTools.Method(typeof(UnitEntityData), "CreateView"), prefix: nameof(TimeStart), finalizer: nameof(CreateEnd), transpiler: nameof(CreateRewrite));
            Patch(AccessTools.Method(typeof(EntityCreationController), "SpawnUnit", new[] { typeof(BlueprintUnit), typeof(Vector3), typeof(Quaternion), typeof(SceneEntitiesState), typeof(string) }),
                prefix: nameof(TimeStart), finalizer: nameof(SpawnEnd), transpiler: nameof(SpawnRewrite));
            if (!createPatched || !spawnPatched) throw new InvalidOperationException("Resource request IL did not match; no deferral installed");
            Patch(AccessTools.Method(typeof(SceneLoader), "LoadAreaCoroutine"), prefix: nameof(BeginArea));
            Patch(AccessTools.Method(typeof(UnitSpawner), "SpawnUnit"), prefix: nameof(EnterSpawner), finalizer: nameof(ExitSpawner));
            Patch(AccessTools.Constructor(typeof(UnitEntityData), new[] { typeof(UnitEntityView) }), postfix: nameof(Constructed));
            Patch(AccessTools.Method(typeof(UnitEntityData), "UpdateHasDummyView"), prefix: nameof(KeepDeferred));
            Patch(AccessTools.Method(typeof(UnitEvaluator), "HandleGetValue"), prefix: nameof(Evaluated));
            Patch(AccessTools.Method(typeof(Evaluator<UnitEntityData>), "TryGetValue"), postfix: nameof(TryEvaluated));
            Patch(AccessTools.Method(typeof(CutsceneControlledUnit), "MarkUnit"), prefix: nameof(ActorNeeded));
            Patch(AccessTools.Method(typeof(CutscenePlayerData), "Start"), prefix: nameof(ScriptBarrier));
            Patch(AccessTools.Method(typeof(DialogController), "StartDialog"), prefix: nameof(ScriptBarrier));
            Patch(AccessTools.Method(typeof(Game), "StartMode"), prefix: nameof(ModeStarting));
            Patch(AccessTools.Method(typeof(UnitEntityData), "JoinCombat"), prefix: nameof(ScriptBarrier));
            Patch(AccessTools.Method(typeof(Player), "UpdateIsInCombat"), postfix: nameof(CombatUpdated));
            Patch(AccessTools.Method(typeof(Player), "SetChapter"), prefix: nameof(ChapterChanging));
            Patch(AccessTools.Method(typeof(UnitCommand), "Init"), prefix: nameof(CommandNeeded));
            Patch(AccessTools.Method(typeof(UnitCommand), "Start"), prefix: nameof(CommandStarting));
            Patch(AccessTools.Method(typeof(Game), "AfterSceneLoaderFinishedArea"), postfix: nameof(LoadingFinished));
            Patch(AccessTools.Method(typeof(Game), "DisposeState"), prefix: nameof(ResetSession));
        }

        internal static MethodInfo Request => AccessTools.Method(typeof(BundledResourceHandle<UnitEntityView>), "Request");
        public static IEnumerable<CodeInstruction> CreateRewrite(IEnumerable<CodeInstruction> input)
        {
            var output = ResourceRewriter.Rewrite(input, Request, AccessTools.Method(typeof(Main), nameof(ChooseViewAsset)), false, out createPatched);
            if (!createPatched) { barrier = true; entry?.Logger.Log("CreateView shape mismatch: original IL preserved"); }
            return output;
        }
        public static IEnumerable<CodeInstruction> SpawnRewrite(IEnumerable<CodeInstruction> input)
        {
            var output = ResourceRewriter.Rewrite(input, Request, AccessTools.Method(typeof(Main), nameof(ChooseSpawnAsset)), true, out spawnPatched);
            if (!spawnPatched) { barrier = true; entry?.Logger.Log("SpawnUnit shape mismatch: original IL preserved"); }
            return output;
        }
        public static void TimeStart(out long __state) => __state = Stopwatch.GetTimestamp();
        public static void CreateEnd(long __state) { createCalls++; createTicks += Stopwatch.GetTimestamp() - __state; }
        public static void SpawnEnd(long __state) { spawnCalls++; spawnTicks += Stopwatch.GetTimestamp() - __state; }

        private static bool Loading => LoadingProcess.Instance != null && LoadingProcess.Instance.IsLoadingInProcess;
        private static bool CanDefer => enabled && session && !barrier && !settings.ProfileOnly && bypass == 0 && Loading &&
            Game.Instance?.Player != null && Policy.KnownChapter(Game.Instance.Player.Chapter) && !Game.Instance.Player.IsInCombat &&
            BuildModeUtility.Data?.DisableUnitOptimization != true;
        private static string Area => Game.Instance?.CurrentlyLoadedArea?.AssetGuid.ToString();
        private static string Dummy => BlueprintRoot.Instance.OptimizationDummyUnit.AssetId;

        public static void BeginArea(BlueprintArea area)
        {
            work = new WeakWorkSet<UnitEntityData>(Capacity);
            session = area != null && string.Equals(area.AssetGuid.ToString(), Policy.AreaId, StringComparison.OrdinalIgnoreCase);
            barrier = false; nextScan = 0; nextRetry = 0;
            generation++;
            Log(session ? "begin_drezen" : "begin_other_area");
        }
        public static void ResetSession() { session = false; barrier = true; generation++; work = new WeakWorkSet<UnitEntityData>(Capacity); }

        private static bool Safe(UnitEntityData unit, bool requireExtra = true)
        {
            if (unit == null || unit.IsDisposed || unit.Destroyed || unit.DestroyMark || unit.HoldingState == null ||
                unit.HoldingState == Game.Instance.Player.CrossSceneState || !Policy.Allowed(Area, unit.Blueprint?.AssetGuid.ToString(), settings.AllNonPartyNpcs) ||
                (!settings.AllNonPartyNpcs && requireExtra && !unit.IsExtra) || !unit.IsInGame || unit.IsPlayerFaction || unit.IsDirectlyControllable || unit.IsInCombat ||
                unit.State.IsDead || unit.State.IsFinallyDead || (bool)unit.Sleepless || unit.CutsceneControlledUnit != null ||
                unit.Get<UnitPartCompanion>() != null || unit.Get<UnitPartPet>() != null || unit.Get<UnitPartPetMaster>() != null ||
                unit.Get<UnitPartDollData>() != null || unit.GetActivePolymorph().Component != null ||
                unit.SavedRagdoll != null || unit.SavedDismemberment != null || !unit.Commands.Empty)
                return false;
            if (settings.AllNonPartyNpcs) return true;
            if (!unit.Faction.Peaceful) return false;
            var part = unit.Get<UnitPartInteractions>();
            return part == null || ((ICollection)interactions.GetValue(part)).Count == 0;
        }

        public static string ChooseViewAsset(string original, UnitEntityData unit)
        {
            try
            {
                if (!CanDefer || original == Dummy || work.Seen(unit) || !Safe(unit)) return original;
                if (!work.Add(unit)) { capacitySkips++; return original; }
                deferred++; return Dummy;
            }
            catch (Exception ex) { Fault("select_view", ex); return original; }
        }

        public static void EnterSpawner(UnitSpawner __instance, out object __state)
        {
            var scope = new SpawnScope { Parent = spawn };
            __state = scope;
            spawn = scope;
            try
            {
                if (!CanDefer) return;
                scope.Blueprint = __instance.Blueprint;
                var optimization = __instance.GetComponent<SpawnerOptimizedUnit>();
                scope.Eligible = CanDefer && Policy.Allowed(Area, scope.Blueprint?.AssetGuid.ToString(), settings.AllNonPartyNpcs) &&
                    !scope.Blueprint.Faction.IsDirectlyControllable &&
                    (settings.AllNonPartyNpcs || (optimization != null && scope.Blueprint.Faction.Peaceful && (bool)sleepExtra.GetValue(optimization)));
            }
            catch (Exception ex) { Fault("spawner_guard", ex); }
        }
        public static void ExitSpawner(object __state)
        {
            var scope = (SpawnScope)__state;
            spawn = scope.Parent;
            // IsExtra is applied by the spawner's later OnSpawn initializer. Keep only provisionally until warmup.
            if (scope.Created != null && !work.Contains(scope.Created)) Warm(scope.Created, "capacity_fallback", true);
        }
        public static string ChooseSpawnAsset(string original, BlueprintUnit blueprint, Vector3 position, SceneEntitiesState state)
        {
            try
            {
                if (!CanDefer || spawn == null || !spawn.Eligible || spawn.Blueprint != blueprint || spawn.Chosen ||
                    original == Dummy || state == Game.Instance.Player.CrossSceneState || UnitSpawningData.Current?.Doll != null ||
                    UnitSpawningData.Current?.SpawnHidden == true) return original;
                // Reserve capacity before creating a dummy whose normal serialized flag will be neutralized.
                if (!work.HasCapacity) { capacitySkips++; return original; }
                spawn.Chosen = true;
                return Dummy;
            }
            catch (Exception ex) { Fault("select_spawn", ex); return original; }
        }
        public static void Constructed(UnitEntityData __instance, UnitEntityView view)
        {
            try
            {
                if (spawn?.Chosen != true || spawn.Created != null || !view.IsOptimizationDummy || __instance.Blueprint != spawn.Blueprint) return;
                spawn.Created = __instance;
                // The original constructor sets this to true for its native dummy. Our temporary choice must not persist.
                dummyFlag.SetValue(__instance, false);
                if (work.Add(__instance)) deferred++;
                else { capacitySkips++; barrier = true; }
            }
            catch (Exception ex) { Fault("constructor", ex); }
        }

        public static bool KeepDeferred(UnitEntityData __instance) => !work.Contains(__instance);
        public static void Evaluated(UnitEntityData value) => Require(value, "script_evaluator");
        public static void TryEvaluated(bool __result, UnitEntityData value) { if (__result) Require(value, "try_evaluator"); }
        public static void ActorNeeded(UnitEntityData unit) => Require(unit, "cutscene_actor");
        public static void CommandNeeded(UnitEntityData executor) => Require(executor, "command_init");
        public static void CommandStarting(UnitCommand __instance) => Require(__instance.Executor, "command_start");
        public static void CombatUpdated(Player __instance) { if (__instance.IsInCombat) ScriptBarrier(); }
        public static void ChapterChanging(int chapter) { if (!Policy.KnownChapter(chapter)) ScriptBarrier(); }
        public static void ScriptBarrier() { if (enabled && session) { barrier = true; Flush("script_barrier"); } }
        public static void ModeStarting(GameModeType type)
        {
            if (type == GameModeType.Cutscene || type == GameModeType.Dialog) ScriptBarrier();
        }
        private static void Require(UnitEntityData unit, string reason)
        {
            if (bypass == 0 && unit != null && work.Contains(unit)) Warm(unit, reason);
        }

        private static bool Warm(UnitEntityData unit, string reason, bool force = false)
        {
            if (!force && !work.Contains(unit)) return true;
            if (unit.IsDisposed || unit.Destroyed || unit.DestroyMark)
            { work.Complete(unit); return true; }
            // Resource selection runs before the new view is attached. Never lose ownership in a reentrant callback.
            if (unit.View == null) return false;
            if (!unit.View.IsOptimizationDummy) { work.Complete(unit); return true; }
            var old = unit.View;
            UnitEntityView replacement = null;
            bypass++;
            try
            {
                replacement = unit.CreateView();
                if (replacement == null || replacement.IsOptimizationDummy)
                    throw new InvalidOperationException("Full model unavailable; original dummy retained");
                replacement.Blueprint = unit.Blueprint;
                replacement.UniqueId = unit.UniqueId;
                replacement.transform.SetParent(old.transform.parent, true);
                replacement.transform.position = unit.Position;
                replacement.transform.rotation = old.transform.rotation;
                unit.AttachToViewOnLoad(replacement);
                if (unit.View != replacement) throw new InvalidOperationException("Full view attachment failed");
                if (replacement.AnimationManager != null)
                {
                    replacement.AnimationManager.Tick();
                    replacement.AnimationManager.UpdateTime(0.1f);
                }
                if (replacement.Fader != null) replacement.SetVisible(replacement.IsVisible, true);
                UnityEngine.Object.Destroy(old.gameObject);
                work.Complete(unit); warmed++; Log(reason);
                return true;
            }
            catch (Exception ex)
            {
                if (unit.View != old)
                {
                    try { unit.AttachToViewOnLoad(old); } catch (Exception rollback) { entry.Logger.LogException(rollback); }
                }
                if (replacement != null && replacement != old && unit.View != replacement) UnityEngine.Object.Destroy(replacement.gameObject);
                Fault("restore", ex); nextRetry = clock.Elapsed.TotalSeconds + 2; return false;
            }
            finally { bypass--; }
        }
        private static bool Flush(string reason)
        {
            bool success = true;
            foreach (var unit in work.Snapshot()) success &= Warm(unit, reason);
            return success;
        }

        public static void LoadingFinished(ref IEnumerator __result) => __result = FinishWithWarmup(__result, generation);
        private static IEnumerator FinishWithWarmup(IEnumerator original, int startedGeneration)
        {
            // All party placement and camera setup have finished; warm nearby models under the loading screen.
            return WarmupSequence.Append(original, () => enabled && session && startedGeneration == generation,
                () =>
                {
                    try { return WarmOne(); }
                    catch (Exception ex) { Fault("loading_warmup", ex); return false; }
                }, () => Log("loading_warmup_finished"));
        }

        private static bool WarmOne()
        {
            var camera = Game.Instance.UI?.GetCameraRig()?.Camera;
            var anchors = Game.Instance.State.Units.All.Where(u => u.IsDirectlyControllable && u.IsInGame && !u.IsDisposed).ToArray();
            UnitEntityData chosen = null;
            double best = double.PositiveInfinity;
            string reason = "proximity";
            foreach (var unit in work.Snapshot())
            {
                if (unit.IsDisposed || unit.Destroyed || unit.DestroyMark)
                { work.Complete(unit); continue; }
                if (unit.View == null) continue;
                if (!unit.View.IsOptimizationDummy) { work.Complete(unit); continue; }
                double distance = double.PositiveInfinity;
                foreach (var anchor in anchors)
                {
                    var delta = anchor.Position - unit.Position;
                    distance = Math.Min(distance, delta.x * delta.x + delta.z * delta.z);
                }
                bool inCamera = false;
                if (camera != null)
                {
                    var point = camera.WorldToViewportPoint(unit.Position + Vector3.up);
                    inCamera = point.z > 0 && point.x >= -0.4 && point.x <= 1.4 && point.y >= -0.4 && point.y <= 1.4;
                }
                bool urgent = barrier || !Safe(unit) || unit.IsInCombat || unit.CutsceneControlledUnit != null;
                if (Policy.NeedsFull(!urgent, anchors.Length != 0, camera != null, inCamera, distance, settings.PrewarmRadius))
                {
                    double priority = urgent ? -2 : inCamera ? -1 : distance;
                    if (chosen == null || priority < best) { chosen = unit; best = priority; reason = urgent ? "state_changed" : "proximity"; }
                }
            }
            return chosen != null && Warm(chosen, reason);
        }

        internal static void Update(UnityModManager.ModEntry _, float dt)
        {
            if (!enabled || !session || bypass != 0) return;
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                if (!Policy.KnownChapter(Game.Instance.Player.Chapter) || Game.Instance.Player.IsInCombat) barrier = true;
                if (now >= nextLog) { nextLog = now + 15; Log("snapshot"); }
                // Initial loading keeps distant views deferred. Scripts above have synchronous escape paths.
                if (Loading || now < nextScan || now < nextRetry) return;
                nextScan = now + 0.1;
                // At most one full model per rendered Update. A single instantiate cannot be preempted.
                if (WarmOne()) nextScan = now;
            }
            catch (Exception ex) { Fault("update", ex); nextRetry = clock.Elapsed.TotalSeconds + 2; }
        }
        internal static bool Toggle(UnityModManager.ModEntry _, bool value)
        {
            if (value) { enabled = true; Log("enabled_next_load"); return true; }
            barrier = true;
            if (!Flush("disable")) return false;
            enabled = false; Log("disabled"); return true;
        }
        internal static bool Unload(UnityModManager.ModEntry e)
        {
            if (!Toggle(e, false)) return false;
            harmony.UnpatchAll(patchOwner); csv?.Dispose(); return true;
        }
        internal static void GUI(UnityModManager.ModEntry _)
        {
            GUILayout.Label("Experimental: regular Drezen NPC models. Story return map excluded. Applies on next area load.");
            GUILayout.Label("Player units, pets and unsupported special models retain their original loading path.");
            bool all = GUILayout.Toggle(settings.AllNonPartyNpcs, "All non-party NPCs (otherwise four commoner types only)");
            if (all != settings.AllNonPartyNpcs && Flush("scope_changed")) settings.AllNonPartyNpcs = all;
            bool profile = GUILayout.Toggle(settings.ProfileOnly, "Profile only (no deferred models)");
            if (profile != settings.ProfileOnly && (!profile || Flush("profile_only"))) settings.ProfileOnly = profile;
            GUILayout.Label("Prewarm radius: " + settings.PrewarmRadius.ToString("F0") + " m");
            settings.PrewarmRadius = GUILayout.HorizontalSlider(settings.PrewarmRadius, 25, 100);
            GUILayout.Label($"Deferred {deferred}, restored {warmed}, pending {work.Snapshot().Count}, failures {failed}");
            if (GUILayout.Button("Restore all models and stop deferring until next area load")) { barrier = true; Flush("manual_restore"); }
        }
        private static void Fault(string reason, Exception ex)
        {
            barrier = true; failed++;
            if (failed <= 20) entry?.Logger.Log(reason + ": " + ex);
            Log("failure_" + reason);
        }
        private static void Log(string reason)
        {
            try
            {
                csv?.Row(clock.Elapsed.TotalSeconds, reason, work.Snapshot().Count, deferred, warmed, failed, capacitySkips,
                    createCalls, createTicks * 1000.0 / Stopwatch.Frequency, spawnCalls, spawnTicks * 1000.0 / Stopwatch.Frequency,
                    settings?.ProfileOnly ?? true, barrier);
                if (reason == "snapshot" || reason.StartsWith("begin_") || reason.StartsWith("failure_") || reason == "disabled") csv?.Flush();
            }
            catch { try { csv?.Dispose(); } catch { } csv = null; }
        }
    }
}
