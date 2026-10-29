namespace PerformanceImprove.LoadCleanup
{
    /// <summary>
    /// Pure gating rules for the texture-wait burst. Kept free of Unity and game types so the
    /// offline checks can exercise them without loading Assembly-CSharp.
    /// </summary>
    internal static class BurstPolicy
    {
        internal const int DefaultBurst = 4;
        internal const int MaximumBurst = 8;

        internal static int ClampBurst(int burst)
        {
            if (burst < 1) return 1;
            if (burst > MaximumBurst) return MaximumBurst;
            return burst;
        }

        internal static bool ShouldPump(bool enabled, bool runtimeOn, bool active, bool reentering)
        {
            return enabled && runtimeOn && active && !reentering;
        }
    }
}
