using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Kingmaker.EntitySystem.Persistence;
using UnityModManagerNet;

namespace PerformanceImprove.LoadCleanup
{
    /// <summary>
    /// 2026-10-30 in-game measurement: SaveManager.LoadRoutine took 86-147 s for a 32 MB save
    /// (152 MB of JSON over 497 zip entries). The cost was not the player/party deserializer:
    /// TabletopTweaks-Core registers JSON upgrader -117 ("Migrate TabletopTweaks to
    /// TabletopTweaks-Core"). Its WillUpgrade() returns true for every file and Upgrade() parses
    /// the whole JSON into a JObject, walks every token with SelectTokens("..$type"), rewrites it
    /// and calls GC.Collect() once per file. A save created while that mod was disabled does not
    /// record version -117, so every load re-runs the migration over all files.
    ///
    /// The upgrader only rewrites $type strings matching "TabletopTweaks". When the JSON text does
    /// not contain that substring at all, the transformation is provably a no-op, so the parse,
    /// rewrite and forced GC can be skipped without changing any game data. If any other upgrader
    /// applies, or the file does contain the marker, the original method runs unchanged.
    ///
    /// Not affected by / not conflicting with Performance Fixes (ManyModsPerformanceFix: particle
    /// and cheat patches), WrathPatches (no save/load patches) or ModMenu (only skips the header
    /// file in ShouldUpgrade and reads the recorded mod list).
    /// </summary>
    internal static class SaveUpgrade
    {
        internal const string Owner = "PerformanceImprove.LoadCleanup.SaveUpgrade";
        internal const string TabletopUpgraderTypeName = "TabletopTweaks.Core.SaveUpgrades.TabletopTweaksMigration";
        internal const string Marker = "TabletopTweaks";

        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;
        private static bool installed;
        private static MethodInfo upgradeMethod;
        private static MethodInfo getUpgraders;
        private static FieldInfo upgraderField;
        private static long lastLogTicks;

        internal static bool Enabled = true;
        internal static bool RuntimeOn = true;
        internal static long SkippedFiles;
        internal static long SkippedChars;
        internal static long Fallbacks;
        internal static Exception LastError;

        internal static bool HasPatch
        {
            get
            {
                if (upgradeMethod == null) return false;
                HarmonyLib.Patches info = Harmony.GetPatchInfo(upgradeMethod);
                if (info == null) return false;
                foreach (Patch patch in info.Prefixes)
                    if (patch.owner == Owner) return true;
                return false;
            }
        }

        internal static bool Initialize(UnityModManager.ModEntry modEntry, bool enabled)
        {
            entry = modEntry;
            Enabled = enabled;
            if (installed) return true;
            try
            {
                Type system = AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.Versioning.JsonUpgradeSystem");
                if (system == null)
                    throw new TypeLoadException("Kingmaker.EntitySystem.Persistence.Versioning.JsonUpgradeSystem");
                upgradeMethod = AccessTools.Method(system, "Upgrade", new[] { typeof(SaveInfo), typeof(string), typeof(string) });
                if (upgradeMethod == null || upgradeMethod.ReturnType != typeof(string) || !upgradeMethod.IsStatic)
                    throw new MissingMethodException("JsonUpgradeSystem.Upgrade(SaveInfo, string, string): string");
                getUpgraders = AccessTools.Method(system, "GetUpgraders", new[] { typeof(SaveInfo), typeof(string) });
                if (getUpgraders == null)
                    throw new MissingMethodException("JsonUpgradeSystem.GetUpgraders(SaveInfo, string)");

                harmony = new Harmony(Owner);
                harmony.Patch(upgradeMethod, prefix: new HarmonyMethod(typeof(SaveUpgrade), nameof(UpgradePrefix)));
                installed = true;
                entry?.Logger.Log("Save-upgrade fast path installed: the TabletopTweaks -117 migration is skipped for files that contain no TabletopTweaks type names."
                    + " TabletopTweaks migration present: " + (AccessTools.TypeByName(TabletopUpgraderTypeName) != null)
                    + "; patch owners on Upgrade: " + string.Join(", ", PatchOwners(upgradeMethod)) + ".");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                entry?.Logger.LogException(ex);
                harmony?.UnpatchAll(Owner);
                harmony = null;
                upgradeMethod = null;
                installed = false;
                return false;
            }
        }

        internal static bool Configure(bool enabled)
        {
            Enabled = enabled;
            return true;
        }

        internal static void Unload()
        {
            SkippedFiles = 0;
            SkippedChars = 0;
            Fallbacks = 0;
            lastLogTicks = 0;
            harmony?.UnpatchAll(Owner);
            harmony = null;
            upgradeMethod = null;
            installed = false;
        }

        /// <summary>Pure skip decision, also used by the offline checks.</summary>
        internal static bool ShouldSkip(string json, bool onlyTabletopUpgraders)
        {
            return onlyTabletopUpgraders && json != null && json.IndexOf(Marker, StringComparison.Ordinal) < 0;
        }

        internal static bool IsTabletopUpgraderName(string fullName)
        {
            return fullName == TabletopUpgraderTypeName;
        }

        private static string PatchOwners(MethodInfo method)
        {
            var owners = new System.Collections.Generic.SortedSet<string>(StringComparer.Ordinal);
            HarmonyLib.Patches info = Harmony.GetPatchInfo(method);
            if (info != null)
            {
                foreach (Patch patch in info.Prefixes) owners.Add(patch.owner);
                foreach (Patch patch in info.Postfixes) owners.Add(patch.owner);
                foreach (Patch patch in info.Transpilers) owners.Add(patch.owner);
                foreach (Patch patch in info.Finalizers) owners.Add(patch.owner);
            }
            return string.Join(",", owners);
        }

        private static bool UpgradePrefix(SaveInfo saveInfo, string fileName, string json, ref string __result)
        {
            if (!Enabled || !RuntimeOn || json == null) return true;
            // Cheap check first: most files never mention TabletopTweaks at all.
            if (json.IndexOf(Marker, StringComparison.Ordinal) >= 0) return true;
            if (!OnlyTabletopUpgraders(saveInfo, fileName)) return true;
            __result = json;
            long files = Interlocked.Increment(ref SkippedFiles);
            Interlocked.Add(ref SkippedChars, json.Length);
            if (files == 1 || files % 150 == 0) LogProgress(files);
            return false;
        }

        private static bool OnlyTabletopUpgraders(SaveInfo saveInfo, string fileName)
        {
            object list;
            try
            {
                list = getUpgraders.Invoke(null, new object[] { saveInfo, fileName });
            }
            catch
            {
                // Unknown version or reflection failure: let the game method do its own work/throw.
                Interlocked.Increment(ref Fallbacks);
                return false;
            }
            IEnumerable entries = list as IEnumerable;
            if (entries == null) return false;
            int count = 0;
            foreach (object item in entries)
            {
                if (item == null) return false;
                if (upgraderField == null)
                {
                    upgraderField = item.GetType().GetField("Upgrader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (upgraderField == null) return false;
                }
                object upgrader = upgraderField.GetValue(item);
                if (upgrader == null || !IsTabletopUpgraderName(upgrader.GetType().FullName)) return false;
                count++;
            }
            return count > 0;
        }

        private static void LogProgress(long files)
        {
            long now = DateTime.UtcNow.Ticks;
            long previous = Interlocked.Exchange(ref lastLogTicks, now);
            if (previous != 0 && now - previous < TimeSpan.TicksPerSecond) return;
            try
            {
                long chars = Interlocked.Read(ref SkippedChars);
                entry?.Logger.Log("Save-upgrade fast path: skipped the no-op TabletopTweaks migration for " + files
                    + " file(s), " + (chars / (1024 * 1024)) + " MB of JSON so far in this load.");
            }
            catch
            {
            }
        }
    }
}
