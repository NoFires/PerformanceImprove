using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PerformanceImprove.Diagnostics
{
    /// <summary>
    /// Append-only JSON-lines writer. Never throws into a patched game method: an I/O failure
    /// disables further logging and is reported once through <see cref="Failure"/>.
    /// No Unity or game types are referenced, so the format can be exercised by the checks project.
    /// </summary>
    internal sealed class LogSink : IDisposable
    {
        private readonly object gate = new object();
        private readonly StreamWriter writer;
        private readonly long maxBytes;
        private long bytes;
        private bool closed;

        internal string Path { get; }
        internal bool LimitReached { get; private set; }
        internal long Records { get; private set; }
        internal Exception Failure { get; private set; }
        internal long Bytes => bytes;

        internal LogSink(string path, long maxBytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path");
            Path = path;
            this.maxBytes = Math.Max(1024, maxBytes);
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        internal void Write(double elapsedSeconds, string kind, params object[] pairs)
        {
            if (pairs == null || pairs.Length % 2 != 0) throw new ArgumentException("field pairs", nameof(pairs));
            string line = Format(elapsedSeconds, kind, pairs);
            lock (gate)
            {
                if (closed || LimitReached) return;
                try
                {
                    long size = Encoding.UTF8.GetByteCount(line) + 1;
                    if (bytes + size > maxBytes)
                    {
                        LimitReached = true;
                        writer.Flush();
                        return;
                    }
                    writer.WriteLine(line);
                    bytes += size;
                    Records++;
                }
                catch (Exception ex)
                {
                    Failure = ex;
                    LimitReached = true;
                }
            }
        }

        internal static string Format(double elapsedSeconds, string kind, params object[] pairs)
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"t\":").Append(elapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"k\":").Append(Quote(kind));
            for (int i = 0; i < pairs.Length; i += 2)
            {
                sb.Append(',');
                sb.Append(Quote(Convert.ToString(pairs[i], CultureInfo.InvariantCulture)));
                sb.Append(':');
                AppendValue(sb, pairs[i + 1]);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case bool flag: sb.Append(flag ? "true" : "false"); return;
                case int number: sb.Append(number.ToString(CultureInfo.InvariantCulture)); return;
                case long number: sb.Append(number.ToString(CultureInfo.InvariantCulture)); return;
                case double number: sb.Append(number.ToString("0.###", CultureInfo.InvariantCulture)); return;
                case float number: sb.Append(((double)number).ToString("0.###", CultureInfo.InvariantCulture)); return;
                default: sb.Append(Quote(Convert.ToString(value, CultureInfo.InvariantCulture))); return;
            }
        }

        internal static string Quote(string text)
        {
            if (text == null) return "null";
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        internal void Flush()
        {
            lock (gate)
            {
                if (closed) return;
                try { writer.Flush(); }
                catch (Exception ex) { Failure = ex; LimitReached = true; }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                try { writer.Dispose(); }
                catch (Exception ex) { Failure = Failure ?? ex; }
            }
        }
    }
}
