using System;
using System.Collections;
using System.Reflection;
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
    ///
    /// The window follows LoadingProcess.TickLoading, which the loading manager calls every frame
    /// and which reads the current stage from m_CurrentLoadingProcess. Two earlier mechanisms were
    /// rejected by the 2026-10-30 in-game run:
    ///  - a postfix on Game.AwaitTextureCompression() never changed the returned enumerator (the
    ///    stage stayed the original &lt;AwaitTextureCompression&gt;d__267 and the pumped counter stayed 0);
    ///  - patching the iterator's MoveNext makes the CLR reject the rewritten method with
    ///    InvalidProgramException (reproduced offline with Harmony alone).
    /// Identity of the stage is therefore read from the loading manager itself; no wrapper is
    /// inserted, so stage names and diagnostics stay unchanged.
    /// </summary>
    internal static class TextureBurst
    {
        internal const string Owner = "PerformanceImprove.LoadCleanup.TextureBurst";

        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;
        private static bool installed;
        private static Type iteratorType;
        private static FieldInfo loadingCurrent;
        private static bool windowOpen;
        private static long windowStart;

        internal static bool Enabled = true;
        internal static bool RuntimeOn = true;
        internal static int Burst = 4;
        internal static bool Active;
        internal static bool Reentering;
        internal static long Pumped;
        internal static long Windows;
        internal static Exception LastError;

        internal static Type IteratorType => iteratorType;
        internal static bool HasWindowPatch => tickInfo != null && HasOwnerPatch(tickInfo);
        private static HarmonyLib.Patches tickInfo;

        internal static bool Initialize(UnityModManager.ModEntry modEntry, bool enabled, int burst)
        {
            entry = modEntry;
            Enabled = enabled;
            Burst = ClampBurst(burst);
            if (installed) return true;
            try
            {
                MethodInfo factory = AccessTools.Method(typeof(Game), "AwaitTextureCompression", Type.EmptyTypes);
                if (factory == null || factory.ReturnType != typeof(IEnumerator))
                    throw new MissingMethodException("Kingmaker.Game.AwaitTextureCompression(): IEnumerator");

                iteratorType = FindIteratorType();
                if (iteratorType == null)
                    throw new MissingMethodException("Kingmaker.Game+<AwaitTextureCompression>d__*");

                Type loadingType = AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.LoadingProcess");
                if (loadingType == null)
                    throw new TypeLoadException("Kingmaker.EntitySystem.Persistence.LoadingProcess");
                loadingCurrent = AccessTools.Field(loadingType, "m_CurrentLoadingProcess");
                if (loadingCurrent == null)
                    throw new MissingFieldException(loadingType.FullName, "m_CurrentLoadingProcess");
                MethodInfo tick = AccessTools.Method(loadingType, "TickLoading", Type.EmptyTypes);
                if (tick == null)
                    throw new MissingMethodException(loadingType.FullName + ".TickLoading()");

                MethodInfo update = AccessTools.Method(typeof(CharacterAtlasService), "Update", Type.EmptyTypes);
                if (update == null)
                    throw new MissingMethodException("Kingmaker.Visual.CharacterSystem.CharacterAtlasService.Update()");

                harmony = new Harmony(Owner);
                harmony.Patch(tick,
                    prefix: new HarmonyMethod(typeof(TextureBurst), nameof(TickPrefix)),
                    postfix: new HarmonyMethod(typeof(TextureBurst), nameof(TickPostfix)),
                    finalizer: new HarmonyMethod(typeof(TextureBurst), nameof(TickFinalizer)));
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(TextureBurst), nameof(UpdatePostfix)));
                tickInfo = Harmony.GetPatchInfo(tick);
                installed = true;
                entry?.Logger.Log("Texture burst installed: wait window follows LoadingProcess.TickLoading for " + iteratorType.Name + ".");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                entry?.Logger.LogException(ex);
                harmony?.UnpatchAll(Owner);
                harmony = null;
                tickInfo = null;
                installed = false;
                return false;
            }
        }

        private static bool HasOwnerPatch(HarmonyLib.Patches info)
        {
            if (info == null) return false;
            foreach (Patch patch in info.Prefixes)
                if (patch.owner == Owner) return true;
            foreach (Patch patch in info.Postfixes)
                if (patch.owner == Owner) return true;
            return false;
        }

        private static Type FindIteratorType()
        {
            foreach (Type nested in typeof(Game).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.Name.StartsWith("<AwaitTextureCompression>", StringComparison.Ordinal) &&
                    typeof(IEnumerator).IsAssignableFrom(nested))
                    return nested;
            }
            return null;
        }

        internal static bool IsWindowProcess(Type processType)
            => processType != null && iteratorType != null && processType == iteratorType;

        internal static bool IsWindowInstance(object process)
            => process != null && iteratorType != null && iteratorType.IsInstanceOfType(process);

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
            windowOpen = false;
            tickInfo = null;
            harmony?.UnpatchAll(Owner);
            harmony = null;
            installed = false;
        }

        // Harmony on LoadingProcess.TickLoading: the window is exactly the time the current stage
        // is the AwaitTextureCompression iterator.
        private static void TickPrefix(object __instance)
        {
            bool open = IsOpen(__instance);
            if (open && !windowOpen)
            {
                windowOpen = true;
                windowStart = Pumped;
            }
            Active = open;
        }

        private static void TickPostfix(object __instance)
        {
            bool open = IsOpen(__instance);
            if (!open)
            {
                Active = false;
                if (windowOpen)
                {
                    windowOpen = false;
                    Windows++;
                    long pumped = Pumped - windowStart;
                    if (pumped > 0)
                        entry?.Logger.Log("Texture burst: pumped " + pumped + " extra Update calls during the wait window.");
                }
            }
            else
            {
                Active = true;
            }
        }

        private static void TickFinalizer(Exception __exception)
        {
            if (__exception != null) Active = false;
        }

        private static bool IsOpen(object instance)
        {
            if (instance == null || loadingCurrent == null || iteratorType == null) return false;
            object current = loadingCurrent.GetValue(instance);
            return IsWindowInstance(current);
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
