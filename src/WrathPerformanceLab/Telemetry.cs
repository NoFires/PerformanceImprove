using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WrathPerformanceLab
{
    // Shared by the Mod and executable checks. No dependency on Unity or game objects.
    internal sealed class TimingAggregate
    {
        public long Count;
        public long Exceptions;
        public double TotalMs;
        public double MaxMs;
        public void Add(double milliseconds, bool failed)
        {
            Count++;
            if (failed) Exceptions++;
            TotalMs += milliseconds;
            MaxMs = Math.Max(MaxMs, milliseconds);
        }
    }

    internal sealed class BoundedCsv : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly long maxBytes;
        private long bytes;
        public bool LimitReached { get; private set; }

        public BoundedCsv(string path, long maxBytes, params object[] header)
        {
            this.maxBytes = maxBytes;
            writer = new StreamWriter(path, false, new UTF8Encoding(false));
            Row(header);
        }

        internal static string Escape(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        public void Row(params object[] cells)
        {
            if (LimitReached) return;
            string line = string.Join(",", Array.ConvertAll(cells, Escape));
            long size = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
            if (bytes + size > maxBytes) { LimitReached = true; writer.Flush(); return; }
            writer.WriteLine(line);
            bytes += size;
        }

        public void Flush() { writer.Flush(); }
        public void Dispose() { writer.Dispose(); }
    }
}
