using System;

namespace PerformanceImprove.Diagnostics
{
    /// <summary>Counters and timings for one <c>UnitHelper.CopySpellbook</c> invocation.</summary>
    internal sealed class CopyStats
    {
        internal int Spellbooks;
        internal int KnownSpells;
        internal int SpecialLists;
        internal int AddKnown;
        internal int LearnAddKnown;
        internal int DuplicateAddKnown;
        internal int AddSpecial;
        internal int AddBaseLevel;
        internal int SkippedAddKnown;
        internal double AddKnownMs;
        internal double AddSpecialMs;
        internal double LearnMs;
        internal long StartedAt;

        internal double ElapsedMs(long now) => (now - StartedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        internal double RedundantShare => AddKnown == 0 ? 0 : (double)DuplicateAddKnown / AddKnown;

        internal void NoteAddKnown(bool duplicate)
        {
            if (duplicate) DuplicateAddKnown++;
            AddKnown++;
        }
    }

    /// <summary>
    /// Tracks the current spellbook-copy scope on the calling thread. Game copies run on the main
    /// thread; a nested copy is ignored so one record describes one top-level copy.
    /// </summary>
    internal static class CopyScope
    {
        [ThreadStatic] private static CopyStats current;
        [ThreadStatic] private static int learnDepth;

        internal static bool Active => current != null;
        internal static bool InLearn => learnDepth > 0;
        internal static CopyStats Current => current;

        internal static CopyStats Begin(int spellbooks, int knownSpells, int specialLists, long startedAt)
        {
            if (current != null) return null;
            learnDepth = 0;
            current = new CopyStats
            {
                Spellbooks = spellbooks,
                KnownSpells = knownSpells,
                SpecialLists = specialLists,
                StartedAt = startedAt
            };
            return current;
        }

        internal static CopyStats End()
        {
            CopyStats finished = current;
            current = null;
            learnDepth = 0;
            return finished;
        }

        internal static void EnterLearn()
        {
            if (current != null) learnDepth++;
        }

        internal static void ExitLearn()
        {
            if (learnDepth > 0) learnDepth--;
        }

        // Called from Harmony prefixes/finalizers that may run outside a copy; every setter is a no-op then.
        internal static void NoteAddKnown(bool duplicate)
        {
            CopyStats stats = current;
            if (stats == null) return;
            stats.NoteAddKnown(duplicate);
            if (learnDepth > 0) stats.LearnAddKnown++;
        }

        internal static void AddKnownTime(double milliseconds)
        {
            CopyStats stats = current;
            if (stats != null) stats.AddKnownMs += milliseconds;
        }

        internal static void NoteAddSpecial()
        {
            CopyStats stats = current;
            if (stats != null) stats.AddSpecial++;
        }

        internal static void AddSpecialTime(double milliseconds)
        {
            CopyStats stats = current;
            if (stats != null) stats.AddSpecialMs += milliseconds;
        }

        internal static void NoteBaseLevel()
        {
            CopyStats stats = current;
            if (stats != null) stats.AddBaseLevel++;
        }

        internal static void AddLearnTime(double milliseconds)
        {
            CopyStats stats = current;
            if (stats != null) stats.LearnMs += milliseconds;
        }

        internal static void NoteSkipped()
        {
            CopyStats stats = current;
            if (stats != null) stats.SkippedAddKnown++;
        }

        /// <summary>Checks-only helper: clears thread state so a test starts from a known point.</summary>
        internal static void Reset()
        {
            current = null;
            learnDepth = 0;
        }
    }
}
