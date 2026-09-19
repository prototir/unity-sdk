using System;
using System.Collections.Generic;
using System.Linq;

namespace Prototir.Native
{
    /// <summary>One session, in the shape the server records it.</summary>
    [Serializable]
    public sealed class PrototirSessionPayload
    {
        public int durationMs;
        public int eventCount;
        public int score;
        public bool hasScore;
        public List<PrototirSignal> signals = new();
        /// <summary>Set once the server has given this session a row, so a later flush updates it
        /// instead of recording a second session for the same play.</summary>
        public string sessionId;
    }

    [Serializable]
    public sealed class PrototirSignal
    {
        public string name;
        public int count;
    }

    /// <summary>Accumulates a play so it can be reported as one session.
    ///
    /// <para>The browser shell watches a prototype and reports for it. A download has no shell, so
    /// the build must keep its own count and send one session rather than a call per event: the
    /// endpoint records a session, and a request per <c>Event()</c> would be both wrong and a
    /// good way to burn a player's connection.</para></summary>
    public sealed class PrototirSessionRecorder
    {
        /// <summary>Distinct event names kept. Past this the counts still accumulate into the
        /// total, but the per-name breakdown stops growing: a runaway loop generating unique names
        /// must not turn one session into an unbounded payload.
        ///
        /// <para>The same number the server keeps (<c>MaxSignalNamesPerSession</c>). Sending more
        /// would not record more: the server drops the surplus silently, so the only effect of a
        /// larger number here would be a bigger payload and a different answer to "how many
        /// events can I use" depending on who you ask.</para></summary>
        public const int MaxDistinctSignals = 50;

        private readonly Func<DateTimeOffset> _now;
        private readonly Dictionary<string, int> _signals = new(StringComparer.Ordinal);
        private DateTimeOffset? _startedAt;
        private int _eventCount;
        private double? _score;

        public PrototirSessionRecorder(Func<DateTimeOffset> now) =>
            _now = now ?? throw new ArgumentNullException(nameof(now));

        public string SessionId { get; set; }

        /// <summary>The scene is interactive. Called again mid-session is ignored rather than
        /// restarting the clock, because a game that calls it from two places should not silently
        /// halve its own play time.</summary>
        public void Ready() => _startedAt ??= _now();

        public void Event(string name)
        {
            var normalized = NormalizeEventName(name);
            if (normalized == null) return;

            // The clock starts at the first sign of life, whichever it is: a build that reports
            // events without ever calling Ready still has a real duration.
            _startedAt ??= _now();
            _eventCount++;
            if (_signals.ContainsKey(normalized)) _signals[normalized]++;
            else if (_signals.Count < MaxDistinctSignals) _signals[normalized] = 1;
        }

        /// <summary>The best score of the session. A run that ends badly should not erase what the
        /// player already achieved, and the leaderboard takes the maximum anyway.</summary>
        public void Score(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            if (_score is null || value > _score) _score = value;
        }

        public bool HasAnythingToReport => _startedAt is not null;

        public PrototirSessionPayload Snapshot()
        {
            var duration = _startedAt is { } started
                ? (int)Math.Max(0, Math.Min(int.MaxValue, (_now() - started).TotalMilliseconds))
                : 0;

            return new PrototirSessionPayload
            {
                durationMs = duration,
                eventCount = _eventCount,
                score = _score is { } s ? (int)Math.Round(Math.Clamp(s, int.MinValue, int.MaxValue)) : 0,
                hasScore = _score is not null,
                sessionId = SessionId,
                signals = _signals
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new PrototirSignal { name = pair.Key, count = pair.Value })
                    .ToList(),
            };
        }

        public void Reset()
        {
            _startedAt = null;
            _eventCount = 0;
            _score = null;
            _signals.Clear();
            SessionId = null;
        }

        /// <summary>Matches the name rule the platform already enforces, so a name the browser
        /// path would accept is not silently dropped natively and the reverse.</summary>
        public static string NormalizeEventName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var trimmed = name.Trim().ToLowerInvariant();
            if (trimmed.Length > 64) return null;
            foreach (var c in trimmed)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                    || c is '_' or '.' or ':' or '-';
                if (!ok) return null;
            }
            return trimmed;
        }
    }
}
