using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Prototir.Native;

namespace Prototir.Native.Tests;

/// <summary>A scripted server. Each queued reply answers one request, so a test states the exact
/// sequence a build would meet rather than a stub that always says the same thing.</summary>
public sealed class FakeHttp : IPrototirHttp
{
    private readonly Queue<PrototirHttpResponse> _replies = new();

    public List<(string Url, string Body, string Bearer)> Calls { get; } = new();

    /// <summary>Answered once the script runs out, so a runaway poll loop shows up as a large
    /// call count instead of an exception that hides the real failure.</summary>
    public PrototirHttpResponse Default { get; set; } = new(202, """{"pending":true}""");

    public FakeHttp Reply(int status, string body = "")
    {
        _replies.Enqueue(new PrototirHttpResponse(status, body));
        return this;
    }

    /// <summary>A request that never reached the server: no DNS, no route, timed out.</summary>
    public FakeHttp ReplyTransportFailure() => Reply(0);

    public Task<PrototirHttpResponse> PostJsonAsync(
        string url, string json, string bearer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add((url, json, bearer));
        return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : Default);
    }
}

/// <summary>System.Text.Json here; Unity uses JsonUtility. The protocol depends on neither, which
/// is what lets these tests run at all.</summary>
public sealed class SystemTextJsonCodec : IPrototirJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    public string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);

    public T Decode<T>(string json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, Options) ?? new T(); }
        catch (JsonException) { return new T(); }
    }
}

/// <summary>Advances a clock instead of sleeping, so a poll loop with a five second cadence runs
/// in microseconds and the test still asserts the real waits.</summary>
public sealed class VirtualClock : IPrototirDelay
{
    private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public List<TimeSpan> Waits { get; } = new();

    public DateTimeOffset Now() => _now;

    public Task WaitAsync(TimeSpan duration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Waits.Add(duration);
        _now = _now.Add(duration);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryTokenStore : IPrototirTokenStore
{
    private readonly Dictionary<string, string> _tokens = new();

    public string Read(string slug) => _tokens.TryGetValue(slug, out var token) ? token : null;
    public void Write(string slug, string token) => _tokens[slug] = token;
    public void Clear(string slug) => _tokens.Remove(slug);
    public int Count => _tokens.Count;
}
