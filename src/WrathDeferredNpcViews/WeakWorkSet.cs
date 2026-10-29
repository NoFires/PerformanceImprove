using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WrathDeferredNpcViews
{
    internal sealed class WeakWorkSet<T> where T : class
    {
        private sealed class State { internal bool Done; }
        private readonly ConditionalWeakTable<T, State> states = new ConditionalWeakTable<T, State>();
        private readonly List<WeakReference<T>> pending = new List<WeakReference<T>>();
        private readonly int capacity;
        internal WeakWorkSet(int capacity) { this.capacity = capacity; }
        internal bool Add(T item)
        {
            if (states.TryGetValue(item, out _)) return false;
            if (!HasCapacity) return false;
            states.Add(item, new State());
            pending.Add(new WeakReference<T>(item));
            return true;
        }
        internal bool Contains(T item) => item != null && states.TryGetValue(item, out var s) && !s.Done;
        internal bool HasCapacity
        {
            get { if (pending.Count >= capacity) Prune(); return pending.Count < capacity; }
        }
        internal bool Seen(T item) => item != null && states.TryGetValue(item, out _);
        internal void Complete(T item) { if (states.TryGetValue(item, out var s)) s.Done = true; }
        internal List<T> Snapshot()
        {
            Prune();
            var result = new List<T>();
            foreach (var reference in pending) if (reference.TryGetTarget(out var target)) result.Add(target);
            return result;
        }
        private void Prune()
        {
            for (int i = pending.Count - 1; i >= 0; --i)
                if (!pending[i].TryGetTarget(out var target) || !Contains(target)) pending.RemoveAt(i);
        }
    }
}
