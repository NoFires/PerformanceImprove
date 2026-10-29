using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;
using WrathPerformanceLab;

namespace WrathLifecycleFix
{
    public static class Main
    {
        private const string Id = "WrathLifecycleFix";
        private static string patchOwner = Id;
        private static Harmony harmony;
        private static UnityModManager.ModEntry entry;
        private static FieldInfo stopped;
        private static SubscriptionLifetime lifetime;
        private static BoundedCsv statistics;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static bool enabled, lastRewriteApplied, ioFailed;
        private static double next;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            if (!Initialize(modEntry, Id)) return false;
            entry.OnToggle = Toggle;
            entry.OnUnload = Unload;
            entry.OnUpdate = Update;
            return true;
        }

        internal static bool Initialize(UnityModManager.ModEntry modEntry, string owner, string logPrefix = "")
        {
            entry = modEntry;
            patchOwner = owner;
            try
            {
                string folder = Path.Combine(entry.Path, "Logs");
                Directory.CreateDirectory(folder);
                statistics = new BoundedCsv(Path.Combine(folder, logPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".csv"),
                    2 * 1024 * 1024, "elapsed_s", "event", "captured_total", "disposed_total", "dispose_failures", "outstanding_tracked", "tracking_dropped_on_disable", "getter_capture_installed");
                harmony = new Harmony(patchOwner);
                Type type = AccessTools.TypeByName("Kingmaker.UnitLogic.Class.LevelUp.LevelUpController");
                if (type == null) throw new InvalidOperationException("LevelUpController not found");
                if (!GameCompatibility.Matches(type.Assembly.Location))
                    throw new InvalidOperationException("Unsupported Assembly-CSharp hash; this experimental build targets the analysed 2.7.0x installation only");
                stopped = AccessTools.Field(type, "<Stopped>k__BackingField");
                if (stopped == null || stopped.FieldType != typeof(bool)) throw new InvalidOperationException("Stopped backing field mismatch");
                lifetime = new SubscriptionLifetime(owner => (bool)stopped.GetValue(owner), message => entry.Logger.Log(message));
                Install(type);
                entry.Logger.Log("Experimental lifecycle fix enabled. Captures the original getter subscription and disposes it after Stopped=true; no GC or global cleanup.");
                Write("enabled");
                return true;
            }
            catch (Exception ex)
            {
                enabled = false;
                harmony?.UnpatchAll(patchOwner);
                statistics?.Dispose();
                entry.Logger.LogException(ex);
                return false;
            }
        }

        private static void Install(Type type)
        {
            MethodInfo getter = AccessTools.PropertyGetter(type, "UpdateCommand");
            MethodInfo stop = AccessTools.DeclaredMethod(type, "Stop", Type.EmptyTypes);
            if (getter == null || stop == null || getter.IsStatic || getter.GetParameters().Length != 0 ||
                getter.ReturnType.FullName != "UniRx.ReactiveCommand" || getter.GetMethodBody().ExceptionHandlingClauses.Count != 0)
                throw new InvalidOperationException("Unsupported lifecycle method layout; no fix installed");
            enabled = true;
            lastRewriteApplied = false;
            harmony.Patch(getter, transpiler: new HarmonyMethod(typeof(Main), nameof(CaptureTranspiler)) { priority = Priority.Last });
            if (!lastRewriteApplied) throw new InvalidOperationException("Expected one discarded Subscribe call; getter unchanged");
            harmony.Patch(stop, prefix: new HarmonyMethod(typeof(Main), nameof(BeforeStop)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(Main), nameof(AfterStop)) { priority = Priority.Last });
        }

        public static IEnumerable<CodeInstruction> CaptureTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = CaptureRewriter.Rewrite(instructions, AccessTools.Method(typeof(Main), nameof(CaptureSubscription)), out bool applied);
            lastRewriteApplied = applied;
            if (!applied)
                try { entry?.Logger.Log("Getter layout no longer matches; new subscriptions are not captured. Original getter behavior preserved."); } catch { }
            return code;
        }

        public static IDisposable CaptureSubscription(IDisposable subscription, object owner)
        {
            if (!enabled) return subscription;
            try { return lifetime.Capture(subscription, owner); }
            catch (Exception ex) { try { entry?.Logger.Log("Capture failed: " + ex.GetType().Name); } catch { } return subscription; }
        }

        public static void BeforeStop(object __instance, ref bool __state)
        {
            if (!enabled) return;
            try { lifetime.BeginStop(__instance); __state = true; } catch { }
        }

        // Void finalizer: the game's return and exception are preserved, including failed Stop.
        public static void AfterStop(object __instance, bool __state)
        {
            if (!enabled || !__state) return;
            try { lifetime.EndStop(__instance); } catch { }
        }

        private static void Write(string value)
        {
            if (statistics == null || ioFailed) return;
            try
            {
                long[] row = lifetime.Snapshot();
                statistics.Row(clock.Elapsed.TotalSeconds, value, row[0], row[1], row[2], row[3], row[4], lastRewriteApplied);
                statistics.Flush();
            }
            catch (Exception ex) { ioFailed = true; entry?.Logger.Log("Fix statistics stopped: " + ex.GetType().Name); }
        }
        internal static void Update(UnityModManager.ModEntry modEntry, float delta)
        {
            if (!enabled || clock.Elapsed.TotalSeconds < next) return;
            next = clock.Elapsed.TotalSeconds + 15;
            Write("periodic");
        }
        internal static bool Toggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (value == enabled) return true;
            if (value)
            {
                try { Install(stopped.DeclaringType); next = 0; Write("enabled"); }
                catch (Exception ex) { enabled = false; harmony.UnpatchAll(patchOwner); entry.Logger.LogException(ex); return false; }
            }
            else
            {
                enabled = false;
                harmony.UnpatchAll(patchOwner);
                lastRewriteApplied = false;
                lifetime.DropTracking();
                Write("disabled_active_subscriptions_preserved");
            }
            return true;
        }
        internal static bool Unload(UnityModManager.ModEntry modEntry)
        {
            Toggle(modEntry, false);
            try { statistics?.Dispose(); } catch { }
            return true;
        }
    }
}
