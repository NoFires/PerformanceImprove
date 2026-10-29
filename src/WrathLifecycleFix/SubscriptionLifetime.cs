using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WrathLifecycleFix
{
    // Values can point back to their key through the real subscription. Ephemeron ownership
    // is essential here; a Dictionary<object, IDisposable> would itself root the controller.
    internal sealed class SubscriptionLifetime
    {
        private sealed class State
        {
            internal int StopDepth;
            internal readonly List<IDisposable> Tokens = new List<IDisposable>();
        }
        private readonly object gate = new object();
        private ConditionalWeakTable<object, State> states = new ConditionalWeakTable<object, State>();
        private readonly Func<object, bool> isStopped;
        private readonly Action<string> report;
        private long captured, disposed, failures, outstanding, dropped;

        internal SubscriptionLifetime(Func<object, bool> isStopped, Action<string> report = null)
        {
            this.isStopped = isStopped ?? throw new ArgumentNullException(nameof(isStopped));
            this.report = report;
        }

        internal IDisposable Capture(IDisposable token, object owner)
        {
            if (token == null || owner == null) return token;
            bool release = false;
            lock (gate)
            {
                bool stopping = states.TryGetValue(owner, out State state) && state.StopDepth > 0;
                if (isStopped(owner) && !stopping)
                {
                    // A first getter access after Stop must not introduce a new permanent root.
                    captured++; release = true;
                }
                else
                {
                    state = state ?? states.GetValue(owner, _ => new State());
                    foreach (IDisposable known in state.Tokens) if (ReferenceEquals(known, token)) return token;
                    state.Tokens.Add(token); captured++; outstanding++;
                }
            }
            if (release) Release(token);
            return token; // Preserve the exact original Subscribe result and original pop.
        }

        internal void BeginStop(object owner)
        {
            if (owner == null) return;
            lock (gate) states.GetValue(owner, _ => new State()).StopDepth++;
        }

        internal void EndStop(object owner)
        {
            if (owner == null) return;
            List<IDisposable> release = null;
            lock (gate)
            {
                if (!states.TryGetValue(owner, out State state)) return;
                if (state.StopDepth > 0) state.StopDepth--;
                if (state.StopDepth > 0) return; // Preserve callbacks throughout a reentrant Stop.
                if (!isStopped(owner))
                {
                    // Stop failed before reaching its state transition: keep the active subscription.
                    if (state.Tokens.Count == 0) states.Remove(owner);
                    return;
                }
                states.Remove(owner);
                release = state.Tokens;
                outstanding -= release.Count;
            }
            // Dispose outside our lock; UniRx's cancellation removes only its own observer/node.
            foreach (IDisposable token in release) Release(token);
        }

        private void Release(IDisposable token)
        {
            try { token.Dispose(); lock (gate) disposed++; }
            catch (Exception ex)
            {
                lock (gate) failures++;
                try { report?.Invoke("Subscription disposal failed: " + ex.GetType().Name); } catch { }
            }
        }

        internal void DropTracking()
        {
            lock (gate)
            {
                // Disabling must not disconnect a still active character UI. Existing tokens
                // continue their original behavior; only ownership metadata is released.
                states = new ConditionalWeakTable<object, State>();
                dropped += outstanding; outstanding = 0;
            }
        }

        internal long[] Snapshot()
        {
            lock (gate) return new[] { captured, disposed, failures, outstanding, dropped };
        }
    }
}
