using System;

namespace PerformanceImprove.Merchant
{
    internal interface ISaleOperation<T> where T : class
    {
        bool Eligible(T item);
        bool Journal(T item);
        T Original(T item);
        T Remove(T item);
        bool CanDispose(T original, T removed);
        void Dispose(T item);
        T Recover(T item);
        void Completed(T item);
        void Failed(Exception error);
    }

    internal static class SaleDiscard
    {
        internal static T Run<T>(T item, ISaleOperation<T> operation) where T : class
        {
            bool discard;
            try { discard = operation.Eligible(item) && operation.Journal(item); }
            catch (Exception ex) { operation.Failed(ex); discard = false; }
            // Keep the native transfer outside a catch/retry: an original exception must never repeat a sale.
            if (!discard) return operation.Original(item);
            T removed;
            try { removed = operation.Remove(item); }
            catch (Exception ex) { operation.Failed(ex); return operation.Recover(item); }
            try
            {
                if (operation.CanDispose(item, removed))
                {
                    operation.Dispose(removed);
                    operation.Completed(removed);
                    return removed;
                }
            }
            catch (Exception ex) { operation.Failed(ex); }
            return operation.Recover(removed ?? item);
        }
    }
}
