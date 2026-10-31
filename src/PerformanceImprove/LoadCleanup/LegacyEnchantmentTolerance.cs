using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints.Items.Ecnchantments;
using UnityModManagerNet;

namespace PerformanceImprove.LoadCleanup
{
    /// <summary>
    /// Old saves can contain item enchantments that are removed or replaced by the current build.
    /// During load, ItemEntity.ApplyEnchantments removes such a fact before its context is active;
    /// AddFactContextActions.OnDispose then calls the explicit IFactContextOwner.RunActionInContext,
    /// which throws "You can't RunActionInContext while ItemEnchantment is not active" and aborts the
    /// whole load (2026-10-31: SaveManager.LoadRoutine -> Player.OnTurnOn -> ItemEntity.ApplyEnchantments).
    /// The enchantment is being removed anyway, so skipping its on-dispose actions when the context is
    /// null keeps the load going; normal (active) enchantments never take this branch.
    /// </summary>
    internal static class LegacyEnchantmentTolerance
    {
        internal const string Owner = "PerformanceImprove.LoadCleanup.LegacyEnchantment";

        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;
        private static bool installed;
        private static FieldInfo currentContext;

        internal static bool Enabled = true;
        internal static bool RuntimeOn = true;
        internal static long Suppressed;
        internal static Exception LastError;

        internal static bool Initialize(UnityModManager.ModEntry modEntry, bool enabled)
        {
            entry = modEntry;
            Enabled = enabled;
            if (installed) return true;
            try
            {
                MethodInfo target = FindRunActionInContext();
                if (target == null)
                    throw new MissingMethodException("ItemEnchantment.IFactContextOwner.RunActionInContext");
                currentContext = AccessTools.Field(typeof(ItemEnchantment), "m_CurrentContext");
                if (currentContext == null)
                    throw new MissingFieldException(typeof(ItemEnchantment).FullName, "m_CurrentContext");
                harmony = new Harmony(Owner);
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(LegacyEnchantmentTolerance), nameof(Prefix)));
                installed = true;
                entry?.Logger.Log("Legacy enchantment tolerance installed: inactive enchantment dispose actions are skipped instead of aborting a save load.");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                entry?.Logger.LogException(ex);
                harmony?.UnpatchAll(Owner);
                harmony = null;
                installed = false;
                return false;
            }
        }

        private static MethodInfo FindRunActionInContext()
        {
            foreach (MethodInfo method in typeof(ItemEnchantment).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name.EndsWith(".RunActionInContext", StringComparison.Ordinal))
                    return method;
            }
            return null;
        }

        internal static bool Configure(bool enabled)
        {
            Enabled = enabled;
            return true;
        }

        internal static void Unload()
        {
            Suppressed = 0;
            harmony?.UnpatchAll(Owner);
            harmony = null;
            installed = false;
        }

        private static bool Prefix(ItemEnchantment __instance)
        {
            if (!Enabled || !RuntimeOn || __instance == null) return true;
            try
            {
                if (currentContext.GetValue(__instance) != null) return true;
                Suppressed++;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
