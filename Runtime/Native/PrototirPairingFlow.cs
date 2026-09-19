using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prototir.Native
{
    public enum PrototirPairingOutcome
    {
        Approved,
        /// <summary>The code will never become valid: expired, unknown, already used, or revoked.
        /// The server answers 410 for all of them on purpose, so a caller cannot probe which codes
        /// exist, and the client must not distinguish them either.</summary>
        Expired,
        Cancelled,
        Failed,
    }

    /// <summary>Plain settable properties, not `init`: Unity compiles this package as
    /// netstandard2.1, which has no `IsExternalInit`, so `init` accessors do not compile
    /// there even though the net8.0 test project accepts them.</summary>
    public sealed class PrototirPairingResult
    {
        public PrototirPairingOutcome Outcome { get; set; }
        public string Token { get; set; }
        public string Message { get; set; }
    }

    /// <summary>What the game shows a tester while it waits.</summary>
    public sealed class PrototirPairingRequest
    {
        public string Code { get; set; }
        public string VerificationUrl { get; set; }
        /// <summary>The QR the server rendered, decoded from its data URL. Rendered server-side
        /// deliberately: a client-side encoder is dense, easy to get subtly wrong, and would be
        /// written three times, and the place it matters most is where it is most awkward.</summary>
        public string QrSvg { get; set; }
        public string PrototypeTitle { get; set; }
        public TimeSpan ExpiresIn { get; set; }
        public TimeSpan PollInterval { get; set; }
    }

    /// <summary>The device code flow, with no engine in it.
    ///
    /// <para>A downloaded build has no browser session: it shows a code, the tester approves it on
    /// prototir.com, and the build polls until a token comes back. Loopback redirects and custom
    /// URI schemes were both rejected for this, because a downloaded build is unsigned and opening
    /// a socket trips a firewall prompt at the worst moment, while nothing registers a URI scheme
    /// for a zip.</para></summary>
    public sealed class PrototirPairingFlow
    {
        private readonly IPrototirHttp _http;
        private readonly IPrototirJson _json;
        private readonly IPrototirDelay _delay;
        private readonly Func<DateTimeOffset> _now;
        private readonly string _apiBase;
        private readonly string _slug;

        public PrototirPairingFlow(
            IPrototirHttp http,
            IPrototirJson json,
            IPrototirDelay delay,
            Func<DateTimeOffset> now,
            string apiBase,
            string slug)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _json = json ?? throw new ArgumentNullException(nameof(json));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _apiBase = (apiBase ?? string.Empty).TrimEnd('/');
            _slug = slug;
        }

        /// <summary>Fallback cadence when the server does not say. Never poll faster than this.</summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

        public async Task<PrototirPairingRequest> StartAsync(
            string deviceLabel, string buildId, CancellationToken ct)
        {
            var body = _json.Encode(new StartBody { deviceLabel = deviceLabel, buildId = buildId });
            var response = await _http.PostJsonAsync(Url("pair"), body, null, ct).ConfigureAwait(false);
            if (response.Status != 200) return null;

            var payload = _json.Decode<StartResponse>(response.Body);
            if (payload == null || string.IsNullOrEmpty(payload.code)) return null;

            return new PrototirPairingRequest
            {
                Code = payload.code,
                VerificationUrl = payload.verificationUrl,
                QrSvg = DecodeQr(payload.qrSvgDataUrl),
                PrototypeTitle = payload.prototypeTitle,
                ExpiresIn = TimeSpan.FromSeconds(Math.Max(1, payload.expiresInSeconds)),
                PollInterval = Interval(payload.intervalSeconds),
            };
        }

        /// <summary>Polls until approved, refused, or the deadline passes.</summary>
        public async Task<PrototirPairingResult> AwaitApprovalAsync(
            string code, TimeSpan timeout, TimeSpan pollInterval, CancellationToken ct)
        {
            var deadline = _now().Add(timeout);
            var wait = pollInterval <= TimeSpan.Zero ? DefaultPollInterval : pollInterval;
            var body = _json.Encode(new PollBody { code = code });

            while (true)
            {
                if (ct.IsCancellationRequested)
                    return new PrototirPairingResult { Outcome = PrototirPairingOutcome.Cancelled };

                PrototirHttpResponse response;
                try
                {
                    response = await _http.PostJsonAsync(Url("pair/poll"), body, null, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new PrototirPairingResult { Outcome = PrototirPairingOutcome.Cancelled };
                }

                switch (response.Status)
                {
                    case 200:
                        var approved = _json.Decode<PollResponse>(response.Body);
                        if (approved != null && !string.IsNullOrEmpty(approved.token))
                            return new PrototirPairingResult
                            {
                                Outcome = PrototirPairingOutcome.Approved,
                                Token = approved.token,
                            };
                        return new PrototirPairingResult
                        {
                            Outcome = PrototirPairingOutcome.Failed,
                            Message = "The server approved the pairing but returned no token.",
                        };

                    case 410:
                        return new PrototirPairingResult
                        {
                            Outcome = PrototirPairingOutcome.Expired,
                            Message = "This code is no longer valid. Start again.",
                        };

                    case 400:
                        return new PrototirPairingResult
                        {
                            Outcome = PrototirPairingOutcome.Failed,
                            Message = "That pairing code was not accepted.",
                        };

                    case 202:
                        var pending = _json.Decode<PollResponse>(response.Body);
                        // The server owns the cadence; honour it rather than hammering.
                        if (pending != null && pending.intervalSeconds > 0)
                            wait = Interval(pending.intervalSeconds);
                        break;

                    default:
                        // Anything else, including a transport failure, is treated as temporary:
                        // pairing is a person walking to their phone, and one dropped request
                        // should not make them start over.
                        break;
                }

                if (_now() >= deadline)
                    return new PrototirPairingResult
                    {
                        Outcome = PrototirPairingOutcome.Expired,
                        Message = "Nobody approved this code in time. Start again.",
                    };

                try
                {
                    await _delay.WaitAsync(wait, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new PrototirPairingResult { Outcome = PrototirPairingOutcome.Cancelled };
                }
            }
        }

        /// <summary>Whether a response to an authenticated call means this token is finished.
        /// Revoked and wrong-prototype both answer 403, and neither is worth retrying: the flow
        /// must drop the token and pair again rather than looping on a refusal.</summary>
        public static bool IsTokenTerminal(int status) => status == 401 || status == 403;

        private static TimeSpan Interval(int seconds) =>
            seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultPollInterval;

        private string Url(string suffix) =>
            $"{_apiBase}/prototypes/{Uri.EscapeDataString(_slug ?? string.Empty)}/{suffix}";

        internal static string DecodeQr(string dataUrl)
        {
            const string prefix = "data:image/svg+xml;base64,";
            if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            try
            {
                return System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(dataUrl.Substring(prefix.Length)));
            }
            catch (FormatException)
            {
                // A malformed QR is not worth failing a pairing over: the code and the URL are
                // both still shown, and those are what a tester actually needs.
                return null;
            }
        }

        [Serializable]
        private sealed class StartBody
        {
            public string deviceLabel;
            public string buildId;
        }

        [Serializable]
        private sealed class PollBody
        {
            public string code;
        }

        [Serializable]
        public sealed class StartResponse
        {
            public string code;
            public string verificationUrl;
            public string qrSvgDataUrl;
            public string prototypeTitle;
            public int expiresInSeconds;
            public int intervalSeconds;
        }

        [Serializable]
        public sealed class PollResponse
        {
            public string token;
            public bool pending;
            public int intervalSeconds;
        }
    }
}
