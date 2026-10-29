using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace WrathPerformanceLab
{
    // Inspection deliberately reads fields, never arbitrary property getters, object.ToString(),
    // singleton creation APIs, native buffers or Unity object enumerations.
    internal static class Inspect
    {
        private static readonly ConcurrentDictionary<Type, Dictionary<string, FieldInfo>> Cache = new ConcurrentDictionary<Type, Dictionary<string, FieldInfo>>();
        private static Dictionary<string, FieldInfo> Fields(Type type)
        {
            return Cache.GetOrAdd(type, t =>
            {
                var result = new Dictionary<string, FieldInfo>();
                for (Type current = t; current != null; current = current.BaseType)
                    foreach (FieldInfo field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (!result.ContainsKey(field.Name)) result.Add(field.Name, field);
                return result;
            });
        }
        internal static object Field(object target, string name)
        {
            if (target == null) return null;
            return Fields(target.GetType()).TryGetValue(name, out FieldInfo field) ? field.GetValue(target) : null;
        }
        internal static object Auto(object target, string name) { return Field(target, "<" + name + ">k__BackingField") ?? Field(target, name); }
        internal static object Static(Type type, string name)
        {
            return type != null && Fields(type).TryGetValue(name, out FieldInfo field) && field.IsStatic ? field.GetValue(null) : null;
        }
        internal static bool Flag(object target, string name) { return Auto(target, name) is bool b && b; }
        internal static int Count(object target) { return target is ICollection c ? c.Count : -1; }
        internal static IEnumerable<FieldInfo> InstanceFields(object target)
        {
            if (target == null) yield break;
            foreach (FieldInfo field in Fields(target.GetType()).Values) if (!field.IsStatic) yield return field;
        }
        internal static object ExistingService(IDictionary registry, Type serviceType)
        {
            if (registry == null || serviceType == null || !registry.Contains(serviceType)) return null;
            return Auto(registry[serviceType], "Service");
        }
        internal static string TypeName(object target) { return target?.GetType().FullName ?? "none"; }
    }

    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
        public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
    }

    internal sealed class ScanBudget
    {
        private readonly long deadline;
        private int remaining;
        internal bool Truncated { get; private set; }
        internal ScanBudget(int maxItems = 12000, double milliseconds = 5)
        {
            remaining = maxItems;
            deadline = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000);
        }
        internal bool Take()
        {
            if (--remaining < 0 || Stopwatch.GetTimestamp() > deadline) { Truncated = true; return false; }
            return true;
        }
    }

    internal sealed class WeakLedger
    {
        private sealed class Record
        {
            internal long Id;
            internal double Since;
            internal WeakReference Target;
        }
        private sealed class Identity { internal long Id; }
        private readonly object gate = new object();
        private readonly ConditionalWeakTable<object, Identity> ids = new ConditionalWeakTable<object, Identity>();
        private readonly List<Record> records = new List<Record>();
        private readonly int capacity;
        private long total, collected, evicted;
        internal WeakLedger(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity; }
        internal long Track(object target, double now)
        {
            if (target == null) return 0;
            lock (gate)
            {
                if (ids.TryGetValue(target, out Identity known)) return known.Id;
                long id = ++total;
                ids.Add(target, new Identity { Id = id });
                if (records.Count >= capacity)
                {
                    // Preserve cumulative accounting and never claim an evicted live record was collected.
                    if (records[0].Target.IsAlive) evicted++; else collected++;
                    records.RemoveAt(0);
                }
                records.Add(new Record { Id = id, Since = now, Target = new WeakReference(target) });
                return id;
            }
        }
        internal bool Contains(object target)
        {
            if (target == null) return false;
            lock (gate) return ids.TryGetValue(target, out _);
        }
        internal Dictionary<string, object> Capture(double now)
        {
            lock (gate)
            {
                int alive = 0, stopped = 0, disposed = 0, older = 0;
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    object target = records[i].Target.Target;
                    if (target == null) { collected++; records.RemoveAt(i); continue; }
                    alive++;
                    if (Inspect.Flag(target, "Stopped")) stopped++;
                    if (Inspect.Flag(target, "IsDisposed")) disposed++;
                    if (now - records[i].Since >= 60) older++;
                }
                return new Dictionary<string, object> {
                    ["tracked_total"] = total, ["alive_tracked"] = alive, ["collected_observed"] = collected,
                    ["alive_stopped"] = stopped, ["alive_disposed"] = disposed, ["alive_over_60s"] = older,
                    ["evicted_alive"] = evicted, ["tracking_capacity"] = capacity };
            }
        }
    }

    internal static class RootInspection
    {
        internal static Dictionary<string, object> Capture(object nodes, Type controllerType, ScanBudget budget)
        {
            int nodeCount = Inspect.Count(nodes), scanned = 0, observers = 0, stopped = 0, limited = 0;
            var controllers = new HashSet<object>(ReferenceComparer.Instance);
            var targetTypes = new Dictionary<string, int>();
            if (nodes is IEnumerable list)
            {
                foreach (object observable in list)
                {
                    if (!budget.Take()) break;
                    scanned++;
                    if (!(Inspect.Field(observable, "m_Observers") is IEnumerable sequence)) continue;
                    foreach (object observer in sequence)
                    {
                        if (!budget.Take()) break;
                        observers++;
                        // Only known observer/delegate/closure edges, never an arbitrary game object graph.
                        var pending = new Queue<KeyValuePair<object, int>>();
                        var visited = new HashSet<object>(ReferenceComparer.Instance);
                        pending.Enqueue(new KeyValuePair<object, int>(observer, 0));
                        while (pending.Count > 0)
                        {
                            if (!budget.Take()) break;
                            var current = pending.Dequeue();
                            object value = current.Key;
                            if (value == null || !visited.Add(value)) continue;
                            if (controllerType != null && controllerType.IsInstanceOfType(value))
                            {
                                if (controllers.Add(value) && Inspect.Flag(value, "Stopped")) stopped++;
                                continue;
                            }
                            if (value is Delegate callback)
                            {
                                int invocationCount = 0;
                                foreach (Delegate action in callback.GetInvocationList())
                                {
                                    if (++invocationCount > 8) { limited++; break; }
                                    object target = action.Target;
                                    string name = Inspect.TypeName(target);
                                    if (targetTypes.Count < 32 || targetTypes.ContainsKey(name))
                                        targetTypes[name] = targetTypes.TryGetValue(name, out int old) ? old + 1 : 1;
                                    else limited++;
                                    if (current.Value < 4) pending.Enqueue(new KeyValuePair<object, int>(target, current.Value + 1));
                                    else if (target != null) limited++;
                                }
                            }
                            else if (current.Value < 4)
                            {
                                foreach (FieldInfo field in Inspect.InstanceFields(value))
                                {
                                    if (!budget.Take()) break;
                                    if (typeof(Delegate).IsAssignableFrom(field.FieldType) || field.Name == "observer" ||
                                        field.Name == "<>4__this" || field.Name == "m_LevelUpController" || field.Name == "levelUpController" ||
                                        (controllerType != null && controllerType.IsAssignableFrom(field.FieldType)))
                                        pending.Enqueue(new KeyValuePair<object, int>(field.GetValue(value), current.Value + 1));
                                }
                            }
                            else limited++;
                        }
                    }
                    if (budget.Truncated) break;
                }
            }
            var names = new List<string>();
            foreach (var pair in targetTypes) names.Add(pair.Key + "=" + pair.Value);
            return new Dictionary<string, object> {
                ["fabric_nodes"] = nodeCount, ["nodes_scanned"] = scanned, ["observers_scanned"] = observers,
                ["controller_targets_found"] = controllers.Count, ["stopped_controller_targets_found"] = stopped,
                ["delegate_target_types"] = string.Join(";", names), ["scope_limit_hits"] = limited,
                ["scan_truncated"] = budget.Truncated, ["traversal_max_depth"] = 4 };
        }
    }

    internal static class RetentionInspection
    {
        internal static Dictionary<string, object> Capture(IDictionary settings, IDictionary entities, WeakLedger previews, ScanBudget budget)
        {
            int scanned = 0, registered = 0, fxKeys = 0, disposed = 0, disconnected = 0, previewOwners = 0, disposedOwners = 0;
            var unique = new HashSet<object>(ReferenceComparer.Instance);
            if (settings != null)
            {
                foreach (DictionaryEntry entry in settings)
                {
                    if (!budget.Take()) break;
                    scanned++;
                    if (entities != null && entities.Contains(entry.Key) && Inspect.Auto(entities[entry.Key], "Entity") != null) registered++;
                    if (!(Inspect.Field(entry.Value, "CurrentFXs") is IDictionary fx)) continue;
                    foreach (DictionaryEntry pair in fx)
                    {
                        if (!budget.Take()) break;
                        object item = pair.Key;
                        fxKeys++;
                        unique.Add(item);
                        if (Inspect.Flag(item, "IsDisposed")) disposed++;
                        object slot = Inspect.Auto(item, "HoldingSlot"), collection = Inspect.Auto(item, "Collection");
                        if (slot == null && collection == null) disconnected++;
                        object owner = Inspect.Auto(slot, "Owner") ?? Inspect.Field(slot, "m_Owner");
                        object unit = Inspect.Field(owner, "Unit") ?? Inspect.Auto(owner, "Unit");
                        if (unit != null && previews.Contains(unit)) previewOwners++;
                        if (Inspect.Flag(unit, "IsDisposed")) disposedOwners++;
                    }
                    if (budget.Truncated) break;
                }
            }
            return new Dictionary<string, object> {
                ["settings_total"] = settings?.Count ?? -1, ["settings_scanned"] = scanned,
                ["settings_with_registered_entity"] = entities == null ? -1 : registered,
                ["fx_key_slots_scanned"] = fxKeys, ["fx_unique_keys_scanned"] = unique.Count,
                ["fx_disposed_item_keys"] = disposed, ["fx_keys_without_slot_or_collection"] = disconnected,
                ["fx_keys_with_known_preview_slot_owner"] = previewOwners, ["fx_keys_with_disposed_slot_owner"] = disposedOwners,
                ["scan_truncated"] = budget.Truncated };
        }
    }
}
