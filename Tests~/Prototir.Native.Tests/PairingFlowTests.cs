using System;
using System.Threading;
using System.Threading.Tasks;
using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

/// <summary>Pairing hands a game the ability to post as a person, and a downloaded build has no
/// session to fall back on, so the states worth pinning are the ones where a wrong answer either
/// loops forever or throws away a valid grant.</summary>
public class PairingFlowTests
{
    [Fact]
    public async Task A_started_pairing_reports_what_the_tester_needs_to_see()
    {
        var http = new FakeHttp().Reply(200, """
            {"code":"ABCD-EFGH","verificationUrl":"https://prototir.com/link?code=ABCD-EFGH",
             "qrSvgDataUrl":"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=",
             "prototypeTitle":"Night Drive","expiresInSeconds":900,"intervalSeconds":5}
            """);

        var request = await Flow(http).StartAsync("Evandro's PC", "build-1", default);

        Assert.Equal("ABCD-EFGH", request.Code);
        Assert.Equal("https://prototir.com/link?code=ABCD-EFGH", request.VerificationUrl);
        Assert.Equal("Night Drive", request.PrototypeTitle);
        Assert.Equal(TimeSpan.FromSeconds(900), request.ExpiresIn);
        // Decoded from the data URL, because the game draws an image, not a base64 string.
        Assert.Equal("<svg></svg>", request.QrSvg);
    }

    [Fact]
    public async Task The_build_id_and_label_travel_with_the_request()
    {
        var http = new FakeHttp().Reply(200, """{"code":"AAAA-BBBB","expiresInSeconds":900}""");

        await Flow(http).StartAsync("Steam Deck", "7f3a91c2", default);

        Assert.Contains("Steam Deck", http.Calls[0].Body);
        Assert.Contains("7f3a91c2", http.Calls[0].Body);
        Assert.EndsWith("/prototypes/night-drive/pair", http.Calls[0].Url);
        // Pairing is how a build gets an identity; it cannot present one yet.
        Assert.Null(http.Calls[0].Bearer);
    }

    [Fact]
    public async Task Approval_after_two_pending_polls_returns_the_token()
    {
        var http = new FakeHttp()
            .Reply(202, """{"pending":true,"intervalSeconds":5}""")
            .Reply(202, """{"pending":true,"intervalSeconds":5}""")
            .Reply(200, """{"token":"jwt-value"}""");
        var clock = new VirtualClock();

        var result = await Flow(http, clock).AwaitApprovalAsync(
            "ABCD-EFGH", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5), default);

        Assert.Equal(PrototirPairingOutcome.Approved, result.Outcome);
        Assert.Equal("jwt-value", result.Token);
        Assert.Equal(3, http.Calls.Count);
    }

    /// <summary>410 covers expired, unknown, already used and revoked, deliberately
    /// indistinguishable so a caller cannot probe which codes exist. Polling through it would
    /// hammer the server forever for a code that can never work.</summary>
    [Fact]
    public async Task A_410_stops_immediately_rather_than_being_retried()
    {
        var http = new FakeHttp().Reply(410, """{"error":"no longer valid"}""");

        var result = await Flow(http).AwaitApprovalAsync(
            "ABCD-EFGH", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5), default);

        Assert.Equal(PrototirPairingOutcome.Expired, result.Outcome);
        Assert.Single(http.Calls);
    }

    [Fact]
    public async Task The_server_sets_the_cadence_not_the_client()
    {
        var http = new FakeHttp()
            .Reply(202, """{"pending":true,"intervalSeconds":11}""")
            .Reply(200, """{"token":"jwt-value"}""");
        var clock = new VirtualClock();

        await Flow(http, clock).AwaitApprovalAsync(
            "ABCD-EFGH", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(3), default);

        Assert.Equal(new[] { TimeSpan.FromSeconds(11) }, clock.Waits);
    }

    /// <summary>A tester walking to their phone should not have to start over because one request
    /// was dropped, so a lost connection is temporary, unlike a refusal the server chose.</summary>
    [Fact]
    public async Task A_dropped_request_is_retried_rather_than_ending_the_pairing()
    {
        var http = new FakeHttp()
            .ReplyTransportFailure()
            .Reply(500)
            .Reply(200, """{"token":"jwt-value"}""");

        var result = await Flow(http, new VirtualClock()).AwaitApprovalAsync(
            "ABCD-EFGH", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5), default);

        Assert.Equal(PrototirPairingOutcome.Approved, result.Outcome);
        Assert.Equal(3, http.Calls.Count);
    }

    [Fact]
    public async Task Nobody_approving_ends_at_the_deadline_instead_of_polling_forever()
    {
        // Default reply is "pending", so this only terminates if the deadline is honoured.
        var http = new FakeHttp();
        var clock = new VirtualClock();

        var result = await Flow(http, clock).AwaitApprovalAsync(
            "ABCD-EFGH", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), default);

        Assert.Equal(PrototirPairingOutcome.Expired, result.Outcome);
        Assert.InRange(http.Calls.Count, 2, 10);
    }

    [Fact]
    public async Task Cancelling_stops_the_loop_and_is_not_reported_as_a_failure()
    {
        // Cancelled up front rather than on a timer: the clock here is virtual, so a wall-clock
        // timer loses the race against fifteen virtual minutes elapsing in microseconds.
        var http = new FakeHttp();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var result = await Flow(http, new VirtualClock()).AwaitApprovalAsync(
            "ABCD-EFGH", TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5), cancel.Token);

        Assert.Equal(PrototirPairingOutcome.Cancelled, result.Outcome);
        Assert.Empty(http.Calls);
    }

    /// <summary>Revoked and wrong-prototype both answer 403. Retrying either just repeats a
    /// refusal; the token has to be dropped and the build paired again.</summary>
    [Theory]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(500, false)]
    [InlineData(0, false)]
    [InlineData(200, false)]
    public void A_token_is_finished_only_when_the_server_refuses_it(int status, bool terminal)
    {
        Assert.Equal(terminal, PrototirPairingFlow.IsTokenTerminal(status));
    }

    [Fact]
    public async Task A_refused_start_reports_nothing_rather_than_a_half_built_request()
    {
        var http = new FakeHttp().Reply(403, """{"error":"Feedback is turned off"}""");

        Assert.Null(await Flow(http).StartAsync(null, null, default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/qr.png")]
    [InlineData("data:image/svg+xml;base64,not-valid-base64!!")]
    public void An_unusable_qr_does_not_cost_the_pairing(string dataUrl)
    {
        // The code and the URL are what a tester actually needs; the QR is a convenience.
        Assert.Null(PrototirPairingFlow.DecodeQr(dataUrl));
    }

    private static PrototirPairingFlow Flow(FakeHttp http, VirtualClock clock = null)
    {
        clock ??= new VirtualClock();
        return new PrototirPairingFlow(
            http, new SystemTextJsonCodec(), clock, clock.Now,
            "https://prototir.com/api", "night-drive");
    }
}
