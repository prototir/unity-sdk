using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Prototir.Native
{
    /// <summary>Everything a downloadable build needs at runtime, in one place: configuration, the
    /// stored token, the session being accumulated, and the pairing state a game draws.
    ///
    /// <para>Deliberately not a MonoBehaviour. The only thing it needs from the engine is a
    /// moment to flush on quit, which <see cref="Application.quitting"/> provides, and staying
    /// plain keeps the whole thing usable from a test.</para></summary>
    public static class PrototirNativeRuntime
    {
        private const string RevokedMessage = "Access to this build was withdrawn. Pair it again.";

        private static readonly object Gate = new();
        private static PrototirSessionRecorder _session;
        private static PrototirSettings _settings;
        private static IPrototirTokenStore _tokens;
        private static PrototirPairingFlow _flow;
        private static CancellationTokenSource _pairingCancel;
        private static PrototirSessionQueue _queue;
        private static bool _configurationWarned;
        private static bool _storedOnQuit;

        /// <summary>Raised when a pairing code is ready to show. The SDK never draws it: it cannot
        /// know the game's art direction, its input model, or whether it is in VR.</summary>
        public static event Action<PrototirPairingRequest> PairingStarted;
        public static event Action PairingSucceeded;
        public static event Action<string> PairingFailed;

        private static PrototirPairingState _state = PrototirPairingState.NotPaired;

        /// <summary>Where this build stands. See <see cref="PrototirPairing.Resting"/> for why the
        /// answer cannot come from memory alone.</summary>
        public static PrototirPairingState PairingState => PrototirPairing.Resting(_state, IsPaired);

        public static bool IsPaired => !string.IsNullOrEmpty(Token);

        private static string Token
        {
            get
            {
                var slug = Settings?.PrototypeSlug;
                return string.IsNullOrEmpty(slug) ? null : Tokens.Read(slug);
            }
        }

        private static PrototirSettings Settings => _settings ??= PrototirSettings.Load();
        private static IPrototirTokenStore Tokens => _tokens ??= new PrototirUnityTokenStore();
        private static PrototirSessionRecorder Session =>
            _session ??= new PrototirSessionRecorder(() => DateTimeOffset.UtcNow);

        private static PrototirSessionQueue Queue => _queue ??= new PrototirSessionQueue(
            Path.Combine(Application.persistentDataPath, "prototir", "pending"));

        /// <summary>Overrides the settings asset, for a game that decides its slug at runtime.</summary>
        public static void Configure(PrototirSettings settings)
        {
            _settings = settings;
            _flow = null;
            _configurationWarned = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Application.quitting += StoreSession;
            // Deliberately not awaited: nothing in the game should wait on last session getting
            // through, and a failure here is already handled by leaving it in the queue.
            _ = SendPendingAsync(CancellationToken.None);
        }

        /// <summary>A session that is never sent is a play the creator never sees, and quitting is
        /// the normal way a desktop game ends. There is no time to send one here, so it is written
        /// down instead: see <see cref="PrototirSessionQueue"/> for why waiting would hang.</summary>
        public static void StoreSession()
        {
            if (_storedOnQuit) return;
            _storedOnQuit = true;
            if (!Session.HasAnythingToReport) return;
            if (Settings == null || !Settings.IsConfigured) return;
            if (string.IsNullOrEmpty(Token)) return;
            Queue.Store(new PrototirUnityJson().Encode(Session.Snapshot()));
        }

        /// <summary>Sends what earlier runs left behind. Anything the server takes, or refuses in a
        /// way that will not change, is dropped; anything that failed because the network did is
        /// kept for next time.</summary>
        public static async Task SendPendingAsync(CancellationToken ct)
        {
            // Quiet rather than EnsureConfigured: this runs at boot in every build, including ones
            // whose creator never asked for any of this.
            if (Settings == null || !Settings.IsConfigured) return;
            var token = Token;
            if (string.IsNullOrEmpty(token)) return;

            foreach (var path in Queue.Pending())
            {
                if (ct.IsCancellationRequested) return;
                var body = Queue.Read(path);
                if (string.IsNullOrWhiteSpace(body))
                {
                    Queue.Discard(path);
                    continue;
                }

                PrototirHttpResponse response;
                try
                {
                    response = await new PrototirUnityHttp()
                        .PostJsonAsync(SessionsUrl(), body, token, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                if (PrototirPairingFlow.IsTokenTerminal(response.Status))
                {
                    Unpair();
                    PairingFailed?.Invoke(RevokedMessage);
                    return;
                }
                if (response.Status == 0 || response.Status >= 500)
                {
                    // Offline, or the server is unwell. Keeping the rest is the whole point.
                    return;
                }
                Queue.Discard(path);
            }
        }

        private static string SessionsUrl() =>
            $"{Settings.ApiBaseUrl.TrimEnd('/')}/prototypes/{Uri.EscapeDataString(Settings.PrototypeSlug)}/sessions";

        public static void Ready() => Session.Ready();

        public static void Event(string name) => Session.Event(name);

        public static void Score(double value) => Session.Score(value);

        /// <summary>Sends the session so far. Safe to call repeatedly: the server updates the same
        /// row once it has given the session an id.</summary>
        public static async Task FlushAsync(CancellationToken ct)
        {
            if (!Session.HasAnythingToReport) return;
            if (!EnsureConfigured()) return;

            var token = Token;
            if (string.IsNullOrEmpty(token)) return; // Unpaired builds report nothing, by design.

            var payload = Session.Snapshot();
            var body = new PrototirUnityJson().Encode(payload);
            try
            {
                var response = await new PrototirUnityHttp()
                    .PostJsonAsync(SessionsUrl(), body, token, ct).ConfigureAwait(false);

                if (PrototirPairingFlow.IsTokenTerminal(response.Status))
                {
                    // Revoked, or aimed at a prototype this token was not issued for. Retrying
                    // just repeats the refusal.
                    Unpair();
                    PairingFailed?.Invoke(RevokedMessage);
                    return;
                }
                if (response.Status == 0 || response.Status >= 500)
                {
                    // Not sent, and not the caller's problem to solve. It goes in the queue and
                    // leaves with the next launch.
                    Queue.Store(body);
                    return;
                }
                if (response.Status == 200)
                {
                    var recorded = new PrototirUnityJson().Decode<SessionResponse>(response.Body);
                    if (!string.IsNullOrEmpty(recorded.sessionId)) Session.SessionId = recorded.sessionId;
                }
            }
            catch (Exception error)
            {
                Queue.Store(body);
                // Losing a session is not worth interrupting someone's game over.
                Debug.LogWarning($"Prototir: could not report this session ({error.Message}).");
            }
        }

        /// <summary>Asks for a pairing code and waits for a tester to approve it.</summary>
        public static async Task<PrototirPairingResult> BeginPairingAsync(CancellationToken ct)
        {
            if (!EnsureConfigured())
                return Fail("This build has no Prototir settings, so it cannot be paired.");

            lock (Gate)
            {
                if (PairingState is PrototirPairingState.Requesting or PrototirPairingState.AwaitingApproval)
                    return Fail("This build is already waiting to be paired.");
                _state = PrototirPairingState.Requesting;
            }

            _pairingCancel?.Dispose();
            _pairingCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var cancel = _pairingCancel.Token;

            var request = await Flow().StartAsync(Settings.DeviceLabel, ReadBuildId(), cancel)
                .ConfigureAwait(false);
            if (request == null)
                return Fail("Prototir would not start a pairing for this prototype. Check the slug " +
                            "and that feedback is turned on for it.");

            _state = PrototirPairingState.AwaitingApproval;
            PairingStarted?.Invoke(request);

            var result = await Flow()
                .AwaitApprovalAsync(request.Code, request.ExpiresIn, request.PollInterval, cancel)
                .ConfigureAwait(false);

            if (result.Outcome == PrototirPairingOutcome.Approved)
            {
                Tokens.Write(Settings.PrototypeSlug, result.Token);
                _state = PrototirPairingState.Paired;
                PairingSucceeded?.Invoke();
                return result;
            }

            _state = result.Outcome == PrototirPairingOutcome.Cancelled
                ? PrototirPairingState.NotPaired
                : PrototirPairingState.Failed;
            if (result.Outcome != PrototirPairingOutcome.Cancelled)
                PairingFailed?.Invoke(result.Message ?? "Pairing did not finish.");
            return result;
        }

        public static void CancelPairing()
        {
            _pairingCancel?.Cancel();
            _state = IsPaired ? PrototirPairingState.Paired : PrototirPairingState.NotPaired;
        }

        public static void Unpair()
        {
            var slug = Settings?.PrototypeSlug;
            if (!string.IsNullOrEmpty(slug)) Tokens.Clear(slug);
            _state = PrototirPairingState.NotPaired;
        }

        /// <summary>Posts feedback as the tester who approved this build.
        ///
        /// <para>No session is needed first. Commenting is normally gated on having played, and
        /// that gate is waived for a paired device on purpose: approving the pairing is the
        /// stronger signal, since the tester signed in and authorised this exact build for this
        /// exact prototype.</para></summary>
        public static async Task<bool> SendFeedbackAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!EnsureConfigured()) return false;

            var token = Token;
            if (string.IsNullOrEmpty(token))
            {
                PairingFailed?.Invoke("Pair this build before sending feedback.");
                return false;
            }

            var body = new PrototirUnityJson().Encode(new FeedbackBody { text = text.Trim() });
            try
            {
                var response = await new PrototirUnityHttp().PostJsonAsync(
                    $"{Settings.ApiBaseUrl.TrimEnd('/')}/prototypes/{Uri.EscapeDataString(Settings.PrototypeSlug)}/comments",
                    body, token, ct).ConfigureAwait(false);

                if (PrototirPairingFlow.IsTokenTerminal(response.Status))
                {
                    Unpair();
                    PairingFailed?.Invoke(RevokedMessage);
                    return false;
                }
                return response.Status is 200 or 201;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Prototir: could not send feedback ({error.Message}).");
                return false;
            }
        }

        private static PrototirPairingFlow Flow() => _flow ??= new PrototirPairingFlow(
            new PrototirUnityHttp(), new PrototirUnityJson(), new PrototirUnityDelay(),
            () => DateTimeOffset.UtcNow, Settings.ApiBaseUrl, Settings.PrototypeSlug);

        private static bool EnsureConfigured()
        {
            if (Settings != null && Settings.IsConfigured) return true;
            // Once, not every frame: a game calling Event() in Update would otherwise fill the
            // log with the same line and bury everything else.
            if (!_configurationWarned)
            {
                _configurationWarned = true;
                Debug.LogWarning(
                    "Prototir: no prototype slug is set, so this build cannot pair or report " +
                    "anything. Create Resources/PrototirSettings via Prototir > Create Settings.");
            }
            return false;
        }

        /// <summary>The id the export plugin wrote beside the executable. Missing is normal for a
        /// build made before the plugin emitted one, and pairs without it.</summary>
        private static string ReadBuildId()
        {
            try
            {
                var path = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty, "prototir-build.json");
                if (!File.Exists(path)) return null;
                return new PrototirUnityJson().Decode<BuildSidecar>(File.ReadAllText(path)).buildId;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static PrototirPairingResult Fail(string message)
        {
            _state = PrototirPairingState.Failed;
            PairingFailed?.Invoke(message);
            return new PrototirPairingResult
            {
                Outcome = PrototirPairingOutcome.Failed,
                Message = message,
            };
        }

        [Serializable]
        private sealed class FeedbackBody
        {
            public string text;
        }

        [Serializable]
        private sealed class SessionResponse
        {
            public string sessionId;
            public bool real;
        }

        [Serializable]
        private sealed class BuildSidecar
        {
            public string buildId;
        }
    }
}
