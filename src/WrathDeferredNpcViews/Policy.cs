using System;
using System.Collections.Generic;

namespace WrathDeferredNpcViews
{
    // Explicit fallback allowlist for the optional narrow mode. Broad mode accepts normal NPC blueprints.
    internal static class Policy
    {
        internal const string AreaId = "2570015799edf594daf2f076f2f975d8";
        internal const string StoryReturnAreaId = "83a099db95e0e6e4485a20b10ce7c28d";
        internal static readonly HashSet<string> Blueprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "6a4b1324c7eb87a49a9c781021734e72", // CommonerFemale01
            "d5eda4da29581a54f883e880c8aeffe4", // CommonerFemale02
            "5203bf31ea57c2240834c14fc6b1e0a1", // CommonerMale01
            "929ba2ebb9c92974ca6a35c73582f671", // CommonerMale02
        };
        internal static bool Allowed(string area, string blueprint, bool allNpcs = true) =>
            string.Equals(area, AreaId, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(blueprint, "N", out var parsed) && parsed != Guid.Empty &&
            (allNpcs || Blueprints.Contains(blueprint));
        // No blanket chapter-four exclusion: the actual area variant and combat/script state are decisive.
        internal static bool KnownChapter(int chapter) => chapter >= 3 && chapter <= 6;

        internal static bool NeedsFull(bool safe, bool hasAnchors, bool hasCamera, bool cameraNear,
            double distanceSquared, double radius) =>
            !safe || !hasAnchors || !hasCamera || cameraNear || double.IsNaN(distanceSquared) || double.IsInfinity(distanceSquared) ||
            distanceSquared < 0 || distanceSquared <= radius * radius;
    }
}
