using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints.Area;
using Kingmaker.EntitySystem.Persistence;
using UnityEngine;
using UnityModManagerNet;

namespace PerformanceImprove.LoadCleanup
{
    /// <summary>
    /// Game.LoadArea registers UnloadUnusedAssetsCoroutine twice for every transition.
    /// For a normal area transition (no SaveInfo) both are created before the new area is loaded,
    /// so the second repeats a full CleanupLoadedCache + GC.Collect + UnloadUnusedAssets pass with
    /// nothing new in between (2026-10-21 logs: 2.71-2.94 s per call).
    /// For a save load the first pass runs before SaveManager.LoadRoutine and the second after it;
    /// the first one only frees assets the second pass would free anyway, so it can be skipped on
    /// machines with enough memory (default on, gated at 12 GB).
    /// </summary>
    internal static class Main
    {
        private const string Owner = "PerformanceImprove.LoadCleanup";
        internal const int SaveLoadSkipMinimumMemoryMb = 12000;
        private static readonly Type[] LoadAreaSignature =
        {
            typeof(BlueprintArea), typeof(BlueprintAreaEnterPoint), typeof(AutoSaveMode),
            typeof(bool), typeof(SaveInfo), typeof(Action)
        };

        private static UnityModManager.ModEntry entry;
        private static Harmony harmony;
        private static bool enabled;
        private static volatile bool skipSaveLoadFirst;
        private static Type cleanupIteratorType;
        internal static long Skipped;
        internal static long SkippedSaveLoad;

        internal static bool Initialize(UnityModManager.ModEntry modEntry)
        {
            entry = modEntry;
            try
            {
                MethodInfo loadArea = AccessTools.Method(typeof(Game), "LoadArea", LoadAreaSignature);
                if (loadArea == null)
                    throw new MissingMethodException("Kingmaker.Game.LoadArea(BlueprintArea, BlueprintAreaEnterPoint, AutoSaveMode, bool, SaveInfo, Action)");
                MethodInfo unload = AccessTools.Method(typeof(Game), "UnloadUnusedAssetsCoroutine", Type.EmptyTypes);
                if (unload == null || unload.ReturnType != typeof(IEnumerator<object>))
                    throw new MissingMethodException("Kingmaker.Game.UnloadUnusedAssetsCoroutine(): IEnumerator<object>");
                // The factory call is inlined into LoadArea in the shipping build (the prefix below never
                // fires), so the skip is also applied where the loading manager receives each process.
                Type loadingType = AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.LoadingProcess");
                if (loadingType == null)
                    throw new TypeLoadException("Kingmaker.EntitySystem.Persistence.LoadingProcess");
                MethodInfo start = AccessTools.Method(loadingType, "StartLoadingProcess",
                    new[] { typeof(IEnumerator), typeof(Action), typeof(LoadingProcessTag), typeof(Func<IDisposable>) });
                if (start == null)
                    throw new MissingMethodException(loadingType.FullName + ".StartLoadingProcess(IEnumerator, Action, LoadingProcessTag, Func<IDisposable>)");
                cleanupIteratorType = FindCleanupIteratorType();
                if (cleanupIteratorType == null)
                    throw new MissingMethodException("Kingmaker.Game+<UnloadUnusedAssetsCoroutine>d__*");

                harmony = new Harmony(Owner);
                harmony.Patch(loadArea,
                    prefix: new HarmonyMethod(typeof(Main), nameof(LoadAreaPrefix)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(Main), nameof(LoadAreaPostfix)) { priority = Priority.Last });
                harmony.Patch(unload,
                    prefix: new HarmonyMethod(typeof(Main), nameof(UnloadPrefix)) { priority = Priority.First });
                harmony.Patch(start,
                    prefix: new HarmonyMethod(typeof(Main), nameof(StartProcessPrefix)) { priority = Priority.First });
                enabled = true;
                if (!TextureBurst.Initialize(entry, TextureBurst.Enabled, TextureBurst.Burst))
                    entry?.Logger.Log("Texture wait burst could not be installed; cleanup skip keeps working.");
                if (!SaveUpgrade.Initialize(entry, SaveUpgrade.Enabled))
                    entry?.Logger.Log("Save-upgrade fast path could not be installed; other features keep working.");
                entry?.Logger.Log("Area-load cleanup skip enabled: redundant UnloadUnusedAssets passes are skipped (save-load first pass only on >= " + SaveLoadSkipMinimumMemoryMb + " MB).");
                return true;
            }
            catch (Exception ex)
            {
                enabled = false;
                harmony?.UnpatchAll(Owner);
                harmony = null;
                entry.Logger.LogException(ex);
                return false;
            }
        }

        internal static bool Toggle(bool value)
        {
            enabled = value;
            TextureBurst.RuntimeOn = value;
            SaveUpgrade.RuntimeOn = value;
            if (!value) State.End();
            return true;
        }

        internal static bool ConfigureTexture(bool enabled, int burst)
        {
            return TextureBurst.Configure(enabled, burst);
        }

        internal static bool ConfigureSaveUpgrade(bool enabled)
        {
            return SaveUpgrade.Configure(enabled);
        }

        internal static long PumpedTextures => TextureBurst.Pumped;
        internal static long SkippedUpgradeFiles => SaveUpgrade.SkippedFiles;
        internal static long SkippedUpgradeMegabytes => SaveUpgrade.SkippedChars / (1024 * 1024);

        internal static bool Configure(bool skipFirstOnSaveLoad)
        {
            skipSaveLoadFirst = skipFirstOnSaveLoad;
            return ShouldSkipSaveLoadCleanup(skipFirstOnSaveLoad, SystemInfo.systemMemorySize);
        }

        /// <summary>Pure decision used by the prefix and by the offline checks.</summary>
        internal static bool ShouldSkipSaveLoadCleanup(bool configured, int systemMemoryMb)
        {
            return configured && systemMemoryMb >= SaveLoadSkipMinimumMemoryMb;
        }

        internal static bool Unload()
        {
            enabled = false;
            TextureBurst.Unload();
            SaveUpgrade.Unload();
            State.End();
            harmony?.UnpatchAll(Owner);
            harmony = null;
            return true;
        }

        private static void LoadAreaPrefix(object[] __args)
        {
            if (!enabled)
            {
                State.End();
                return;
            }
            // Parameter 4 is [CanBeNull] SaveInfo. A non-null value means SaveManager.LoadRoutine
            // runs between the two cleanup calls.
            SaveInfo saveInfo = __args != null && __args.Length > 4 ? __args[4] as SaveInfo : null;
            bool isNormal = saveInfo == null;
            bool skipFirst = !isNormal && ShouldSkipSaveLoadCleanup(skipSaveLoadFirst, SystemInfo.systemMemorySize);
            State.Begin(isNormal, skipFirst);
        }

        private static void LoadAreaPostfix()
        {
            State.End();
        }

        private static bool UnloadPrefix(ref IEnumerator<object> __result)
        {
            if (!enabled || !State.ShouldSkip()) return true;
            if (State.SkippedIndex == 1) SkippedSaveLoad++; else Skipped++;
            try
            {
                entry?.Logger.Log("Skipped a redundant UnloadUnusedAssets (" + (State.SkippedIndex == 1 ? "save-load first pass" : "normal transition second pass")
                    + ", total " + (Skipped + SkippedSaveLoad) + ").");
            }
            catch { }
            __result = Empty();
            return false;
        }

        private static Type FindCleanupIteratorType()
        {
            foreach (Type nested in typeof(Game).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.Name.StartsWith("<UnloadUnusedAssetsCoroutine>", StringComparison.Ordinal) &&
                    typeof(IEnumerator).IsAssignableFrom(nested))
                    return nested;
            }
            return null;
        }

        internal static bool IsCleanupProcess(Type type)
        {
            return type != null && cleanupIteratorType != null && type == cleanupIteratorType;
        }

        // The cleanup coroutine factory is inlined into LoadArea in the shipping build, so the
        // prefix on UnloadUnusedAssetsCoroutine never runs. Take the process back here instead:
        // the loading manager receives exactly one process per registered cleanup.
        private static void StartProcessPrefix(ref IEnumerator process)
        {
            if (!enabled || process == null) return;
            if (!IsCleanupProcess(process.GetType())) return;
            if (!State.ShouldSkip()) return;
            bool saveLoad = State.SkippedIndex == 1;
            if (saveLoad) SkippedSaveLoad++; else Skipped++;
            try
            {
                entry?.Logger.Log("Skipped a redundant UnloadUnusedAssets via StartLoadingProcess ("
                    + (saveLoad ? "save-load first pass" : "normal transition second pass")
                    + ", total " + (Skipped + SkippedSaveLoad) + ").");
            }
            catch { }
            process = Empty();
        }

        private static IEnumerator<object> Empty()
        {
            yield break;
        }
    }

    /// <summary>
    /// Tracks the synchronous registration window of Game.LoadArea. LoadArea does not yield; it only
    /// enqueues coroutines, so the cleanup calls are observed back-to-back and counted in order.
    /// </summary>
    internal static class State
    {
        private static bool tracking;
        private static int calls;
        private static int skipIndex;

        internal static int SkippedIndex { get; private set; }

        internal static void Begin(bool isNormal, bool skipFirstOnSave)
        {
            tracking = true;
            calls = 0;
            skipIndex = isNormal ? 2 : (skipFirstOnSave ? 1 : 0);
            SkippedIndex = 0;
        }

        internal static void Begin(bool isNormal)
        {
            Begin(isNormal, false);
        }

        internal static void End()
        {
            tracking = false;
            calls = 0;
            skipIndex = 0;
            SkippedIndex = 0;
        }



        internal static bool ShouldSkip()
        {
            if (!tracking || skipIndex == 0) return false;
            calls++;
            if (calls != skipIndex) return false;
            SkippedIndex = calls;
            return true;
        }
    }
}

