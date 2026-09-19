using System;
using System.Linq;
using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

/// <summary>What a download reports about a play. The server decides whether a session is "real"
/// (3s and at least one event) and that gate controls whether feedback and ranking count it, so
/// under-reporting here quietly costs a creator their plays.</summary>
public class SessionRecorderTests
{
    [Fact]
    public void Duration_runs_from_the_moment_the_scene_became_interactive()
    {
        var clock = new StepClock();
        var recorder = new PrototirSessionRecorder(clock.Now);

        clock.Advance(TimeSpan.FromSeconds(9)); // loading: not play time
        recorder.Ready();
        clock.Advance(TimeSpan.FromSeconds(42));

        Assert.Equal(42_000, recorder.Snapshot().durationMs);
    }

    /// <summary>A game may call Ready from a scene loader and again from a controller. Restarting
    /// the clock there would silently halve its own reported play time.</summary>
    [Fact]
    public void Calling_ready_twice_does_not_restart_the_clock()
    {
        var clock = new StepClock();
        var recorder = new PrototirSessionRecorder(clock.Now);

        recorder.Ready();
        clock.Advance(TimeSpan.FromSeconds(30));
        recorder.Ready();
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(40_000, recorder.Snapshot().durationMs);
    }

    /// <summary>Some builds never call Ready but do report events. Treating that as a zero-length
    /// session would make every one of them fail the real-session gate.</summary>
    [Fact]
    public void An_event_starts_the_clock_when_ready_was_never_called()
    {
        var clock = new StepClock();
        var recorder = new PrototirSessionRecorder(clock.Now);

        recorder.Event("level_start");
        clock.Advance(TimeSpan.FromSeconds(12));

        Assert.Equal(12_000, recorder.Snapshot().durationMs);
        Assert.True(recorder.HasAnythingToReport);
    }

    [Fact]
    public void Nothing_happening_is_reported_as_nothing_rather_than_an_empty_session()
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);

        Assert.False(recorder.HasAnythingToReport);
        Assert.Equal(0, recorder.Snapshot().durationMs);
    }

    [Fact]
    public void Events_are_counted_in_total_and_broken_down_by_name()
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);

        recorder.Event("level_complete");
        recorder.Event("level_complete");
        recorder.Event("died");

        var payload = recorder.Snapshot();
        Assert.Equal(3, payload.eventCount);
        Assert.Equal(new[] { ("level_complete", 2), ("died", 1) },
            payload.signals.Select(s => (s.name, s.count)));
    }

    /// <summary>The leaderboard takes the best of a session, so a run that ends badly must not
    /// erase what the player already achieved.</summary>
    [Fact]
    public void The_best_score_of_the_session_is_what_gets_reported()
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);

        recorder.Score(120);
        recorder.Score(2400);
        recorder.Score(30);

        var payload = recorder.Snapshot();
        Assert.True(payload.hasScore);
        Assert.Equal(2400, payload.score);
    }

    [Fact]
    public void A_session_without_a_score_says_so_rather_than_reporting_zero()
    {
        // Zero is a real score in plenty of games; "no score" is not the same thing.
        var recorder = new PrototirSessionRecorder(new StepClock().Now);
        recorder.Ready();

        Assert.False(recorder.Snapshot().hasScore);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_score_that_is_not_a_number_is_ignored_rather_than_sent(double value)
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);

        recorder.Score(100);
        recorder.Score(value);

        Assert.Equal(100, recorder.Snapshot().score);
    }

    [Theory]
    [InlineData("Level_Complete", "level_complete")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("a.b:c-d_e", "a.b:c-d_e")]
    public void Event_names_are_normalized_the_way_the_platform_expects(string given, string expected)
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);

        recorder.Event(given);

        Assert.Equal(expected, recorder.Snapshot().signals.Single().name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("has spaces")]
    [InlineData("emoji-🎮")]
    public void An_unusable_event_name_is_dropped_without_inflating_the_count(string name)
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);
        recorder.Ready();

        recorder.Event(name);

        Assert.Equal(0, recorder.Snapshot().eventCount);
    }

    /// <summary>A loop generating unique names must not turn one session into an unbounded
    /// payload, but the plays themselves still happened and still count.</summary>
    [Fact]
    public void Unbounded_distinct_names_stop_growing_the_breakdown_not_the_total()
    {
        var recorder = new PrototirSessionRecorder(new StepClock().Now);

        for (var i = 0; i < 500; i++) recorder.Event($"event_{i}");

        var payload = recorder.Snapshot();
        Assert.Equal(500, payload.eventCount);
        Assert.Equal(PrototirSessionRecorder.MaxDistinctSignals, payload.signals.Count);
    }

    [Fact]
    public void Resetting_clears_the_session_and_the_id_it_was_recorded_under()
    {
        var clock = new StepClock();
        var recorder = new PrototirSessionRecorder(clock.Now) { SessionId = "abc" };
        recorder.Ready();
        recorder.Event("x");
        clock.Advance(TimeSpan.FromSeconds(5));

        recorder.Reset();

        var payload = recorder.Snapshot();
        Assert.False(recorder.HasAnythingToReport);
        Assert.Equal(0, payload.eventCount);
        Assert.Equal(0, payload.durationMs);
        // Reusing an id across sessions makes the server answer 404 on the next flush.
        Assert.Null(payload.sessionId);
    }

    private sealed class StepClock
    {
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Now() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
