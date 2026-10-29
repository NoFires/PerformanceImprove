using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.Profiling;
using UnityModManagerNet;

namespace WrathPerformanceLab
{
    public static class Main
    {
        private const string Id = "WrathPerformanceLab";
        private static readonly object Gate = new object();
        private static readonly Dictionary<MethodBase, TimingAggregate> Timings = new Dictionary<MethodBase, TimingAggregate>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;
        private static BoundedCsv timingFile, memoryFile, loadingFile;
        private static Process process;
        private static StreamWriter patchFile;
        private static string output;
        private static volatile bool enabled;
        private static double nextSample;
        private static bool ioFailed;
        private static FieldInfo loadingCurrent, loadingTimer;
        private static Type gameType, resourceType, bundlesType, eventBusType;
        private static WeakReference loadingInstance;
        internal static double ElapsedSeconds => Clock.Elapsed.TotalSeconds;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            entry = modEntry;
            try
            {
                output = Path.Combine(entry.Path, "Logs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(output);
                const long cap = 8 * 1024 * 1024;
                timingFile = new BoundedCsv(Path.Combine(output, "timing.csv"), cap,
                    "elapsed_s", "method", "calls_in_window", "inclusive_total_ms", "inclusive_max_ms", "exceptions_in_window");
                memoryFile = new BoundedCsv(Path.Combine(output, "memory.csv"), cap,
                    "elapsed_s", "reason", "private_bytes", "working_set_bytes", "managed_live_estimate_bytes",
                    "unity_allocated_bytes", "unity_reserved_bytes", "mono_used_bytes", "mono_heap_bytes",
                    "gc0_cumulative", "gc1_cumulative", "gc2_cumulative", "resource_cache_entries", "bundle_cache_entries", "loaded_bundle_count",
                    "global_subscriber_slots", "unit_subscriber_groups", "area");
                loadingFile = new BoundedCsv(Path.Combine(output, "loading.csv"), cap,
                    "elapsed_s", "event", "stage", "wall_ms_including_wait", "area", "exception_type");
                patchFile = new StreamWriter(Path.Combine(output, "patches.txt"), false);
                patchFile.WriteLine("WrathPerformanceLab 0.2.0 diagnostics; session UTC " + DateTime.UtcNow.ToString("O"));
                patchFile.WriteLine("Timing rows are inclusive, aggregated over windows. Loading wall time includes waits. Memory samples do not force GC.");
                process = Process.GetCurrentProcess();
                harmony = new Harmony(Id);
                gameType = AccessTools.TypeByName("Kingmaker.Game");
                resourceType = AccessTools.TypeByName("Kingmaker.Blueprints.ResourcesLibrary");
                bundlesType = AccessTools.TypeByName("Kingmaker.BundlesLoading.BundlesLoadService");
                eventBusType = AccessTools.TypeByName("Kingmaker.PubSubSystem.EventBus");
                entry.OnUpdate = Update;
                entry.OnToggle = Toggle;
                entry.OnUnload = Unload;
                enabled = true;
                ExtendedMetrics.Open(output, message => entry.Logger.Log(message));
                InstallProbes();
                SampleMemory("mod_loaded");
                entry.Logger.Log("Diagnostics only; output: " + output);
                return true;
            }
            catch (Exception ex)
            {
                enabled = false;
                harmony?.UnpatchAll(Id);
                CloseFiles();
                entry.Logger.LogException(ex);
                return false;
            }
        }

        private static void InstallProbes()
        {
            Probe("Kingmaker.UnitLogic.Class.LevelUp.LevelUpController", ".ctor", "Start", "UpdatePreview", "ApplyLevelUpActions", "Commit", "Stop");
            Probe("Kingmaker.UnitLogic.UnitHelper", "CreatePreview", "Copy", "CopyFacts", "CopySpellbook", "CopyItems");
            Probe("Kingmaker.UI.MVVM._VM.CharGen.CharGenVM", ".ctor", "DisposeImplementation");
            Probe("Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector.CharGenFeatureSelectorStateVM", "CreateFeaturesList", "UpdateFeatureState", "UpdateFeaturesAvailibleState");
            Probe("Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.CharacterInfoVM", ".ctor", "CreateVMs", "CreateVM", "DisposeImplementation");
            Probe("Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main.UnitProgressionVM", ".ctor", "RefreshData", "DisposeImplementation");
            Probe("Kingmaker.EntitySystem.Persistence.AreaDataStash", "UnstashAreaState", "UnstashAreaSubState", "StashAreaState");
            Probe("Kingmaker.EntitySystem.Persistence.Scenes.SceneLoader", "MatchStateWithScene", "LoadSceneEntities");
            Probe("Kingmaker.Blueprints.ResourcesLibrary", "CleanupLoadedCache");
            Probe("Kingmaker.BundlesLoading.BundlesLoadService", "RequestBundle");
            Type type = AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.LoadingProcess");
            if (type != null)
            {
                loadingCurrent = AccessTools.Field(type, "m_CurrentLoadingProcess");
                loadingTimer = AccessTools.Field(type, "m_OneTaskStopwatch");
                MethodInfo start = AccessTools.DeclaredMethod(type, "StartLoadingProcessInternal");
                MethodInfo tick = AccessTools.DeclaredMethod(type, "TickLoading");
                if (start != null && tick != null && loadingCurrent != null && loadingTimer != null)
                {
                    harmony.Patch(start, postfix: new HarmonyMethod(typeof(Main), nameof(LoadingStarted)));
                    harmony.Patch(tick, prefix: new HarmonyMethod(typeof(Main), nameof(LoadingPrefix)),
                        finalizer: new HarmonyMethod(typeof(Main), nameof(LoadingFinished)));
                    DumpPatches(start);
                    DumpPatches(tick);
                }
                else patchFile.WriteLine("SKIPPED loading lifecycle: expected signature/fields unavailable");
            }
            ExtendedMetrics.Install(harmony, patchFile);
            patchFile.Flush();
        }

        private static void Probe(string typeName, params string[] names)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null) { patchFile.WriteLine("MISSING TYPE " + typeName); return; }
            foreach (string name in names)
            {
                IEnumerable<MethodBase> targets = name == ".ctor"
                    ? type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Cast<MethodBase>()
                    : type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name == name && !m.ContainsGenericParameters && m.GetMethodBody() != null).Cast<MethodBase>();
                bool found = false;
                foreach (MethodBase method in targets)
                {
                    found = true;
                    try
                    {
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(TimingPrefix)),
                            finalizer: new HarmonyMethod(typeof(Main), nameof(TimingFinished)));
                        DumpPatches(method);
                    }
                    catch (Exception ex) { patchFile.WriteLine("SKIPPED " + Label(method) + ": " + ex.GetType().Name + " " + ex.Message); }
                }
                if (!found) patchFile.WriteLine("MISSING METHOD " + typeName + "." + name);
            }
        }

        private static string Label(MethodBase method) { return method.DeclaringType.FullName + "." + method; }

        private static void DumpPatches(MethodBase method)
        {
            Patches info = Harmony.GetPatchInfo(method);
            patchFile.WriteLine("METHOD " + Label(method));
            if (info == null) return;
            foreach (var group in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                foreach (Patch patch in group)
                    patchFile.WriteLine("  " + patch.owner + " priority=" + patch.priority + " " + patch.PatchMethod.DeclaringType.FullName + "." + patch.PatchMethod.Name);
        }

        public static void TimingPrefix(ref long __state) { __state = enabled ? Stopwatch.GetTimestamp() : 0; }

        // A void finalizer observes the exception; it never returns a replacement or suppresses it.
        public static void TimingFinished(MethodBase __originalMethod, long __state, Exception __exception)
        {
            if (__state == 0 || !enabled) return;
            try
            {
                double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
                lock (Gate)
                {
                    if (!Timings.TryGetValue(__originalMethod, out TimingAggregate value))
                        Timings[__originalMethod] = value = new TimingAggregate();
                    value.Add(ms, __exception != null);
                }
            }
            catch { /* Telemetry must not alter the original method's outcome. */ }
        }

        public struct LoadingCall
        {
            public object Stage;
            public Stopwatch Timer;
        }

        public static void LoadingPrefix(object __instance, ref LoadingCall __state)
        {
            if (!enabled) return;
            try
            {
                __state.Stage = loadingCurrent.GetValue(__instance);
                __state.Timer = loadingTimer.GetValue(__instance) as Stopwatch;
            }
            catch { }
        }

        public static void LoadingStarted(object __instance)
        {
            if (!enabled || ioFailed) return;
            try
            {
                loadingInstance = new WeakReference(__instance);
                ExtendedMetrics.RequestSample("loading_start");
                object stage = loadingCurrent.GetValue(__instance);
                lock (Gate)
                {
                    loadingFile.Row(Clock.Elapsed.TotalSeconds, "start", stage?.GetType().FullName, "", Area(), "");
                    loadingFile.Flush();
                }
                SampleMemory("loading_start");
            }
            catch (Exception ex) { DisableIo(ex); }
        }

        public static void LoadingFinished(object __instance, LoadingCall __state, Exception __exception)
        {
            if (!enabled || ioFailed || __state.Stage == null) return;
            try
            {
                if (__exception == null && ReferenceEquals(__state.Stage, loadingCurrent.GetValue(__instance))) return;
                ExtendedMetrics.RequestSample("loading_stage_ended");
                lock (Gate)
                {
                    loadingFile.Row(Clock.Elapsed.TotalSeconds, __exception == null ? "end" : "exception", __state.Stage.GetType().FullName,
                        __state.Timer == null ? (object)"" : __state.Timer.Elapsed.TotalMilliseconds, Area(), __exception?.GetType().FullName);
                    loadingFile.Flush();
                }
            }
            catch (Exception ex) { DisableIo(ex); }
        }

        private static object StaticProperty(Type type, string name) { return type == null ? null : AccessTools.Property(type, name)?.GetValue(null, null); }
        private static object Field(object obj, string name) { return obj == null ? null : AccessTools.Field(obj.GetType(), name)?.GetValue(obj); }
        private static int Count(object obj) { return obj is ICollection collection ? collection.Count : -1; }
        private static object StaticField(Type type, string name) { return type == null ? null : AccessTools.Field(type, name)?.GetValue(null); }
        private static string Area()
        {
            try
            {
                object game = StaticField(gameType, "s_Instance");
                return game == null ? "unavailable" : (AccessTools.Property(gameType, "CurrentlyLoadedArea")?.GetValue(game, null)?.ToString() ?? "none");
            }
            catch { return "unavailable"; }
        }

        private static void SampleMemory(string reason)
        {
            if (ioFailed) return;
            try
            {
                process.Refresh();
                // Read existing instances only. Game.Instance and service getters can initialize game state.
                object proxy = StaticField(bundlesType, "s_Proxy");
                object bundles = Inspect.Auto(proxy, "Service");
                object bundleCache = Field(bundles, "m_Bundles");
                int loadedBundles = -1;
                if (bundleCache is IDictionary bundleEntries)
                {
                    loadedBundles = 0;
                    foreach (DictionaryEntry item in bundleEntries)
                        if (Field(item.Value, "Bundle") is UnityEngine.Object bundle && bundle) loadedBundles++;
                }
                object global = StaticField(eventBusType, "GlobalSubscribers");
                int slots = 0;
                if (Field(global, "m_Listeners") is IDictionary listeners)
                    foreach (DictionaryEntry item in listeners)
                        slots += Math.Max(0, Count(Field(item.Value, "List")));
                else slots = -1;
                lock (Gate)
                {
                    memoryFile.Row(Clock.Elapsed.TotalSeconds, reason, process.PrivateMemorySize64, process.WorkingSet64,
                        GC.GetTotalMemory(false), Profiler.GetTotalAllocatedMemoryLong(), Profiler.GetTotalReservedMemoryLong(),
                        Profiler.GetMonoUsedSizeLong(), Profiler.GetMonoHeapSizeLong(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                        Count(StaticField(resourceType, "s_LoadedResources")), Count(bundleCache), loadedBundles, slots,
                        Count(StaticField(eventBusType, "UnitSubscribers")), Area());
                    memoryFile.Flush();
                }
            }
            catch (Exception ex) { DisableIo(ex); }
        }

        private static void FlushTimings()
        {
            if (ioFailed) return;
            lock (Gate)
            {
                foreach (var item in Timings)
                {
                    TimingAggregate value = item.Value;
                    timingFile.Row(Clock.Elapsed.TotalSeconds, Label(item.Key), value.Count, value.TotalMs, value.MaxMs, value.Exceptions);
                }
                Timings.Clear();
                timingFile.Flush();
            }
        }

        private static void Update(UnityModManager.ModEntry modEntry, float delta)
        {
            if (!enabled) return;
            object loading = loadingInstance?.Target;
            bool isLoading = false;
            try { isLoading = loading != null && loadingCurrent?.GetValue(loading) != null; } catch { }
            ExtendedMetrics.Tick(isLoading);
            if (Clock.Elapsed.TotalSeconds < nextSample) return;
            nextSample = Clock.Elapsed.TotalSeconds + 15;
            try { FlushTimings(); SampleMemory("periodic_15s"); }
            catch (Exception ex) { DisableIo(ex); }
        }

        private static void DisableIo(Exception ex)
        {
            if (ioFailed) return;
            ioFailed = true;
            entry.Logger.Log("Telemetry stopped after error: " + ex.GetType().Name + ": " + ex.Message);
        }

        private static bool Toggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (enabled == value) return true;
            if (value)
            {
                try { enabled = true; InstallProbes(); nextSample = 0; }
                catch (Exception ex) { enabled = false; harmony.UnpatchAll(Id); entry.Logger.LogException(ex); return false; }
            }
            else
            {
                enabled = false;
                ExtendedMetrics.SetActive(false);
                harmony.UnpatchAll(Id);
                try { FlushTimings(); SampleMemory("disabled"); } catch (Exception ex) { DisableIo(ex); }
            }
            return true;
        }

        private static bool Unload(UnityModManager.ModEntry modEntry)
        {
            Toggle(modEntry, false);
            CloseFiles();
            return true;
        }

        private static void CloseFiles()
        {
            ExtendedMetrics.Close();
            try { timingFile?.Dispose(); } catch { }
            try { memoryFile?.Dispose(); } catch { }
            try { loadingFile?.Dispose(); } catch { }
            try { patchFile?.Dispose(); } catch { }
            process?.Dispose();
        }
    }
}
