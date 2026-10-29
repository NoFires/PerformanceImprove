using System;
using System.Collections;

namespace WrathDeferredNpcViews
{
    internal static class WarmupSequence
    {
        internal static IEnumerator Append(IEnumerator original, Func<bool> active, Func<bool> warmOne, Action completed)
        {
            try
            {
                while (original.MoveNext()) yield return original.Current;
                while (active() && warmOne()) yield return null;
                completed();
            }
            finally { (original as IDisposable)?.Dispose(); }
        }
    }
}
