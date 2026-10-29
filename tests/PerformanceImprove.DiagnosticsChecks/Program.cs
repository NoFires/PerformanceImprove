using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PerformanceImprove.Diagnostics;

internal static class Program
{
    private static int checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static string[] ReadLines(string path)
    {
        // The sink keeps the file open for writing; readers must share both directions.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            var lines = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null) lines.Add(line);
            return lines.ToArray();
        }
    }

    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pi-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            string header = LogSink.Format(1.23456, "copy", "ms", 12.3456, "ok", true, "none", null, "text", "a\"b\\c\nd");
            Check(header.StartsWith("{\"t\":1.235,\"k\":\"copy\","), "record starts with elapsed time and kind");
            Check(header.Contains("\"ms\":12.346"), "timings render with invariant three-decimal format");
            Check(header.Contains("\"ok\":true"), "booleans are unquoted Json literals");
            Check(header.Contains("\"none\":null"), "null fields render as null");
            Check(header.Contains("\"text\":\"a\\\"b\\\\c\\nd\""), "strings escape quote, backslash and newline");
            Check(LogSink.Quote("\u4e2d\u6587 \u2713").Contains("\u4e2d\u6587 \u2713"), "non-ascii text is preserved verbatim");

            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                Check(LogSink.Format(0.5, "x", "v", 1.5).Contains("\"v\":1.5"), "formatting ignores comma-decimal cultures");
            }
            finally { CultureInfo.CurrentCulture = previous; }

            string path = Path.Combine(dir, "nested", "perf.jsonl");
            using (var sink = new LogSink(path, 64 * 1024))
            {
                sink.Write(0, "session", "mod", "PerformanceImprove");
                sink.Write(1, "copy", "ms", 5.0, "ok", true);
                Check(sink.Records == 2, "sink counts written records");
                Check(File.Exists(path), "sink creates missing directories");
                sink.Flush();
                string[] lines = ReadLines(path);
                Check(lines.Length == 2, "one record per line");
                Check(lines[0].StartsWith("{") && lines[1].Contains("\"k\":\"copy\""), "lines are standalone Json objects");
            }

            string capped = Path.Combine(dir, "capped.jsonl");
            using (var sink = new LogSink(capped, 2048))
            {
                for (int i = 0; i < 200; i++) sink.Write(i, "timing", "method", new string('x', 40));
                Check(sink.LimitReached && sink.Records > 0 && sink.Records < 200, "cap stops writes without throwing");
                long before = sink.Records;
                sink.Write(999, "timing", "method", "ignored");
                Check(sink.Records == before, "records after the cap are dropped");
            }

            var disposed = new LogSink(Path.Combine(dir, "disposed.jsonl"), 4096);
            disposed.Dispose();
            disposed.Dispose();
            disposed.Write(0, "late", "x", 1);
            Check(disposed.Records == 0, "disposed sink ignores writes and tolerates repeated dispose");

            CopyScope.Reset();
            Check(!CopyScope.Active, "copy scope starts inactive");
            CopyStats stats = CopyScope.Begin(2, 120, 3, System.Diagnostics.Stopwatch.GetTimestamp());
            Check(stats != null && CopyScope.Active, "begin opens a copy scope");
            CopyScope.NoteAddKnown(false);
            CopyScope.NoteAddKnown(true);
            CopyScope.NoteAddKnown(true);
            CopyScope.AddKnownTime(6.0);
            CopyScope.NoteAddSpecial();
            CopyScope.AddSpecialTime(2.5);
            CopyScope.NoteBaseLevel();
            CopyScope.NoteSkipped();
            Check(stats.AddKnown == 3 && stats.DuplicateAddKnown == 2, "add-known counters separate duplicate re-adds");
            Check(Math.Abs(stats.RedundantShare - (2.0 / 3.0)) < 1e-9, "redundant share is duplicates over total");
            Check(stats.AddSpecial == 1 && Math.Abs(stats.AddSpecialMs - 2.5) < 1e-9, "add-special counters and timing accumulate");
            Check(stats.AddBaseLevel == 1 && stats.SkippedAddKnown == 1, "base-level and skipped counters accumulate");
            Check(Math.Abs(stats.AddKnownMs - 6.0) < 1e-9, "add-known timing accumulates");

            CopyScope.EnterLearn();
            Check(CopyScope.InLearn, "learn depth opens inside AddBaseLevel");
            CopyScope.NoteAddKnown(false);
            CopyScope.AddLearnTime(4.0);
            CopyScope.ExitLearn();
            Check(!CopyScope.InLearn, "learn depth closes after AddBaseLevel");
            Check(stats.LearnAddKnown == 1, "spells added during learning are tagged");
            Check(Math.Abs(stats.LearnMs - 4.0) < 1e-9, "learn wall time is recorded separately");

            Check(CopyScope.Begin(1, 1, 1, System.Diagnostics.Stopwatch.GetTimestamp()) == null, "nested begin is ignored");
            Check(ReferenceEquals(CopyScope.End(), stats) && !CopyScope.Active, "end returns top-level stats and closes scope");
            int afterEnd = stats.AddKnown;
            CopyScope.NoteAddKnown(true);
            Check(stats.AddKnown == afterEnd, "counters outside a scope are dropped");

            var timed = new CopyStats { StartedAt = System.Diagnostics.Stopwatch.GetTimestamp() - (System.Diagnostics.Stopwatch.Frequency / 10) };
            Check(timed.ElapsedMs(System.Diagnostics.Stopwatch.GetTimestamp()) >= 95, "elapsed milliseconds derive from stopwatch ticks");

            var many = new LogSink(Path.Combine(dir, "many.jsonl"), 1024 * 1024);
            for (int i = 0; i < 500; i++) many.Write(i, "timing", "method", "m" + i, "calls", i);
            Check(many.Records == 500 && !many.LimitReached, "normal volume stays under the cap");
            many.Dispose();
            Check(ReadLines(Path.Combine(dir, "many.jsonl")).Length == 500, "every record reaches disk");

            Console.WriteLine("ALL " + checks + " CHECKS PASSED");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED after " + checks + " checks: " + ex);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
