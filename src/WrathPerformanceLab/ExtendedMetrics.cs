using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace WrathPerformanceLab
{
    internal static class ExtendedMetrics
    {
        private static double Now => Main.ElapsedSeconds;
        private static WeakLedger controllers = new WeakLedger(4096), previews = new WeakLedger(8192);
        private static GraphicsTrace graphics = new GraphicsTrace();
        private static readonly Queue<string> previewIds = new Queue<string>();
        private static readonly HashSet<string> knownPreviewIds = new HashSet<string>(StringComparer.Ordinal);
        private static long previewIdsEvicted;
        private static BoundedCsv file, graphicsFile;
        private static Type servicesType, fabricType, controllerType, dxtType, atlasType, entityType, settingsType;
        private static readonly Dictionary<string, string> probeStatus = new Dictionary<string, string>();
        private static volatile bool active;
        private static bool requested;
        private static double next, last;
        private static string reason;
        private static WeakReference settingsInstance;
        private static Action<string> report;
        internal static void Open(string folder, Action<string> reportFailure)
        {
            report = reportFailure;
            file = new BoundedCsv(Path.Combine(folder, "diagnostics.csv"), 8 * 1024 * 1024,
                "elapsed_s", "reason", "group", "metric", "value", "status");
            graphicsFile = new BoundedCsv(Path.Combine(folder, "graphics.csv"), 8 * 1024 * 1024,
                "elapsed_s", "event", "request_id", "observed_age_s", "age_is_lower_bound", "worker_observed_state",
                "since_worker_start_s", "readback_callbacks_observed", "raw_complete", "has_error",
                "mip_descriptors", "descriptor_bytes_not_live_allocation", "exception_type");
            servicesType = AccessTools.TypeByName("Owlcat.Runtime.Core.Utils.Locator.Services");
            fabricType = AccessTools.TypeByName("Owlcat.Runtime.UniRx.LastValueOnLateUpdateObservableFabric");
            controllerType = AccessTools.TypeByName("Kingmaker.UnitLogic.Class.LevelUp.LevelUpController");
            dxtType = AccessTools.TypeByName("Owlcat.Runtime.Visual.Dxt.DxtCompressorService");
            atlasType = AccessTools.TypeByName("Kingmaker.Visual.CharacterSystem.CharacterAtlasService");
            entityType = AccessTools.TypeByName("Kingmaker.EntitySystem.EntityService");
            settingsType = AccessTools.TypeByName("VisualAdjustments2.Infrastructure.GlobalCharacterSettings");
        }
        private static void Patch(Harmony harmony, MethodBase target, string label, string prefix = null, string postfix = null, string finalizer = null)
        {
            if (target == null) { probeStatus[label] = "missing"; return; }
            try
            {
                harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(ExtendedMetrics), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(ExtendedMetrics), postfix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(typeof(ExtendedMetrics), finalizer));
                probeStatus[label] = "installed: " + target.DeclaringType.FullName + "." + target.Name;
            }
            catch (Exception ex) { probeStatus[label] = "skipped: " + ex.GetType().Name; }
        }
        internal static void Install(Harmony harmony, TextWriter log)
        {
            active = true;
            foreach (ConstructorInfo ctor in controllerType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? new ConstructorInfo[0])
                Patch(harmony, ctor, "controller_ctor_" + ctor.MetadataToken, postfix: nameof(ControllerCreated));
            Patch(harmony, controllerType == null ? null : AccessTools.DeclaredMethod(controllerType, "Stop"), "controller_stop", finalizer: nameof(LifecycleEnded));
            Type helper = AccessTools.TypeByName("Kingmaker.UnitLogic.UnitHelper");
            MethodInfo preview = helper?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "CreatePreview" && m.ReturnType != typeof(void) && !m.ReturnType.IsValueType);
            Patch(harmony, preview, "preview_created", postfix: nameof(PreviewCreated));
            Type request = dxtType?.GetNestedType("Request", BindingFlags.Public | BindingFlags.NonPublic);
            Patch(harmony, request == null ? null : AccessTools.DeclaredMethod(request, "Run"), "dxt_run", prefix: nameof(RequestRun));
            Patch(harmony, request == null ? null : AccessTools.DeclaredMethod(request, "OnReadbackDone"), "dxt_readback", finalizer: nameof(ReadbackFinished));
            Patch(harmony, request == null ? null : AccessTools.DeclaredMethod(request, "Error"), "dxt_error", finalizer: nameof(RequestError));
            Patch(harmony, request == null ? null : AccessTools.DeclaredMethod(request, "Clear"), "dxt_clear", prefix: nameof(RequestClear));
            // Validate the compiler generated worker by semantic signature and captured Request field.
            var workers = request == null ? new MethodInfo[0] : request.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(f => f.Name == "<>4__this" && f.FieldType == request))
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.Name.StartsWith("<OnReadbackDone>b__", StringComparison.Ordinal) && m.ReturnType == typeof(void) && m.GetParameters().Length == 0).ToArray();
            Patch(harmony, workers.Length == 1 ? workers[0] : null, "dxt_worker", prefix: nameof(WorkerStarted), finalizer: nameof(WorkerFinished));
            Patch(harmony, settingsType == null ? null : AccessTools.DeclaredMethod(settingsType, "ForCharacter"), "va2_for_character", prefix: nameof(SettingsBefore), finalizer: nameof(SettingsAfter));
            foreach (string typeName in new[] { "Kingmaker.UI.MVVM._VM.CharGen.CharGenVM", "Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.CharacterInfoVM" })
            {
                Type type = AccessTools.TypeByName(typeName);
                Patch(harmony, type == null ? null : AccessTools.DeclaredMethod(type, "DisposeImplementation"), "ui_closed_" + typeName, finalizer: nameof(LifecycleEnded));
            }
            foreach (var pair in probeStatus) log.WriteLine("EXTENDED " + pair.Key + " " + pair.Value);
            RequestSample("enabled");
        }
        internal static void SetActive(bool value) { active = value; }
        internal static void RequestSample(string value) { reason = value; requested = true; }
        public static void ControllerCreated(object __instance)
        {
            if (!active) return;
            try { controllers.Track(__instance, Now); RequestSample("controller_created"); } catch { }
        }
        public static void PreviewCreated(object __result)
        {
            if (!active) return;
            try
            {
                previews.Track(__result, Now);
                if (Inspect.Auto(__result, "UniqueId") is string id && knownPreviewIds.Add(id))
                {
                    if (previewIds.Count == 8192) { knownPreviewIds.Remove(previewIds.Dequeue()); previewIdsEvicted++; }
                    previewIds.Enqueue(id);
                }
                RequestSample("preview_created");
            }
            catch { }
        }
        public static void LifecycleEnded() { if (active) RequestSample("ui_or_controller_closed"); }
        private static void Observe(object request, string kind, Exception error = null)
        {
            if (!active) return;
            try { graphics.Observe(request, kind, Now, error); } catch { }
        }
        public static void RequestRun(object __instance) { Observe(__instance, "run"); }
        public static void ReadbackFinished(object __instance, Exception __exception) { Observe(__instance, "readback", __exception); }
        public static void RequestError(object __instance, Exception __exception) { Observe(__instance, "error", __exception); }
        public static void RequestClear(object __instance) { Observe(__instance, "before_clear"); }
        public static void WorkerStarted(object __instance) { try { Observe(Inspect.Field(__instance, "<>4__this"), "worker_start"); } catch { } }
        public static void WorkerFinished(object __instance, Exception __exception) { try { Observe(Inspect.Field(__instance, "<>4__this"), "worker_end", __exception); } catch { } }
        public struct SettingsCall { public int Count; public bool Preview; }
        public static void SettingsBefore(object __instance, object __0, ref SettingsCall __state)
        {
            if (!active) return;
            try
            {
                if (settingsInstance == null || !ReferenceEquals(settingsInstance.Target, __instance)) settingsInstance = new WeakReference(__instance);
                __state.Count = Inspect.Count(Inspect.Field(__instance, "PerCharacter")); __state.Preview = previews.Contains(__0);
            }
            catch { __state.Count = -1; }
        }
        public static void SettingsAfter(object __instance, SettingsCall __state)
        {
            if (!active) return;
            try
            {
                int count = Inspect.Count(Inspect.Field(__instance, "PerCharacter"));
                if (count > __state.Count && __state.Count >= 0)
                {
                    settingsInsertions += count - __state.Count;
                    if (__state.Preview) previewInsertions += count - __state.Count;
                    RequestSample("va2_settings_added");
                }
            }
            catch { }
        }
        private static long settingsInsertions, previewInsertions;
        private static object Service(Type type)
        {
            object gate = Inspect.Static(servicesType, "s_Lock");
            if (gate == null) return null;
            lock (gate) return Inspect.ExistingService(Inspect.Static(servicesType, "s_RegisteredServices") as IDictionary, type);
        }
        private static void Emit(double now, string why, string group, Dictionary<string, object> values)
        {
            bool partial = values.TryGetValue("scan_truncated", out object truncated) && truncated is bool b && b;
            foreach (var pair in values) file.Row(now, why, group, pair.Key, pair.Value, partial ? "partial" : "observed");
        }
        private static void Group(double now, string why, string group, Func<Dictionary<string, object>> capture)
        {
            try
            {
                var data = capture();
                if (data == null) file.Row(now, why, group, "availability", "", "unavailable");
                else Emit(now, why, group, data);
            }
            catch (Exception ex) { file.Row(now, why, group, "capture_error", ex.GetType().Name, "unavailable"); }
        }
        internal static void Tick(bool loading)
        {
            if (!active || file == null) return;
            double now = Now;
            if (now < next && (!requested || now - last < 1)) return;
            string why = requested ? reason : loading ? "loading_1s" : "periodic_15s";
            requested = false; last = now; next = now + (loading ? 1 : 15);
            try
            {
                foreach (var pair in probeStatus) file.Row(now, why, "probes", pair.Key, pair.Value, "status");
                Group(now, why, "controllers", () => controllers.Capture(now));
                Group(now, why, "previews", () => previews.Capture(now));
                Group(now, why, "late_update", () => {
                    object nodes = Inspect.Field(Inspect.Static(fabricType, "s_Instance"), "m_ObservablesToLateUpdate");
                    return nodes == null ? null : RootInspection.Capture(nodes, controllerType, new ScanBudget());
                });
                Group(now, why, "va2", () => {
                    // Observe a real call first; reading the optional Mod's static Instance can run its initializer.
                    var settings = Inspect.Field(settingsInstance?.Target, "PerCharacter") as IDictionary;
                    if (settings == null) return null;
                    var data = RetentionInspection.Capture(settings, Inspect.Field(Service(entityType), "m_Entities") as IDictionary, previews, new ScanBudget());
                    data["insertions_observed"] = settingsInsertions; data["insertions_for_known_previews"] = previewInsertions;
                    int matched = 0;
                    foreach (string id in knownPreviewIds) if (settings.Contains(id)) matched++;
                    data["settings_with_observed_preview_id"] = matched; data["preview_ids_tracked"] = knownPreviewIds.Count;
                    data["preview_ids_evicted"] = previewIdsEvicted;
                    return data;
                });
                Group(now, why, "dxt", () => {
                    var queue = Inspect.Field(Service(dxtType), "m_Requests") as IList;
                    var budget = new ScanBudget(512);
                    foreach (object[] row in graphics.Capture(queue, now, budget)) graphicsFile.Row(row);
                    var data = graphics.Summary(); data["queue_count"] = queue?.Count ?? -1; data["scan_truncated"] = budget.Truncated;
                    data["queue_available"] = queue != null; return data;
                });
                Group(now, why, "atlas", () => {
                    object service = Service(atlasType);
                    if (service == null) return null;
                    Type character = AccessTools.TypeByName("Kingmaker.Visual.CharacterSystem.Character");
                    Type atlas = AccessTools.TypeByName("Kingmaker.Visual.CharacterSystem.CharacterAtlas");
                    return new Dictionary<string, object> { ["queue_count"] = Inspect.Count(Inspect.Field(service, "m_Requests")),
                        ["characters_wait_calculated"] = Inspect.Static(character, "WaitCalculatedCount") ?? -1,
                        ["occupied_build_budget"] = Inspect.Static(atlas, "s_OccupiedBuildBudget") ?? -1 };
                });
                file.Row(now, why, "sampler", "capture_ms", (Now - now) * 1000, "observed");
                file.Flush(); graphicsFile.Flush();
            }
            catch (Exception ex)
            {
                active = false;
                try { report?.Invoke("Extended telemetry stopped: " + ex.GetType().Name); } catch { }
            }
        }
        internal static void Close()
        {
            active = false;
            try { file?.Dispose(); } catch { }
            try { graphicsFile?.Dispose(); } catch { }
        }
    }
}
