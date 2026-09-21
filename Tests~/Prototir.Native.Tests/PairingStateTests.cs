using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests;

/// <summary>What a game is told about its own pairing.
///
/// <para>This was a plain static field, and a static field does not survive the process that set
/// it. A build paired yesterday and started today reported <c>NotPaired</c> while the token sat on
/// disk, so the pairing sample offered "Send test feedback" and "Unpair" under the heading
/// "Status: NotPaired" — a game drawing from this would have shown a pairing prompt to someone
/// already paired.</para></summary>
public class PairingStateTests
{
    /// <summary>The case that was broken: nothing remembered, a token on disk.</summary>
    [Fact]
    public void A_build_that_was_paired_before_this_launch_reports_paired()
    {
        Assert.Equal(
            PrototirPairingState.Paired,
            PrototirPairing.Resting(PrototirPairingState.NotPaired, hasToken: true));
    }

    [Fact]
    public void A_build_with_no_token_reports_not_paired()
    {
        Assert.Equal(
            PrototirPairingState.NotPaired,
            PrototirPairing.Resting(PrototirPairingState.NotPaired, hasToken: false));
    }

    /// <summary>Nothing on disk can describe "a code is on screen right now", so those two come
    /// from memory and must not be overwritten by a token that arrives mid-flow.</summary>
    [Theory]
    [InlineData(PrototirPairingState.Requesting)]
    [InlineData(PrototirPairingState.AwaitingApproval)]
    public void A_pairing_in_flight_is_reported_as_it_happens(PrototirPairingState inFlight)
    {
        Assert.Equal(inFlight, PrototirPairing.Resting(inFlight, hasToken: false));
        Assert.Equal(inFlight, PrototirPairing.Resting(inFlight, hasToken: true));
    }

    /// <summary>A game shows the reason the last attempt failed, so a failure with no token has to
    /// survive rather than flattening back to "not paired".</summary>
    [Fact]
    public void A_failed_attempt_stays_visible_until_something_changes()
    {
        Assert.Equal(
            PrototirPairingState.Failed,
            PrototirPairing.Resting(PrototirPairingState.Failed, hasToken: false));
    }

    /// <summary>Unpairing removes the token, and the next read has to notice rather than keep
    /// reporting the state from before.</summary>
    [Fact]
    public void Losing_the_token_is_noticed_without_being_told()
    {
        Assert.Equal(
            PrototirPairingState.NotPaired,
            PrototirPairing.Resting(PrototirPairingState.Paired, hasToken: false));
    }
}
