using System;
using System.IO;
using System.Linq;
using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

/// <summary>Sessions that survive a quit and a plane. A download reports nothing while the player
/// is playing, so the queue is the only thing standing between a play and a creator never hearing
/// about it.</summary>
public class SessionQueueTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "prototir-queue-" + Guid.NewGuid().ToString("N"));

    private long _tick;

    /// <summary>A sequence that only moves when the test says so, because the ordering guarantee
    /// is what puts a tester's three plays back in the order they happened.</summary>
    private PrototirSessionQueue Queue() => new(_directory, () => ++_tick);

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void A_queue_nobody_has_written_to_is_empty_rather_than_missing()
    {
        Assert.Empty(Queue().Pending());
    }

    [Fact]
    public void A_stored_session_comes_back_exactly_as_it_was_written()
    {
        var queue = Queue();
        Assert.True(queue.Store("{\"durationMs\":1200}"));

        var pending = queue.Pending();
        Assert.Single(pending);
        Assert.Equal("{\"durationMs\":1200}", queue.Read(pending[0]));
    }

    [Fact]
    public void Sessions_come_back_oldest_first()
    {
        var queue = Queue();
        queue.Store("first");
        queue.Store("second");
        queue.Store("third");

        Assert.Equal(
            new[] { "first", "second", "third" },
            queue.Pending().Select(queue.Read).ToArray());
    }

    /// <summary>Two sessions can land on the same tick. The second must not quietly replace the
    /// first, which is what a name built only from the clock would do.</summary>
    [Fact]
    public void Two_sessions_stored_at_the_same_instant_both_survive()
    {
        var queue = new PrototirSessionQueue(_directory, () => 7);
        queue.Store("one");
        queue.Store("two");

        Assert.Equal(2, queue.Pending().Count);
    }

    [Fact]
    public void A_sent_session_is_dropped_and_dropping_it_twice_is_harmless()
    {
        var queue = Queue();
        queue.Store("payload");
        var path = queue.Pending()[0];

        queue.Discard(path);
        queue.Discard(path);

        Assert.Empty(queue.Pending());
        Assert.Null(queue.Read(path));
    }

    /// <summary>A build that never reaches the network must not grow without limit, and the most
    /// recent plays are the ones still worth reading.</summary>
    [Fact]
    public void A_full_queue_keeps_the_newest_sessions_and_drops_the_oldest()
    {
        var queue = Queue();
        for (var index = 0; index < PrototirSessionQueue.MaxPending + 5; index++)
            queue.Store(index.ToString());

        var kept = queue.Pending().Select(queue.Read).ToArray();
        Assert.Equal(PrototirSessionQueue.MaxPending, kept.Length);
        Assert.Equal("5", kept.First());
        Assert.Equal((PrototirSessionQueue.MaxPending + 4).ToString(), kept.Last());
    }

    [Fact]
    public void An_empty_payload_is_not_worth_a_file()
    {
        var queue = Queue();

        Assert.False(queue.Store(null));
        Assert.False(queue.Store("   "));
        Assert.Empty(queue.Pending());
    }

    /// <summary>Every path here runs while someone's game is closing. Throwing there would turn a
    /// lost session into a crash on the way out, which is a far worse trade.</summary>
    [Fact]
    public void A_directory_that_cannot_be_used_costs_a_session_and_nothing_more()
    {
        var file = Path.Combine(Path.GetTempPath(), "prototir-not-a-dir-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "this is a file, not a directory");
        try
        {
            var queue = new PrototirSessionQueue(Path.Combine(file, "pending"));

            Assert.False(queue.Store("payload"));
            Assert.Empty(queue.Pending());
            Assert.Null(queue.Read(Path.Combine(file, "pending", "nope.json")));
            queue.Discard(Path.Combine(file, "pending", "nope.json"));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
