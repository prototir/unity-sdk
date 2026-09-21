namespace Prototir.Native
{
    /// <summary>Where a downloadable build stands with its prototype.
    ///
    /// <para>Lives on the engine-free side of the seam, with the rest of the protocol: it is a
    /// fact about a pairing, not about Unity, and keeping it here is what lets the rule below be
    /// tested at all.</para></summary>
    public enum PrototirPairingState
    {
        NotPaired,
        Requesting,
        AwaitingApproval,
        Paired,
        Failed,
    }

    public static class PrototirPairing
    {
        /// <summary>The state to report, given what the process remembers and whether a token is
        /// on disk.
        ///
        /// <para>A static field does not survive the process that set it. A build paired yesterday
        /// and started today remembers nothing, so reporting the remembered value alone said
        /// <see cref="PrototirPairingState.NotPaired"/> while the token sat right there, and a game
        /// drawing from it showed a pairing prompt to someone already paired.</para>
        ///
        /// <para>Only the two in-flight values are taken from memory, because nothing on disk can
        /// describe them. <see cref="PrototirPairingState.Failed"/> is kept when there is no token,
        /// so a game can still show why the last attempt did not work.</para></summary>
        public static PrototirPairingState Resting(PrototirPairingState remembered, bool hasToken)
        {
            if (remembered is PrototirPairingState.Requesting or PrototirPairingState.AwaitingApproval)
                return remembered;
            if (hasToken) return PrototirPairingState.Paired;
            // The token is what "paired" means, so a remembered Paired without one is stale: a
            // revoked build, or a token cleared by something that did not think to update a field.
            // Deriving it only in one direction would have left exactly the bug this replaced.
            return remembered == PrototirPairingState.Paired
                ? PrototirPairingState.NotPaired
                : remembered;
        }
    }
}
