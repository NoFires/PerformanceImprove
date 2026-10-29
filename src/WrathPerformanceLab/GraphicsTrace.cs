using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WrathPerformanceLab
{
    // Retains only weak requests and primitive event payloads. Worker hooks never perform I/O.
    internal sealed class GraphicsTrace
    {
        private sealed class Trace
        {
            internal long Id;
            internal WeakReference Request;
            internal double Since, WorkerSince;
            internal bool LowerBound = true;
            internal string Worker = "not_observed";
            internal int Callbacks;
        }
        private readonly object gate = new object();
        private readonly ConditionalWeakTable<object, Trace> identities = new ConditionalWeakTable<object, Trace>();
        private readonly Queue<Trace> records = new Queue<Trace>();
        private readonly Queue<object[]> events = new Queue<object[]>();
        private readonly int capacity, eventCapacity;
        private long total, evicted, dropped;
        private long starts, completed, failed, readbacks, errors;
        internal GraphicsTrace(int capacity = 4096, int eventCapacity = 512)
        {
            if (capacity < 1 || eventCapacity < 1) throw new ArgumentOutOfRangeException();
            this.capacity = capacity; this.eventCapacity = eventCapacity;
        }
        private Trace Get(object request, double now)
        {
            if (identities.TryGetValue(request, out Trace trace)) return trace;
            trace = new Trace { Id = ++total, Since = now, Request = new WeakReference(request) };
            identities.Add(request, trace);
            if (records.Count == capacity) { records.Dequeue(); evicted++; }
            records.Enqueue(trace);
            return trace;
        }
        internal void Observe(object request, string kind, double now, Exception error = null)
        {
            if (request == null) return;
            lock (gate)
            {
                Trace trace = Get(request, now);
                if (kind == "run") { trace.Since = now; trace.LowerBound = false; }
                if (kind == "readback") { trace.Callbacks++; readbacks++; }
                if (kind == "error") errors++;
                if (kind == "worker_start") { trace.Worker = "running"; trace.WorkerSince = now; starts++; }
                if (kind == "worker_end")
                {
                    trace.Worker = error == null ? "completed" : "failed";
                    if (error == null) completed++; else failed++;
                }
                Enqueue(Row(trace, request, kind, now, error, kind != "worker_start" && kind != "worker_end"));
            }
        }
        private void Enqueue(object[] row)
        {
            if (events.Count == eventCapacity) { events.Dequeue(); dropped++; }
            events.Enqueue(row);
        }
        internal static long DescriptorBytes(object request, out int mips)
        {
            mips = -1;
            if (!(Inspect.Field(request, "SourceBytes") is IDictionary buffers)) return -1;
            mips = buffers.Count;
            long bytes = 0;
            foreach (DictionaryEntry pair in buffers)
            {
                if (!(Inspect.Field(pair.Value, "m_Length") is int length)) return -1;
                bytes += Math.Max(0, length);
            }
            return bytes;
        }
        private static object[] Row(Trace t, object request, string kind, double now, Exception error, bool descriptors)
        {
            int mips = -1;
            long bytes = descriptors ? DescriptorBytes(request, out mips) : -1;
            return new object[] { now, kind, t.Id, now - t.Since, t.LowerBound, t.Worker,
                t.WorkerSince == 0 ? -1 : now - t.WorkerSince, t.Callbacks,
                Inspect.Flag(request, "m_IsComplete"), Inspect.Flag(request, "HasError"),
                mips, bytes, error?.GetType().FullName ?? "" };
        }
        internal List<object[]> Capture(IList queue, double now, ScanBudget budget)
        {
            var rows = new List<object[]>();
            lock (gate)
            {
                while (events.Count > 0) rows.Add(events.Dequeue());
                if (queue != null)
                    foreach (object request in queue)
                    {
                        if (!budget.Take()) break;
                        if (request != null) rows.Add(Row(Get(request, now), request, "queued_snapshot", now, null, true));
                    }
            }
            return rows;
        }
        internal Dictionary<string, object> Summary()
        {
            lock (gate)
            {
                int alive = 0, running = 0, failedAlive = 0, completedAlive = 0;
                foreach (Trace t in records)
                {
                    if (!t.Request.IsAlive) continue;
                    alive++;
                    if (t.Worker == "running") running++;
                    if (t.Worker == "failed") failedAlive++;
                    if (t.Worker == "completed") completedAlive++;
                }
                return new Dictionary<string, object> { ["requests_observed_total"] = total,
                    ["requests_alive_tracked"] = alive, ["workers_running_alive_tracked"] = running,
                    ["workers_failed_alive_tracked"] = failedAlive, ["workers_completed_alive_tracked"] = completedAlive,
                    ["worker_starts_observed_total"] = starts, ["worker_completions_observed_total"] = completed,
                    ["worker_failures_observed_total"] = failed, ["readbacks_observed_total"] = readbacks, ["request_errors_observed_total"] = errors,
                    ["records_evicted"] = evicted, ["events_dropped"] = dropped, ["tracking_capacity"] = capacity };
            }
        }
    }
}
