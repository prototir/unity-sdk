using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Prototir.Native
{
    public enum PrototirLogLevel { Log, Warning, Error }

    public readonly struct PrototirLogEntry
    {
        public PrototirLogEntry(DateTime time, PrototirLogLevel level, string text)
        {
            Time = time; Level = level; Text = text;
        }
        public DateTime Time { get; }
        public PrototirLogLevel Level { get; }
        public string Text { get; }
    }

    /// <summary>The build's console, recorded from start so the error a tester saw before opening
    /// the Console panel is already there (§16.7).
    ///
    /// <para>Engine-free and thread-safe: Unity reports logs from any thread. Each entry is one short
    /// string in a fixed ring of <see cref="Capacity"/>; stack traces are kept for errors only, and
    /// only their first lines. Recording costs a lock and a copy, never formatting work.</para></summary>
    public sealed class PrototirConsoleBuffer
    {
        public const int Capacity = 300;
        private const int MaxEntry = 2000;
        private readonly PrototirLogEntry[] _ring = new PrototirLogEntry[Capacity];
        private readonly object _gate = new object();
        private int _next, _count, _version;

        /// <summary>Changes whenever an entry is added or the buffer cleared, so a panel can redraw only
        /// when something happened.</summary>
        public int Version { get { lock (_gate) return _version; } }

        public void Add(PrototirLogLevel level, string message, string stackTrace = null, DateTime? time = null)
        {
            var text = message ?? string.Empty;
            if (level == PrototirLogLevel.Error && !string.IsNullOrWhiteSpace(stackTrace))
            {
                var lines = stackTrace.Split('\n');
                var kept = new StringBuilder(text);
                for (var i = 0; i < lines.Length && i < 5; i++)
                    if (!string.IsNullOrWhiteSpace(lines[i])) kept.Append("\n  ").Append(lines[i].Trim());
                text = kept.ToString();
            }
            if (text.Length > MaxEntry) text = text.Substring(0, MaxEntry - 1) + "…";
            lock (_gate)
            {
                _ring[_next] = new PrototirLogEntry(time ?? DateTime.UtcNow, level, text);
                _next = (_next + 1) % Capacity;
                _count = Math.Min(_count + 1, Capacity);
                _version++;
            }
        }

        /// <summary>The recorded entries, oldest first.</summary>
        public List<PrototirLogEntry> Entries()
        {
            lock (_gate)
            {
                var entries = new List<PrototirLogEntry>(_count);
                for (var i = 0; i < _count; i++) entries.Add(_ring[(_next - _count + i + Capacity) % Capacity]);
                return entries;
            }
        }

        public void Clear()
        {
            lock (_gate) { Array.Clear(_ring, 0, _ring.Length); _next = 0; _count = 0; _version++; }
        }

        /// <summary>The log as plain text, one line per entry: the form Copy and comments use, the same
        /// as the web SDK's.</summary>
        public string Text()
        {
            var text = new StringBuilder();
            foreach (var entry in Entries())
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(" [").Append(Label(entry.Level)).Append("] ").Append(entry.Text);
            }
            return text.ToString();
        }

        public static string Label(PrototirLogLevel level) => level switch
        {
            PrototirLogLevel.Warning => "warn",
            PrototirLogLevel.Error => "error",
            _ => "log",
        };
    }
}
