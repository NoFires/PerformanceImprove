using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using PerformanceImprove;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private sealed class Fake
    {
        internal readonly string Name;
        internal readonly List<string> Calls;
        internal bool FailInitialize, FailDisable, FailUnload, ThrowInitialize;
        internal Fake(string name, List<string> calls) { Name = name; Calls = calls; }
        internal FeatureSlot Slot() => new FeatureSlot(
            () => { Calls.Add(Name + ":init"); if (ThrowInitialize) throw new InvalidOperationException("failure"); return !FailInitialize; },
            value => { Calls.Add(Name + ":" + value); return value || !FailDisable; },
            () => { Calls.Add(Name + ":unload"); return !FailUnload; },
            dt => Calls.Add(Name + ":tick:" + dt));
    }
    private static int Main()
    {
        try
        {
            var calls = new List<string>(); var lifecycle = new Fake("life", calls); var npc = new Fake("npc", calls);
            var group = new FeatureGroup(lifecycle.Slot(), npc.Slot());
            Check(group.Enable(true, true) && group.Lifecycle.Active && group.Npc.Active, "both features initialize under one host");
            Check(calls.SequenceEqual(new[] { "life:init", "npc:init" }), "each feature initialized once");
            calls.Clear(); Check(group.Enable(true, true) && calls.Count == 0, "repeated enable does not duplicate initialization");
            Check(group.Lifecycle.Set(false) && !group.Lifecycle.Active && group.Npc.Active, "lifecycle switch leaves NPC feature active");
            calls.Clear(); group.Tick(1); Check(calls.SequenceEqual(new[] { "npc:tick:1" }), "only active feature updated");
            group.Lifecycle.Set(true); calls.Clear(); npc.FailDisable = true;
            Check(!group.Disable() && group.Enabled && group.Lifecycle.Active && group.Npc.Active, "NPC restoration refusal keeps master and lifecycle active");
            Check(calls.SequenceEqual(new[] { "npc:False" }), "failed NPC disable does not touch lifecycle tracking");
            npc.FailDisable = false; calls.Clear();
            Check(group.Disable() && !group.Enabled && !group.Lifecycle.Active && !group.Npc.Active, "master disable turns off both features");
            Check(calls.SequenceEqual(new[] { "npc:False", "life:False" }), "NPC models restored before lifecycle cleanup disabled");
            calls.Clear(); group.Tick(1); Check(calls.Count == 0, "disabled master performs no feature updates");
            Check(group.Enable(true, false) && group.Lifecycle.Active && !group.Npc.Active, "master reenable honors individual settings");
            calls.Clear(); npc.FailUnload = true;
            Check(!group.Unload() && group.Lifecycle.Active && calls.SequenceEqual(new[] { "npc:unload" }), "NPC unload refusal preserves remaining host");
            npc.FailUnload = false; calls.Clear();
            Check(group.Unload() && calls.SequenceEqual(new[] { "npc:unload", "life:unload" }), "unload order preserves NPC ownership until restored");
            calls.Clear(); group.Unload(); Check(calls.Count == 0, "repeated unload is idempotent");
            lifecycle = new Fake("life", calls); npc = new Fake("npc", calls) { FailInitialize = true };
            group = new FeatureGroup(lifecycle.Slot(), npc.Slot());
            Check(group.Enable(true, true) && group.Lifecycle.Active && !group.Npc.Active && group.Npc.Error != null, "NPC incompatibility does not disable working lifecycle feature");
            npc.FailInitialize = false;
            Check(group.Npc.Set(true) && group.Npc.Active && group.Npc.Error == null, "failed feature initialization can be retried");
            group.Unload();
            lifecycle = new Fake("life", calls) { ThrowInitialize = true }; npc = new Fake("npc", calls) { FailInitialize = true };
            group = new FeatureGroup(lifecycle.Slot(), npc.Slot());
            Check(!group.Enable(true, true) && !group.Enabled && !group.Lifecycle.Initialized && !group.Npc.Initialized, "both rejected initializations fail the host cleanly");
            Check(group.Enable(false, false) && group.Enabled, "all features off is a valid configuration");

            var configuration = new Configuration { LifecycleCleanup = false, DeferredNpcViews = true };
            configuration.Npc.ProfileOnly = true; configuration.Npc.AllNonPartyNpcs = false; configuration.Npc.PrewarmRadius = 73;
            var serializer = new XmlSerializer(typeof(Configuration)); string xml;
            using (var writer = new StringWriter()) { serializer.Serialize(writer, configuration); xml = writer.ToString(); }
            Configuration restored; using (var reader = new StringReader(xml)) restored = (Configuration)serializer.Deserialize(reader);
            Check(!restored.LifecycleCleanup && restored.DeferredNpcViews && restored.Npc.ProfileOnly && !restored.Npc.AllNonPartyNpcs && restored.Npc.PrewarmRadius == 73,
                "single XML configuration round-trips both independent features and NPC settings");
            var assembly = typeof(PerformanceImprove.Main).Assembly;
            Check(assembly.GetName().Name == "PerformanceImprove" && typeof(WrathLifecycleFix.Main).Assembly == assembly && typeof(WrathDeferredNpcViews.Main).Assembly == assembly,
                "both feature implementations compiled into the single renamed assembly");
            Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "WrathLifecycleFix" || a.Name == "WrathDeferredNpcViews"), "no standalone feature DLL dependency remains");
            Check(PerformanceImprove.Main.LifecycleOwner != PerformanceImprove.Main.NpcOwner && PerformanceImprove.Main.LifecycleOwner.StartsWith("PerformanceImprove.") &&
                PerformanceImprove.Main.NpcOwner.StartsWith("PerformanceImprove."), "independent Harmony ownership uses the new Mod identity");
            Console.WriteLine($"{checks} checks passed"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
