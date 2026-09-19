using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace Prototir
{
    /// <summary>Provider-neutral options for a managed Prototir AI request.</summary>
    [Serializable]
    public sealed class PrototirAiOptions
    {
        public string Prompt = string.Empty;
        public int MaxTokens;
        public int TimeoutMilliseconds = 30000;
    }

    /// <summary>A machine-readable failure returned by the Prototir shell.</summary>
    public sealed class PrototirException : Exception
    {
        public string Code { get; }

        public PrototirException(string code, string message) : base(message)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "error" : code;
        }
    }

    /// <summary>
    /// Unity-shaped adapter for Prototir protocol v1. In a Unity Web player it delegates to the
    /// bundled .jslib bridge. In the Editor and on other platforms it uses deterministic local
    /// mocks, so a scene remains playable without browser symbols or DllNotFoundException.
    /// </summary>
    public static class PrototirSdk
    {
        public const int ProtocolVersion = 1;
        public const int MaxEventNameLength = 64;
        public const int MaxStorageValueBytes = 64 * 1024;

        private static readonly Regex EventNamePattern = new Regex(
            "^[a-z0-9_.:-]+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsWebPlayer => Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>Editor/non-Web signals, useful for tests and local scene diagnostics.</summary>
        public static event Action<string, string> MockEventSent;
        public static event Action<double> MockScoreSent;
        public static event Action MockReadySent;

        /// <summary>Optional Editor/non-Web managed-AI implementation.</summary>
        public static Func<PrototirAiOptions, Task<string>> MockAiHandler { get; set; }

        /// <summary>Signal that the scene is interactive. Call once after the first usable frame.</summary>
        public static void Ready()
        {
            _ = PrototirBridge.Instance;
#if UNITY_WEBGL && !UNITY_EDITOR
            Prototir_Ready();
#else
            MockReadySent?.Invoke();
#if !UNITY_EDITOR
            Native.PrototirNativeRuntime.Ready();
#endif
#endif
        }

        /// <summary>Record a stable analytics event. Names are normalized to lowercase.</summary>
        public static void Event(string name, string jsonData = null)
        {
            var normalized = NormalizeEventName(name);
            _ = PrototirBridge.Instance;
#if UNITY_WEBGL && !UNITY_EDITOR
            Prototir_Event(normalized, jsonData ?? string.Empty);
#else
            MockEventSent?.Invoke(normalized, jsonData);
#if !UNITY_EDITOR
            Native.PrototirNativeRuntime.Event(normalized);
#endif
#endif
        }

        /// <summary>Serialize a small Unity-serializable payload and record an event.</summary>
        public static void Event<T>(string name, T data)
        {
            Event(name, data is null ? null : JsonUtility.ToJson(data));
        }

        /// <summary>Report the current finite numeric score.</summary>
        public static void Score(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Score must be finite.");
            _ = PrototirBridge.Instance;
#if UNITY_WEBGL && !UNITY_EDITOR
            Prototir_Score(value);
#else
            MockScoreSent?.Invoke(value);
#if !UNITY_EDITOR
            Native.PrototirNativeRuntime.Score(value);
#endif
#endif
        }

        /// <summary>Whether this build may act for a person. Always false in a Web export,
        /// where the page already carries the visitor's session and nothing needs pairing.</summary>
        public static bool IsPaired =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            Native.PrototirNativeRuntime.IsPaired;
#endif

        /// <summary>Asks Prototir for a pairing code and waits for a tester to approve it on
        /// prototir.com. Subscribe to <see cref="PairingStarted"/> to show the code, the link and
        /// the QR: the SDK draws nothing, because it cannot know your art direction, your input
        /// model, or whether you are in VR.</summary>
        public static Task<Native.PrototirPairingResult> BeginPairingAsync(
            CancellationToken cancellationToken = default) =>
#if UNITY_WEBGL && !UNITY_EDITOR
            Task.FromResult(new Native.PrototirPairingResult
            {
                Outcome = Native.PrototirPairingOutcome.Failed,
                Message = "A Web export does not pair; the page already has the visitor's session.",
            });
#else
            Native.PrototirNativeRuntime.BeginPairingAsync(cancellationToken);
#endif

        /// <summary>Points a downloadable build at a prototype from code, instead of the settings
        /// asset. The Godot addon has had <c>Prototir.configure</c> from the start; this is the
        /// same thing, and without it a Unity build could only learn its slug by being rebuilt.
        ///
        /// <para>Does nothing in a Web build, which takes its prototype from the page.</para></summary>
        public static void Configure(string slug, string apiBaseUrl = null, string deviceLabel = null)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            Native.PrototirNativeRuntime.Configure(
                PrototirSettings.Create(slug, apiBaseUrl, deviceLabel));
#endif
        }

        /// <summary>Where this build stands. <see cref="IsPaired"/> answers the common question;
        /// this one separates "not paired" from "waiting for someone to approve a code", which is
        /// the difference between drawing a button and drawing a spinner. Readable after a scene
        /// reload, when the events have already been and gone.</summary>
        public static Native.PrototirPairingState PairingState =>
#if UNITY_WEBGL && !UNITY_EDITOR
            Native.PrototirPairingState.NotPaired;
#else
            Native.PrototirNativeRuntime.PairingState;
#endif

        public static event Action<Native.PrototirPairingRequest> PairingStarted
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            add { } remove { }
#else
            add => Native.PrototirNativeRuntime.PairingStarted += value;
            remove => Native.PrototirNativeRuntime.PairingStarted -= value;
#endif
        }

        public static event Action PairingSucceeded
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            add { } remove { }
#else
            add => Native.PrototirNativeRuntime.PairingSucceeded += value;
            remove => Native.PrototirNativeRuntime.PairingSucceeded -= value;
#endif
        }

        /// <summary>A human-readable reason, meant to be shown. Also raised when a paired build
        /// is refused later, which means the tester revoked it and it must pair again.</summary>
        public static event Action<string> PairingFailed
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            add { } remove { }
#else
            add => Native.PrototirNativeRuntime.PairingFailed += value;
            remove => Native.PrototirNativeRuntime.PairingFailed -= value;
#endif
        }

        public static void CancelPairing()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            Native.PrototirNativeRuntime.CancelPairing();
#endif
        }

        /// <summary>Forgets the stored token, so the build pairs again next time.</summary>
        public static void Unpair()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            Native.PrototirNativeRuntime.Unpair();
#endif
        }

        /// <summary>Posts feedback as the tester who approved this build. No session is needed
        /// first: approving the pairing is the stronger signal, so the usual played-it gate is
        /// waived for a paired device.</summary>
        public static Task<bool> SendFeedbackAsync(
            string text, CancellationToken cancellationToken = default) =>
#if UNITY_WEBGL && !UNITY_EDITOR
            Task.FromResult(false);
#else
            Native.PrototirNativeRuntime.SendFeedbackAsync(text, cancellationToken);
#endif

        /// <summary>Reports the session so far. Called automatically when the application quits;
        /// call it yourself at a natural break, such as the end of a run.</summary>
        public static Task FlushSessionAsync(CancellationToken cancellationToken = default) =>
#if UNITY_WEBGL && !UNITY_EDITOR
            Task.CompletedTask;
#else
            Native.PrototirNativeRuntime.FlushAsync(cancellationToken);
#endif

        public static Task<string> StorageGetAsync(
            string key,
            int timeoutMilliseconds = 3000,
            CancellationToken cancellationToken = default)
        {
            return PrototirBridge.Instance.StorageAsync("get", ValidateStorageKey(key), null, timeoutMilliseconds, cancellationToken);
        }

        public static async Task StorageSetAsync(
            string key,
            string value,
            int timeoutMilliseconds = 3000,
            CancellationToken cancellationToken = default)
        {
            value ??= string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(value) > MaxStorageValueBytes)
                throw new ArgumentException("Storage values cannot exceed 64 KiB of UTF-8 data.", nameof(value));
            await PrototirBridge.Instance.StorageAsync("set", ValidateStorageKey(key), value, timeoutMilliseconds, cancellationToken);
        }

        public static async Task StorageRemoveAsync(
            string key,
            int timeoutMilliseconds = 3000,
            CancellationToken cancellationToken = default)
        {
            await PrototirBridge.Instance.StorageAsync("remove", ValidateStorageKey(key), null, timeoutMilliseconds, cancellationToken);
        }

        public static Task<string> AiGenerateAsync(
            PrototirAiOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Prompt))
                throw new ArgumentException("AI prompt is required.", nameof(options));
            if (options.MaxTokens < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxTokens cannot be negative.");
            if (options.TimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "TimeoutMilliseconds must be greater than zero.");
            return PrototirBridge.Instance.AiAsync(options, cancellationToken);
        }

        internal static string NormalizeEventName(string name)
        {
            var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0 || normalized.Length > MaxEventNameLength || !EventNamePattern.IsMatch(normalized))
                throw new ArgumentException(
                    "Event names must be 1-64 characters using lowercase letters, numbers, underscore, dot, colon, or hyphen.",
                    nameof(name));
            return normalized;
        }

        private static string ValidateStorageKey(string key)
        {
            var normalized = (key ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > 128)
                throw new ArgumentException("Storage keys must be between 1 and 128 characters.", nameof(key));
            return normalized;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void Prototir_Ready();
        [DllImport("__Internal")] private static extern void Prototir_Event(string name, string jsonData);
        [DllImport("__Internal")] private static extern void Prototir_Score(double value);
#endif
    }

    [Preserve]
    internal sealed class PrototirBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "__PrototirBridge";
        private static PrototirBridge instance;
        private static int nextRequestId = 1;
        private readonly Dictionary<int, TaskCompletionSource<string>> storageRequests = new();
        private readonly Dictionary<int, TaskCompletionSource<string>> aiRequests = new();
        private readonly Dictionary<string, string> mockStorage = new(StringComparer.Ordinal);

        internal static PrototirBridge Instance
        {
            get
            {
                if (instance != null) return instance;
                var existing = GameObject.Find(BridgeObjectName);
                if (existing != null) instance = existing.GetComponent<PrototirBridge>();
                if (instance == null)
                {
                    var gameObject = new GameObject(BridgeObjectName);
                    instance = gameObject.AddComponent<PrototirBridge>();
                    DontDestroyOnLoad(gameObject);
                }
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => _ = Instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            gameObject.name = BridgeObjectName;
            DontDestroyOnLoad(gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
            Prototir_Install();
#endif
        }

        internal async Task<string> StorageAsync(
            string operation,
            string key,
            string value,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var id = NextRequestId();
            var completion = NewCompletion(storageRequests, id);
            Prototir_Storage(operation, key, value ?? string.Empty, id);
            return await Await(completion, storageRequests, id, timeoutMilliseconds, cancellationToken);
#else
            await Task.Yield();
            if (operation == "get") return mockStorage.TryGetValue(key, out var stored) ? stored : null;
            if (operation == "set") mockStorage[key] = value ?? string.Empty;
            else mockStorage.Remove(key);
            return null;
#endif
        }

        internal async Task<string> AiAsync(PrototirAiOptions options, CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var id = NextRequestId();
            var completion = NewCompletion(aiRequests, id);
            Prototir_Ai(options.Prompt, options.MaxTokens, id);
            return await Await(completion, aiRequests, id, options.TimeoutMilliseconds, cancellationToken);
#else
            if (PrototirSdk.MockAiHandler == null)
                throw new PrototirException("mock_unavailable", "Configure PrototirSdk.MockAiHandler when testing managed AI outside a Web player.");
            return await PrototirSdk.MockAiHandler(options);
#endif
        }

        // Called by Plugins/WebGL/Prototir.jslib via SendMessage.
        [Preserve]
        public void OnPrototirStorageResult(string json)
        {
            try
            {
                var response = JsonUtility.FromJson<StorageResponse>(json);
                if (response != null && storageRequests.Remove(response.id, out var completion))
                    completion.TrySetResult(response.value);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Ignored a malformed Prototir storage response: {exception.Message}");
            }
        }

        // Called by Plugins/WebGL/Prototir.jslib via SendMessage.
        [Preserve]
        public void OnPrototirAiResult(string json)
        {
            try
            {
                var response = JsonUtility.FromJson<AiResponse>(json);
                if (response == null || !aiRequests.Remove(response.id, out var completion)) return;
                if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
                    completion.TrySetException(new PrototirException(response.error.code, response.error.message));
                else if (!string.IsNullOrEmpty(response.text))
                    completion.TrySetResult(response.text);
                else
                    completion.TrySetException(new PrototirException("empty_response", "Prototir.ai returned an empty response."));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Ignored a malformed Prototir AI response: {exception.Message}");
            }
        }

        private static int NextRequestId()
        {
            if (nextRequestId == int.MaxValue) nextRequestId = 1;
            return nextRequestId++;
        }

        private static TaskCompletionSource<string> NewCompletion(
            IDictionary<int, TaskCompletionSource<string>> requests,
            int id)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests[id] = completion;
            return completion;
        }

        private static async Task<string> Await(
            TaskCompletionSource<string> completion,
            IDictionary<int, TaskCompletionSource<string>> requests,
            int id,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            var timeout = Math.Max(1, timeoutMilliseconds);
            var delay = Task.Delay(timeout, cancellationToken);
            var finished = await Task.WhenAny(completion.Task, delay);
            if (finished == completion.Task) return await completion.Task;
            requests.Remove(id);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The Prototir shell did not answer before the request timed out.");
        }

        [Serializable] private sealed class StorageResponse { public int id = 0; public string value = null; }
        [Serializable] private sealed class AiResponse { public int id = 0; public string text = null; public ErrorResponse error = null; }
        [Serializable] private sealed class ErrorResponse { public string code = null; public string message = null; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void Prototir_Install();
        [DllImport("__Internal")] private static extern void Prototir_Storage(string operation, string key, string value, int id);
        [DllImport("__Internal")] private static extern void Prototir_Ai(string prompt, int maxTokens, int id);
#endif
    }
}
