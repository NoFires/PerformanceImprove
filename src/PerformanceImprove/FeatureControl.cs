using System;

namespace PerformanceImprove
{
    internal sealed class FeatureSlot
    {
        private readonly Func<bool> initialize, unload;
        private readonly Func<bool, bool> toggle;
        private readonly Action<float> update;
        internal bool Initialized { get; private set; }
        internal bool Active { get; private set; }
        internal string Error { get; private set; }
        internal FeatureSlot(Func<bool> initialize, Func<bool, bool> toggle, Func<bool> unload, Action<float> update)
        { this.initialize = initialize; this.toggle = toggle; this.unload = unload; this.update = update; }
        internal bool Set(bool value)
        {
            if (value == Active) return true;
            try
            {
                bool ok = value && !Initialized ? initialize() : toggle(value);
                if (!ok) { Error = value ? "Initialization/activation rejected; see UMM log." : "Disable rejected; pending work retained."; return false; }
                if (value) Initialized = true;
                Active = value; Error = null; return true;
            }
            catch (Exception ex) { Error = ex.ToString(); return false; }
        }
        internal void Tick(float delta) { if (Active) update(delta); }
        internal bool Unload()
        {
            if (!Initialized) return true;
            try
            {
                if (!unload()) { Error = "Unload rejected; pending work retained."; return false; }
                Active = Initialized = false; Error = null; return true;
            }
            catch (Exception ex) { Error = ex.ToString(); return false; }
        }
    }

    internal sealed class FeatureGroup
    {
        internal readonly FeatureSlot Lifecycle, Npc, Merchant, Diagnostics, LoadCleanup;
        internal bool Enabled { get; private set; }
        internal FeatureGroup(FeatureSlot lifecycle, FeatureSlot npc, FeatureSlot merchant = null, FeatureSlot diagnostics = null, FeatureSlot loadCleanup = null)
        { Lifecycle = lifecycle; Npc = npc; Merchant = merchant; Diagnostics = diagnostics; LoadCleanup = loadCleanup; }
        internal bool Enable(bool lifecycle, bool npc, bool merchant = false, bool diagnostics = false, bool loadCleanup = false)
        {
            // Telemetry first so initialization of the other features is captured in the log.
            bool d = !diagnostics || (Diagnostics?.Set(true) ?? false);
            // The load-cleanup patch has no dependency on the other features; install it before any area load starts.
            bool e = !loadCleanup || (LoadCleanup?.Set(true) ?? false);
            bool a = !lifecycle || Lifecycle.Set(true);
            bool b = !npc || Npc.Set(true);
            bool c = !merchant || (Merchant?.Set(true) ?? false);
            // One feature may remain usable if another's runtime layout is incompatible.
            Enabled = (!lifecycle && !npc && !merchant && !loadCleanup) || Lifecycle.Active || Npc.Active || Merchant?.Active == true || LoadCleanup?.Active == true;
            return d && Enabled && (a || b || c || e);
        }
        internal bool Disable()
        {
            // NPC models need restoration before any ownership metadata or lifecycle hooks are dropped.
            if (!Npc.Set(false) || !(Merchant?.Set(false) ?? true) || !(LoadCleanup?.Set(false) ?? true) || !Lifecycle.Set(false)) return false;
            // Stop observation last so the teardown above is still recorded.
            Diagnostics?.Set(false);
            Enabled = false; return true;
        }
        internal bool Unload()
        {
            if (!Npc.Unload() || !(Merchant?.Unload() ?? true) || !(LoadCleanup?.Unload() ?? true) || !Lifecycle.Unload()) return false;
            Diagnostics?.Unload();
            Enabled = false; return true;
        }
        internal void Tick(float delta) { if (Enabled) { Lifecycle.Tick(delta); Npc.Tick(delta); Merchant?.Tick(delta); LoadCleanup?.Tick(delta); Diagnostics?.Tick(delta); } }
    }
}
