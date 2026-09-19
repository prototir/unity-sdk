using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prototir.Native
{
    /// <summary>Sessions that have not reached Prototir yet, kept on disk between runs.
    ///
    /// <para>Quitting does not leave time for an HTTP request. <see cref="PrototirUnityHttp"/>
    /// waits on the player loop, and the player loop is exactly what is ending, so waiting for a
    /// send during <c>Application.quitting</c> blocks the main thread on a continuation that can
    /// never run. Writing one small file is synchronous, takes microseconds and always finishes,
    /// and the next launch has a whole process to send it from. The same file covers the other case
    /// worth covering: a tester playing on a plane.</para>
    ///
    /// <para>Sessions carry their server id, so one arriving late updates its row rather than
    /// counting a second play.</para></summary>
    public sealed class PrototirSessionQueue
    {
        /// <summary>Older sessions are dropped first when the queue is full. A build that never
        /// reaches the network must not grow without limit, and the most recent plays are the ones
        /// still worth reading.</summary>
        public const int MaxPending = 20;

        private const string Extension = ".json";

        private readonly string _directory;
        private readonly Func<long> _sequence;

        /// <summary>The sequence only has to increase: it names the files, and the names are what
        /// puts a tester's three plays back in the order they happened.</summary>
        public PrototirSessionQueue(string directory, Func<long> sequence = null)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _sequence = sequence ?? (() => DateTime.UtcNow.Ticks);
        }

        public bool Store(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return false;
            try
            {
                Directory.CreateDirectory(_directory);
                Trim(MaxPending - 1);
                // The suffix, because two sessions can land on the same tick and the second must
                // not silently replace the first.
                var name = $"{_sequence():D19}-{Guid.NewGuid():N}{Extension}";
                File.WriteAllText(Path.Combine(_directory, name), payload);
                return true;
            }
            catch (Exception)
            {
                // A session that cannot be written down is a session lost, which is a shame and
                // nothing more. It is not worth an exception on the way out of someone's game.
                return false;
            }
        }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<string> Pending()
        {
            try
            {
                if (!Directory.Exists(_directory)) return Array.Empty<string>();
                return Directory.GetFiles(_directory, "*" + Extension)
                    .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Null when the file is gone or unreadable, which callers treat as nothing to
        /// send rather than as a failure to retry.</summary>
        public string Read(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        public void Discard(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private void Trim(int limit)
        {
            var pending = Pending();
            for (var index = 0; index < pending.Count - limit; index++) Discard(pending[index]);
        }
    }
}
