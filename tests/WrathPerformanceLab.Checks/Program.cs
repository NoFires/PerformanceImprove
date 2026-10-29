using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using WrathPerformanceLab;
using Mod = WrathPerformanceLab.Main;

internal class Program
{
    private class FakeLoading
    {
        public object m_CurrentLoadingProcess = new object();
        public Stopwatch m_OneTaskStopwatch = Stopwatch.StartNew();
    }
    private static int checks;
    private static readonly Exception Original = new InvalidOperationException("expected original exception");
    private static void Check(bool value, string text)
    {
        if (!value) throw new Exception("FAILED: " + text);
        checks++;
        Console.WriteLine("PASS " + text);
    }
    private static void Set(string field, object value) { typeof(Mod).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value); }
    private static string ReadSnapshot(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Succeeds(int value) { return value * 3; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fails() { throw Original; }

    private static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WrathPerformanceLab-Checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var harmony = new Harmony("WrathPerformanceLab.Checks");
        try
        {
            Check(BoundedCsv.Escape("a,\"b\"\nc") == "\"a,\"\"b\"\"\nc\"", "CSV preserves quotes, comma and newline");
            string csvPath = Path.Combine(folder, "bounded.csv");
            using (var csv = new BoundedCsv(csvPath, 160, "header"))
            {
                for (int i = 0; i < 1000; i++) csv.Row("角色", i);
                Check(csv.LimitReached, "log reaches a bounded size");
            }
            Check(new FileInfo(csvPath).Length <= 160, "UTF-8 log cap enforced in bytes");
            Set("enabled", true);
            foreach (string name in new[] { nameof(Succeeds), nameof(Fails) })
                harmony.Patch(AccessTools.Method(typeof(Program), name),
                    prefix: new HarmonyMethod(typeof(Mod), nameof(Mod.TimingPrefix)),
                    finalizer: new HarmonyMethod(typeof(Mod), nameof(Mod.TimingFinished)));
            Check(Succeeds(7) == 21, "Harmony probe preserves successful return value");
            Exception seen = null;
            try { Fails(); } catch (Exception ex) { seen = ex; }
            Check(ReferenceEquals(Original, seen), "Harmony finalizer propagates original exception instance");
            Parallel.For(0, 200, i => Succeeds(i));
            var table = (Dictionary<MethodBase, TimingAggregate>)typeof(Mod).GetField("Timings", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(table[AccessTools.Method(typeof(Program), nameof(Succeeds))].Count == 201, "concurrent probes do not lose samples");
            Check(table[AccessTools.Method(typeof(Program), nameof(Fails))].Exceptions == 1, "original exception is counted");
            Set("enabled", false);
            Succeeds(3);
            Check(table[AccessTools.Method(typeof(Program), nameof(Succeeds))].Count == 201, "disabled probes do not record calls");
            Set("enabled", true);
            Set("loadingCurrent", typeof(FakeLoading).GetField("m_CurrentLoadingProcess"));
            Set("loadingTimer", typeof(FakeLoading).GetField("m_OneTaskStopwatch"));
            string stagesPath = Path.Combine(folder, "stages.csv");
            using (var stages = new BoundedCsv(stagesPath, 4096, "elapsed", "event", "stage", "wall_ms", "area", "exception"))
            {
                Set("loadingFile", stages);
                var fake = new FakeLoading();
                var state = new Mod.LoadingCall();
                Mod.LoadingPrefix(fake, ref state);
                Mod.LoadingFinished(fake, state, null);
                stages.Flush();
                Check(ReadSnapshot(stagesPath).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length == 1, "yielding stage is not prematurely recorded as completed");
                fake.m_OneTaskStopwatch.Stop();
                fake.m_CurrentLoadingProcess = null;
                Mod.LoadingFinished(fake, state, null);
                fake = new FakeLoading();
                state = new Mod.LoadingCall();
                Mod.LoadingPrefix(fake, ref state);
                object originalStage = fake.m_CurrentLoadingProcess;
                Mod.LoadingFinished(fake, state, Original);
                Check(ReferenceEquals(fake.m_CurrentLoadingProcess, originalStage), "loading observer does not replace coroutine state");
                stages.Flush();
                string captured = ReadSnapshot(stagesPath);
                Check(captured.Contains("\"end\"") && captured.Contains("\"exception\""), "completion and exception stage events are recorded");
            }
            Set("enabled", false);
            harmony.UnpatchAll(harmony.Id);
            var remaining = Harmony.GetPatchInfo(AccessTools.Method(typeof(Program), nameof(Succeeds)));
            Check(remaining == null || !remaining.Owners.Contains(harmony.Id), "test patches can be removed");
            ExtendedChecks.Run(Check, harmony);
            Console.WriteLine("Checks passed: " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
