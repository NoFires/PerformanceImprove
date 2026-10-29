using System;
using System.Collections;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Visual.CharacterSystem;
using UnityModManagerNet;

namespace PerformanceImprove.LoadCleanup
{
    /// <summary>
    /// Game.AwaitTextureCompression drains the character-atlas queue one request per frame
    /// (CharacterAtlasService.Update processes exactly one request), so a load that queues N
    /// atlases waits at least N frames on top of the actual compression work. While that wait is
    /// running, pump Update a few more times per frame so the queue is bound before the next frame.
    /// The work and its order are unchanged: every call takes the same FIFO request, and the DXT
    /// queue still throttles the pump (Update returns early while DXT requests are pending).
    /// </summary>
    internal static class TextureBurst
    {
        internal const string Owner = "PerformanceImprove.LoadCleanup.TextureBurst";

        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;
        private static bool installed;

        internal static bool Enabled = true;
        internal static bool RuntimeOn = true;
        internal static int Burst = 4;
        internal static bool Active;
        internal static bool Reentering;
        internal static long Pumped;

        internal static bool Initialize(UnityModManager.ModEntry modEntry, bool enabled, int burst)
        {
            entry = modEntry;
            Enabled = enabled;
            Burst = ClampBurst(burst);
            if (installed) return true;
            try
            {
                System.Reflection.MethodInfo await = AccessTools.Method(typeof(Game), "AwaitTextureCompression", Type.EmptyTypes);
                if (await == null || await.ReturnType != typeof(IEnumerator))
                    throw new MissingMethodException("Kingmaker.Game.AwaitTextureCompression(): IEnumerator");
                System.Reflection.MethodInfo update = AccessTools.Method(typeof(CharacterAtlasService), "Update", Type.EmptyTypes);
                if (update == null)
                    throw new MissingMethodException("Kingmaker.Visual.CharacterSystem.CharacterAtlasService.Update()");

                harmony = new Harmony(Owner);
                harmony.Patch(await, prefix: new HarmonyMethod(typeof(TextureBurst), nameof(AwaitPrefix)));
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(TextureBurst), nameof(UpdatePostfix)));
                installed = true;
                return true;
            }
            catch (Exception ex)
            {
                entry?.Logger.LogException(ex);
                harmony?.UnpatchAll(Owner);
                harmony = null;
                installed = false;
                return false;
            }
        }

        internal static bool Configure(bool enabled, int burst)
        {
            Enabled = enabled;
            Burst = ClampBurst(burst);
            return true;
        }

        internal static int ClampBurst(int burst) => BurstPolicy.ClampBurst(burst);

        internal static bool ShouldPump(bool enabled, bool runtimeOn, bool active, bool reentering)
            => BurstPolicy.ShouldPump(enabled, runtimeOn, active, reentering);

        internal static void Unload()
        {
            Active = false;
            Reentering = false;
            harmony?.UnpatchAll(Owner);
            harmony = null;
            installed = false;
        }

        // Harmony: wrap the coroutine so the burst window is exactly its lifetime.
        private static bool AwaitPrefix(ref IEnumerator __result)
        {
            if (__result == null) return true;
            __result = Wrap(__result);
            return false;
        }

        private static IEnumerator Wrap(IEnumerator inner)
        {
            Active = true;
            try
            {
                while (inner.MoveNext())
                {
                    yield return inner.Current;
                }
            }
            finally
            {
                Active = false;
                Reentering = false;
            }
        }

        private static void UpdatePostfix(CharacterAtlasService __instance)
        {
            if (__instance == null) return;
            if (!ShouldPump(Enabled, RuntimeOn, Active, Reentering)) return;
            Reentering = true;
            try
            {
                for (int i = 1; i < Burst; i++)
                {
                    __instance.Update();
                    Pumped++;
                }
            }
            catch (Exception ex)
            {
                entry?.Logger.Log("Texture burst stopped: " + ex.GetType().Name + ": " + ex.Message);
                Enabled = false;
            }
            finally
            {
                Reentering = false;
            }
        }
    }
}
