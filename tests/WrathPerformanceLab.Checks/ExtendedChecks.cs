using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using WrathPerformanceLab;

#pragma warning disable 0649
internal static class ExtendedChecks
{
    private sealed class Unit { public bool IsDisposed { get; set; } public string UniqueId { get; set; } }
    private sealed class Controller { public bool Stopped { get; set; } public void Receive() { } }
    private sealed class Subscriber { public Action onNext; }
    private sealed class Observer { public object observer; }
    private sealed class Observable { public List<object> m_Observers = new List<object>(); }
    private sealed class Owner { public Unit Unit { get; set; } }
    private sealed class Slot { public Owner Owner { get; set; } }
    private sealed class Item { public bool IsDisposed { get; set; } public Slot HoldingSlot { get; set; } public object Collection { get; set; } }
    private sealed class Setting { public Hashtable CurrentFXs = new Hashtable(); }
    private sealed class EntityProxy { public object Entity { get; set; } }
    private class ProxyBase { public object Service { get; set; } }
    private sealed class Proxy : ProxyBase
    {
        public object Instance => throw new Exception("getter must not execute");
    }
    private sealed class Dangerous { public object Instance => throw new Exception("getter must not execute"); }
    private struct Buffer { public int m_Length; }
    private sealed class Request
    {
        public Hashtable SourceBytes = new Hashtable();
        public bool m_IsComplete;
        public bool HasError { get; set; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TrackTemporary(WeakLedger ledger, GraphicsTrace graphics)
    {
        var request = new Request();
        ledger.Track(request, 0);
        graphics.Observe(request, "run", 0);
        return new WeakReference(request);
    }
    internal static void Run(Action<bool, string> check, Harmony harmony)
    {
        var ledger = new WeakLedger(2);
        var a = new Controller { Stopped = true };
        long id = ledger.Track(a, 0);
        check(ledger.Track(a, 1) == id && Convert.ToInt64(ledger.Capture(65)["tracked_total"]) == 1, "weak lifecycle identity is deduplicated");
        check((int)ledger.Capture(65)["alive_stopped"] == 1 && (int)ledger.Capture(65)["alive_over_60s"] == 1, "stopped and old controller lifetimes are separate counters");
        var b = new object(); var c = new object(); ledger.Track(b, 1); ledger.Track(c, 2);
        var life = ledger.Capture(65);
        check((long)life["evicted_alive"] == 1 && (int)life["alive_tracked"] == 2 && (long)life["collected_observed"] == 0, "bounded weak ledger never reports capacity eviction as collection");
        GC.KeepAlive(a); GC.KeepAlive(b); GC.KeepAlive(c);

        var weak = new WeakLedger(4); var weakGraphics = new GraphicsTrace();
        WeakReference temporary = TrackTemporary(weak, weakGraphics);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!temporary.IsAlive && (long)weak.Capture(1)["collected_observed"] == 1, "lifecycle and graphics observers do not root target objects");

        object service = new object();
        var registry = new Hashtable { [typeof(Unit)] = new Proxy { Service = service } };
        check(ReferenceEquals(Inspect.ExistingService(registry, typeof(Unit)), service), "service inspection reads inherited backing storage without instance getters");
        check(Inspect.Auto(new Dangerous(), "Instance") == null && Inspect.ExistingService(registry, typeof(Item)) == null,
            "missing fields and services stay unavailable without creating instances");

        var controller = new Controller { Stopped = true };
        var observable = new Observable();
        observable.m_Observers.Add(new Observer { observer = new Subscriber { onNext = controller.Receive } });
        var nodes = new List<object> { observable };
        var roots = RootInspection.Capture(nodes, typeof(Controller), new ScanBudget(1000, 1000));
        check((int)roots["stopped_controller_targets_found"] == 1 && (int)roots["fabric_nodes"] == 1,
            "bounded downstream delegate path finds stopped controller targets");
        roots = RootInspection.Capture(nodes, typeof(Controller), new ScanBudget(0, 1000));
        check((bool)roots["scan_truncated"] && (int)roots["nodes_scanned"] == 0 && observable.m_Observers.Count == 1,
            "partial root scan exposes truncation and does not unsubscribe");

        var preview = new Unit { UniqueId = "preview", IsDisposed = true }; var previews = new WeakLedger(4); previews.Track(preview, 0);
        var item = new Item { IsDisposed = true, HoldingSlot = new Slot { Owner = new Owner { Unit = preview } } };
        var disconnected = new Item(); var setting = new Setting();
        setting.CurrentFXs[item] = new object(); setting.CurrentFXs[disconnected] = new object();
        var settings = new Hashtable { ["preview"] = setting };
        var entities = new Hashtable { ["preview"] = new EntityProxy { Entity = preview } };
        var retention = RetentionInspection.Capture(settings, entities, previews, new ScanBudget(1000, 1000));
        check((int)retention["fx_keys_with_known_preview_slot_owner"] == 1 && (int)retention["fx_keys_with_disposed_slot_owner"] == 1,
            "equipment owner inspection follows auto property backing fields");
        check((int)retention["fx_disposed_item_keys"] == 1 && (int)retention["fx_keys_without_slot_or_collection"] == 1 && (int)retention["settings_with_registered_entity"] == 1,
            "VA2 separates disposed items, disconnected items and registered entities");
        var second = RetentionInspection.Capture(settings, null, previews, new ScanBudget(0, 1000));
        check((bool)second["scan_truncated"] && (int)second["settings_with_registered_entity"] == -1 && setting.CurrentFXs.Count == 2,
            "partial VA2 scans preserve dictionaries and mark missing registry");

        var request = new Request(); request.SourceBytes[0] = new Buffer { m_Length = 4096 };
        check(GraphicsTrace.DescriptorBytes(request, out int mips) == 4096 && mips == 1,
            "DXT descriptor sizes read stored lengths without accessing native memory");
        var trace = new GraphicsTrace(2, 2);
        trace.Observe(request, "run", 3);
        trace.Observe(request, "worker_start", 4);
        var error = new InvalidOperationException("expected worker failure");
        trace.Observe(request, "worker_end", 5, error);
        var rows = trace.Capture(new ArrayList { request }, 8, new ScanBudget(100, 1000));
        object[] snapshot = rows[rows.Count - 1];
        check((string)snapshot[5] == "failed" && (double)snapshot[3] == 5 && !(bool)snapshot[4] && !(bool)snapshot[8],
            "worker failure and request completion remain independent; run establishes exact observed age");
        check((long)trace.Summary()["events_dropped"] == 1 && (long)snapshot[11] == 4096,
            "event buffer is bounded and stale descriptor lengths are retained as metadata only");
        var discovered = new Request();
        rows = trace.Capture(new ArrayList { discovered }, 10, new ScanBudget(100, 1000));
        check((bool)rows[0][4], "pre-existing request age is explicitly a lower bound");
        trace.Observe(new Request(), "run", 11);
        check((long)trace.Summary()["records_evicted"] == 1, "request tracking records have a fixed capacity");

        // Use the real hooks on an emitted equivalent of the captured worker layout.
        var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("FakeDxtWorker"), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("main").DefineType("Worker", TypeAttributes.Public);
        builder.DefineField("<>4__this", typeof(object), FieldAttributes.Public);
        var errorField = builder.DefineField("Failure", typeof(Exception), FieldAttributes.Public | FieldAttributes.Static);
        var method = builder.DefineMethod("Run", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldsfld, errorField); il.Emit(OpCodes.Throw);
        Type workerType = builder.CreateType(); object worker = Activator.CreateInstance(workerType);
        workerType.GetField("<>4__this").SetValue(worker, request); workerType.GetField("Failure").SetValue(null, error);
        var actual = new GraphicsTrace();
        typeof(ExtendedMetrics).GetField("graphics", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, actual);
        ExtendedMetrics.SetActive(true);
        var run = workerType.GetMethod("Run");
        harmony.Patch(run, prefix: new HarmonyMethod(typeof(ExtendedMetrics), nameof(ExtendedMetrics.WorkerStarted)),
            finalizer: new HarmonyMethod(typeof(ExtendedMetrics), nameof(ExtendedMetrics.WorkerFinished)));
        Exception seen = null;
        try { run.Invoke(worker, null); } catch (TargetInvocationException ex) { seen = ex.InnerException; }
        check(ReferenceEquals(seen, error) && (int)actual.Summary()["workers_failed_alive_tracked"] == 1,
            "real worker Harmony finalizer records failure and preserves original exception identity");
        Parallel.For(0, 100, _ => { try { run.Invoke(worker, null); } catch (TargetInvocationException) { } });
        rows = actual.Capture(null, 20, new ScanBudget(1000, 1000));
        check(rows.Count == 202, "worker event capture preserves concurrent starts and failures without file I/O");
        ExtendedMetrics.SetActive(false);
        int before = rows.Count;
        try { run.Invoke(worker, null); } catch (TargetInvocationException) { }
        check(actual.Capture(null, 21, new ScanBudget()).Count == 0 && before == 202, "disabled extended hooks stop recording");
    }
}
