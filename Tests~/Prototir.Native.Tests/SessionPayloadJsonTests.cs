using System.Text.Json;
using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

/// <summary>The exact bytes a session is reported as.
///
/// <para>This is written by hand because <c>JsonUtility</c> cannot leave a field out, and leaving
/// fields out is the whole point. The server models the id as an optional GUID: an always-present
/// <c>"sessionId": ""</c> could not be parsed, the endpoint answered 500, and the SDK's own queue
/// treats 5xx as "retry later" — so every session ever recorded piled up on disk and none were
/// ever sent, with nothing anywhere saying so.</para></summary>
public class SessionPayloadJsonTests
{
    private static JsonElement Parse(PrototirSessionPayload payload) =>
        JsonDocument.Parse(payload.ToJson()).RootElement;

    [Fact]
    public void A_session_with_no_server_id_does_not_mention_one()
    {
        var json = Parse(new PrototirSessionPayload { durationMs = 4200, eventCount = 2 });

        Assert.False(json.TryGetProperty("sessionId", out _));
        Assert.Equal(4200, json.GetProperty("durationMs").GetInt32());
        Assert.Equal(2, json.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public void A_session_the_server_has_already_seen_carries_its_id_back()
    {
        var id = "184b61c3-9603-4fb7-8c73-9dc3722997a9";
        var json = Parse(new PrototirSessionPayload { durationMs = 1, sessionId = id });

        Assert.Equal(id, json.GetProperty("sessionId").GetString());
    }

    /// <summary>The score is optional too, and zero is a real score. Sending it for a play that
    /// never scored would put a nought on a leaderboard nobody earned.</summary>
    [Fact]
    public void A_play_that_never_scored_reports_no_score()
    {
        var json = Parse(new PrototirSessionPayload { durationMs = 1, hasScore = false, score = 0 });

        Assert.False(json.TryGetProperty("score", out _));
    }

    [Fact]
    public void A_real_score_of_zero_is_still_reported()
    {
        var json = Parse(new PrototirSessionPayload { durationMs = 1, hasScore = true, score = 0 });

        Assert.Equal(0, json.GetProperty("score").GetInt32());
    }

    [Fact]
    public void Signals_are_reported_as_a_name_and_a_count()
    {
        var payload = new PrototirSessionPayload { durationMs = 1 };
        payload.signals.Add(new PrototirSignal { name = "points_added", count = 3 });
        payload.signals.Add(new PrototirSignal { name = "level.up", count = 1 });

        var signals = Parse(payload).GetProperty("signals");
        Assert.Equal(2, signals.GetArrayLength());
        Assert.Equal("points_added", signals[0].GetProperty("name").GetString());
        Assert.Equal(3, signals[0].GetProperty("count").GetInt32());
        Assert.Equal("level.up", signals[1].GetProperty("name").GetString());
    }

    /// <summary>An empty list still has to be a list, not a missing field or a stray comma.</summary>
    [Fact]
    public void A_session_with_no_signals_still_produces_valid_json()
    {
        var json = Parse(new PrototirSessionPayload { durationMs = 1 });

        Assert.Equal(0, json.GetProperty("signals").GetArrayLength());
    }

    /// <summary>Hand-written JSON is only safe while nothing in it needs escaping. Event names are
    /// normalized before they can reach here, so this pins the assumption rather than trusting
    /// it: anything the recorder accepts must come out parseable.</summary>
    [Theory]
    [InlineData("a-b:c_d.9")]
    [InlineData("level.up")]
    [InlineData("x")]
    public void Every_name_the_recorder_accepts_survives_serialization(string name)
    {
        Assert.Equal(name, PrototirSessionRecorder.NormalizeEventName(name));

        var payload = new PrototirSessionPayload { durationMs = 1 };
        payload.signals.Add(new PrototirSignal { name = name, count = 1 });

        Assert.Equal(name, Parse(payload).GetProperty("signals")[0].GetProperty("name").GetString());
    }
}
