using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using UnityEngine;
using UnityEngine.Profiling;
using UnityModManagerNet;
using WrathPerformanceLab;

namespace PerformanceImprove.Diagnostics
{
    /// <summary>
    /// Session telemetry for shipping to other players: one JSON-lines file per run, written
    /// incrementally so a crash still leaves usable data. Observation only, except the optional
    /// <c>skipKnownReadd</c> experiment which is off by default.
    /// </summary>
    internal sealed class ProbeHost
    {
        internal const string HarmonyId = "PerformanceImprove.Diagnostics";
        private static ProbeHost instance;

        private readonly UnityModManager.ModEntry entry;
        private readonly int sampleSeconds;
        private readonly long maxBytes;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<MethodBase, TimingAggregate> timings = new Dictionary<MethodBase, TimingAggregate>();
        private readonly object gate = new object();
        private readonly List<string> missing = new List<string>();

        [ThreadStatic] private static CopyStats pendingCopy;

        private Harmony harmony;
        private LogSink sink;
        private Process process;
        private double nextSample;
        private volatile bool enabled;
        private volatile bool skipKnownReadd;

        private FieldInfo loadingCurrent, loadingTimer;
        private Type gameType, loadingProcessType;
        private WeakReference loadingInstance;

        internal string LogPath => sink?.Path;
        internal string LastError { get; private set; }
        internal bool Active => enabled;
        internal bool SkipKnownReadd { get => skipKnownReadd; set => skipKnownReadd = value; }
        internal long Records => sink?.Records ?? 0;
        internal bool LimitReached => sink?.LimitReached ?? false;
        internal IReadOnlyList<string> Missing => missing;

        internal ProbeHost(UnityModManager.ModEntry modEntry, int sampleSeconds, int maxMegabytes)
        {
            entry = modEntry ?? throw new ArgumentNullException(nameof(modEntry));
            this.sampleSeconds = Math.Max(2, sampleSeconds);
            maxBytes = Math.Max(1, maxMegabytes) * 1024L * 1024L;
        }

        internal bool Install()
        {
            if (enabled) return true;
            try
            {
                string directory = Path.Combine(entry.Path, "Logs");
                Directory.CreateDirectory(directory);
                sink = new LogSink(Path.Combine(directory, "perf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl"), maxBytes);
                harmony = new Harmony(HarmonyId);
                process = Process.GetCurrentProcess();
                gameType = AccessTools.TypeByName("Kingmaker.Game");
                loadingProcessType = AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.LoadingProcess");
                instance = this;
                WriteSession();
                InstallProbes();
                if (sink != null && sink.Bytes == 0)
                    entry?.Logger.Log("Diagnostics sink wrote 0 bytes; failure: " + sink.Failure);
                enabled = true;
                nextSample = 0;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                Shutdown();
                return false;
            }
        }

        internal bool SetEnabled(bool value)
        {
            if (value == enabled) return true;
            return value ? Install() : Stop();
        }

        internal bool Stop()
        {
            if (!enabled && sink == null) return true;
            enabled = false;
            FlushTimings();
            sink?.Flush();
            Shutdown();
            return true;
        }

        internal void Unload()
        {
            enabled = false;
            FlushTimings();
            Shutdown();
        }

        private void Shutdown()
        {
            try { harmony?.UnpatchAll(HarmonyId); } catch { }
            harmony = null;
            instance = null;
            try { process?.Dispose(); } catch { }
            process = null;
            try { sink?.Dispose(); } catch { }
            sink = null;
            lock (gate) timings.Clear();
        }

        internal void Tick(float delta)
        {
            if (!enabled) return;
            double now = clock.Elapsed.TotalSeconds;
            if (now < nextSample) return;
            nextSample = now + sampleSeconds;
            FlushTimings();
            EmitMemory("periodic");
        }

        // ---- session + records -----------------------------------------------------------------

        private void WriteSession()
        {
            string assemblyHash = "unavailable";
            try
            {
                string location = typeof(Kingmaker.Game).Assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                    using (var stream = File.OpenRead(location))
                    using (var sha = SHA256.Create())
                        assemblyHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch { }

            string mods = "unavailable";
            try
            {
                mods = string.Join("; ", UnityModManager.modEntries
                    .Where(m => m != null && m.Info != null)
                    .Select(m => m.Info.Id + "@" + m.Info.Version + (m.Enabled ? "" : "(off)")));
            }
            catch { }

            sink.Write(0, "session",
                "mod", "PerformanceImprove",
                "version", entry.Info?.Version?.ToString() ?? "unknown",
                "started_utc", DateTime.UtcNow.ToString("O"),
                "game_version", Application.version ?? "",
                "unity_version", Application.unityVersion ?? "",
                "os", SystemInfo.operatingSystem ?? "",
                "cpu_count", SystemInfo.processorCount,
                "system_memory_mb", SystemInfo.systemMemorySize,
                "assembly_sha256", assemblyHash,
                "sample_seconds", sampleSeconds,
                "skip_known_readd", skipKnownReadd,
                "mods", mods);
            sink.Flush();
        }

        internal void EmitCopy(CopyStats stats, Exception error)
        {
            if (sink == null || stats == null) return;
            double elapsed = stats.ElapsedMs(Stopwatch.GetTimestamp());
            sink.Write(clock.Elapsed.TotalSeconds, "copy",
                "ms", elapsed,
                "spellbooks", stats.Spellbooks,
                "known_spells", stats.KnownSpells,
                "special_lists", stats.SpecialLists,
                "add_known", stats.AddKnown,
                "add_known_learn", stats.LearnAddKnown,
                "add_known_duplicate", stats.DuplicateAddKnown,
                "add_known_skipped", stats.SkippedAddKnown,
                "add_special", stats.AddSpecial,
                "add_base_level", stats.AddBaseLevel,
                "add_known_ms", stats.AddKnownMs,
                "add_special_ms", stats.AddSpecialMs,
                "learn_ms", stats.LearnMs,
                "redundant_share", stats.RedundantShare,
                "area", Area(),
                "error", error == null ? null : error.GetType().Name);
        }

        private void WriteLoading(string kind, object stage, double milliseconds, Exception error)
        {
            if (sink == null) return;
            sink.Write(clock.Elapsed.TotalSeconds, kind,
                "stage", stage?.GetType().FullName,
                "ms", milliseconds,
                "area", Area(),
                "error", error == null ? null : error.GetType().FullName);
        }

        private void EmitMemory(string reason)
        {
            if (sink == null) return;
            try
            {
                process?.Refresh();
                long privateBytes = 0, workingSet = 0;
                try { privateBytes = process?.PrivateMemorySize64 ?? 0; workingSet = process?.WorkingSet64 ?? 0; } catch { }
                sink.Write(clock.Elapsed.TotalSeconds, "memory",
                    "reason", reason,
                    "private_bytes", privateBytes,
                    "working_set_bytes", workingSet,
                    "gc0", GC.CollectionCount(0),
                    "gc1", GC.CollectionCount(1),
                    "gc2", GC.CollectionCount(2),
                    "unity_allocated", Profiler.GetTotalAllocatedMemoryLong(),
                    "unity_reserved", Profiler.GetTotalReservedMemoryLong(),
                    "mono_used", Profiler.GetMonoUsedSizeLong(),
                    "mono_heap", Profiler.GetMonoHeapSizeLong(),
                    "area", Area());
            }
            catch { }
        }

        private string Area()
        {
            try
            {
                if (gameType == null) return "unavailable";
                object game = AccessTools.Field(gameType, "s_Instance")?.GetValue(null);
                if (game == null) return "unavailable";
                object area = AccessTools.Property(gameType, "CurrentlyLoadedArea")?.GetValue(game, null);
                return area?.ToString() ?? "none";
            }
            catch { return "unavailable"; }
        }

        private void Record(MethodBase method, double milliseconds, bool failed)
        {
            lock (gate)
            {
                if (!timings.TryGetValue(method, out TimingAggregate value))
                    timings[method] = value = new TimingAggregate();
                value.Add(milliseconds, failed);
            }
        }

        private void FlushTimings()
        {
            if (sink == null) return;
            lock (gate)
            {
                foreach (KeyValuePair<MethodBase, TimingAggregate> item in timings)
                {
                    TimingAggregate value = item.Value;
                    sink.Write(clock.Elapsed.TotalSeconds, "timing",
                        "method", Label(item.Key),
                        "calls", value.Count,
                        "total_ms", value.TotalMs,
                        "max_ms", value.MaxMs,
                        "exceptions", value.Exceptions);
                }
                timings.Clear();
            }
            sink.Flush();
        }

        private static string Label(MethodBase method)
        {
            string type = method.DeclaringType?.FullName ?? "?";
            return type + "." + method.Name + "/" + method.GetParameters().Length;
        }

        // ---- probes ----------------------------------------------------------------------------

        private void InstallProbes()
        {
            Probe("Kingmaker.UnitLogic.UnitHelper", "CreatePreview", "Copy", "CopyFacts", "CopyItems");
            Probe("Kingmaker.UnitLogic.Class.LevelUp.LevelUpController", ".ctor", "Start", "UpdatePreview", "Commit", "Stop", "ApplyLevelUpActions");
            Probe("Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main.UnitProgressionVM", ".ctor", "RefreshData", "DisposeImplementation");
            Probe("Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main.ClassProgressionVM", ".ctor");
            Probe("Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main.ProgressionVM", ".ctor");
            Probe("Kingmaker.UI.MVVM._VM.CharGen.CharGenVM", ".ctor", "DisposeImplementation");
            Probe("Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector.CharGenFeatureSelectorStateVM", "CreateFeaturesList", "UpdateFeatureState");
            Probe("Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.CharacterInfoVM", ".ctor", "CreateVMs", "DisposeImplementation");
            Probe("Kingmaker.EntitySystem.Persistence.AreaDataStash", "UnstashAreaState", "StashAreaState");
            Probe("Kingmaker.EntitySystem.Persistence.Scenes.SceneLoader", "MatchStateWithScene", "LoadSceneEntities");
            Probe("Kingmaker.EntitySystem.Persistence.AreaPersistentState", "PostLoad", "TurnOn");
            Probe("Kingmaker.EntitySystem.SceneEntitiesState", "PostLoad", "TurnOn");
            Probe("Kingmaker.ResourceLinks.ResourcesPreload", "PreloadUnitViews", "PreloadMapObjectViews");
            Probe("Kingmaker.Sound.SoundBanksManager", "LoadBanks", "MarkBanksToUnload", "UnloadNotUsed", "TryToUnloadBanks");
            Probe("Kingmaker.Blueprints.ResourcesLibrary", "CleanupLoadedCache", "StartPreloadingMode", "StopPreloadingMode");
            Probe("Kingmaker.BundlesLoading.BundlesLoadService", "RequestBundle");

            PatchSpecial(typeof(UnitHelper), "CopySpellbook", "CopySpellbookPrefix", "CopySpellbookFinalizer");
            PatchSpecial(typeof(Spellbook), "AddKnown", "AddKnownPrefix", "AddKnownFinalizer");
            PatchSpecial(typeof(Spellbook), "AddSpecial", "AddSpecialPrefix", "AddSpecialFinalizer");
            PatchSpecial(typeof(Spellbook), "AddBaseLevel", "AddBaseLevelPrefix", "AddBaseLevelFinalizer");
            InstallLoadingProbes();
            if (sink != null)
            {
                sink.Write(clock.Elapsed.TotalSeconds, "probes",
                    "count", missing.Count,
                    "missing", string.Join("; ", missing));
                sink.Flush();
            }
        }

        private void Probe(string typeName, params string[] names)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null) { missing.Add("type:" + typeName); return; }
            foreach (string name in names)
            {
                IEnumerable<MethodBase> targets = name == ".ctor"
                    ? type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    : type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name == name && !m.ContainsGenericParameters && m.GetMethodBody() != null);
                bool found = false;
                foreach (MethodBase method in targets)
                {
                    found = true;
                    try
                    {
                        harmony.Patch(method,
                            prefix: new HarmonyMethod(typeof(ProbeHost).GetMethod(nameof(TimingPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                            finalizer: new HarmonyMethod(typeof(ProbeHost).GetMethod(nameof(TimingFinished), BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                    catch (Exception ex) { missing.Add(Label(method) + ":" + ex.GetType().Name); }
                }
                if (!found) missing.Add("method:" + typeName + "." + name);
            }
        }

        private void PatchSpecial(Type type, string name, string prefix, string finalizer)
        {
            try
            {
                MethodBase target = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == name && !m.ContainsGenericParameters);
                if (target == null) { missing.Add("method:" + type.FullName + "." + name); return; }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(ProbeHost).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(ProbeHost).GetMethod(finalizer, BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex) { missing.Add(type.FullName + "." + name + ":" + ex.GetType().Name); }
        }

        private void InstallLoadingProbes()
        {
            if (loadingProcessType == null) { missing.Add("type:LoadingProcess"); return; }
            try
            {
                loadingCurrent = AccessTools.Field(loadingProcessType, "m_CurrentLoadingProcess");
                loadingTimer = AccessTools.Field(loadingProcessType, "m_OneTaskStopwatch");
                MethodInfo start = AccessTools.DeclaredMethod(loadingProcessType, "StartLoadingProcessInternal");
                MethodInfo tick = AccessTools.DeclaredMethod(loadingProcessType, "TickLoading");
                if (start == null || tick == null || loadingCurrent == null || loadingTimer == null)
                {
                    missing.Add("loading:fields");
                    return;
                }
                harmony.Patch(start, postfix: new HarmonyMethod(typeof(ProbeHost).GetMethod(nameof(LoadingStarted), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(tick,
                    prefix: new HarmonyMethod(typeof(ProbeHost).GetMethod(nameof(LoadingPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(ProbeHost).GetMethod(nameof(LoadingFinished), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex) { missing.Add("loading:" + ex.GetType().Name); }
        }

        // ---- patch bodies (static, invoked by Harmony) -----------------------------------------

        private static void TimingPrefix(ref long __state)
        {
            if (instance?.enabled == true) __state = Stopwatch.GetTimestamp();
        }

        // A void finalizer observes the exception; it never replaces it or suppresses the throw.
        private static void TimingFinished(MethodBase __originalMethod, long __state, Exception __exception)
        {
            if (__state == 0) return;
            try
            {
                ProbeHost host = instance;
                if (host == null) return;
                double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
                host.Record(__originalMethod, ms, __exception != null);
            }
            catch { }
        }

        private static void CopySpellbookPrefix(UnitEntityData original, UnitEntityData target)
        {
            ProbeHost host = instance;
            if (host == null || !host.enabled || pendingCopy != null) return;
            try
            {
                int spellbooks = 0, known = 0, specialLists = 0;
                foreach (Spellbook book in original.Descriptor.Spellbooks)
                {
                    spellbooks++;
                    specialLists += book.SpecialLists.Count;
                    known += book.GetAllKnownSpells().Count();
                }
                pendingCopy = CopyScope.Begin(spellbooks, known, specialLists, Stopwatch.GetTimestamp());
            }
            catch { }
        }

        private static void CopySpellbookFinalizer(Exception __exception)
        {
            ProbeHost host = instance;
            CopyStats stats = pendingCopy;
            pendingCopy = null;
            if (host == null || stats == null) return;
            try
            {
                if (ReferenceEquals(CopyScope.Current, stats)) CopyScope.End();
                host.EmitCopy(stats, __exception);
            }
            catch { CopyScope.Reset(); }
        }

        private static bool AddKnownPrefix(Spellbook __instance, int spellLevel, BlueprintAbility spell, ref long __state, ref AbilityData __result)
        {
            ProbeHost host = instance;
            if (host == null || !host.enabled || !CopyScope.Active) return true;
            try
            {
                if (__instance == null || spell == null || spellLevel < 0) return true;
                __state = Stopwatch.GetTimestamp();
                bool duplicate = __instance.IsKnownOnLevel(spell, spellLevel);
                CopyScope.NoteAddKnown(duplicate);
                if (duplicate && host.skipKnownReadd && !CopyScope.InLearn)
                {
                    __result = FindKnown(__instance, spellLevel, spell);
                    CopyScope.NoteSkipped();
                    return false;
                }
            }
            catch { }
            return true;
        }

        private static void AddKnownFinalizer(long __state)
        {
            if (__state == 0) return;
            try { CopyScope.AddKnownTime((Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency); } catch { }
        }

        private static void AddSpecialPrefix(ref long __state)
        {
            if (instance?.enabled != true || !CopyScope.Active) return;
            __state = Stopwatch.GetTimestamp();
            CopyScope.NoteAddSpecial();
        }

        private static void AddSpecialFinalizer(long __state)
        {
            if (__state == 0) return;
            try { CopyScope.AddSpecialTime((Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency); } catch { }
        }

        private static void AddBaseLevelPrefix(ref long __state)
        {
            if (instance?.enabled != true || !CopyScope.Active) return;
            __state = Stopwatch.GetTimestamp();
            CopyScope.EnterLearn();
            CopyScope.NoteBaseLevel();
        }

        private static void AddBaseLevelFinalizer(long __state)
        {
            if (__state == 0) return;
            try { CopyScope.AddLearnTime((Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency); } catch { }
            CopyScope.ExitLearn();
        }

        private static AbilityData FindKnown(Spellbook book, int spellLevel, BlueprintAbility spell)
        {
            try
            {
                List<AbilityData> list = book.GetKnownSpells(spellLevel);
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].Blueprint == spell) return list[i];
            }
            catch { }
            return null;
        }

        private struct LoadingCall
        {
            internal object Stage;
            internal Stopwatch Timer;
        }

        private static void LoadingPrefix(object __instance, ref LoadingCall __state)
        {
            ProbeHost host = instance;
            if (host == null || !host.enabled) return;
            try
            {
                __state.Stage = host.loadingCurrent.GetValue(__instance);
                __state.Timer = host.loadingTimer.GetValue(__instance) as Stopwatch;
            }
            catch { }
        }

        private static void LoadingStarted(object __instance)
        {
            ProbeHost host = instance;
            if (host == null || !host.enabled) return;
            try
            {
                host.loadingInstance = new WeakReference(__instance);
                host.EmitMemory("loading_start");
                host.WriteLoading("loading_start", host.loadingCurrent?.GetValue(__instance), 0, null);
            }
            catch { }
        }

        private static void LoadingFinished(object __instance, LoadingCall __state, Exception __exception)
        {
            ProbeHost host = instance;
            if (host == null || !host.enabled || __state.Stage == null) return;
            try
            {
                object current = host.loadingCurrent?.GetValue(__instance);
                if (__exception == null && ReferenceEquals(__state.Stage, current)) return;
                double ms = __state.Timer == null ? 0 : __state.Timer.Elapsed.TotalMilliseconds;
                host.WriteLoading("loading", __state.Stage, ms, __exception);
            }
            catch { }
        }
    }
}
