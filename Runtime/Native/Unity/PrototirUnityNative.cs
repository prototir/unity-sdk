using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Prototir.Native
{
    /// <summary>The Unity side of the seams in this folder: real HTTP, real JSON, real disk.
    ///
    /// <para>Deliberately thin. Everything that decides anything lives in the engine-free types
    /// beside it, because Unity cannot be driven from CI and code behind
    /// <see cref="UnityWebRequest"/> is untestable by construction. If logic starts collecting
    /// here, it belongs on the other side of the seam instead.</para></summary>
    public sealed class PrototirUnityHttp : IPrototirHttp
    {
        private readonly int _timeoutSeconds;

        public PrototirUnityHttp(int timeoutSeconds = 20) => _timeoutSeconds = timeoutSeconds;

        public Task<PrototirHttpResponse> PostJsonAsync(
            string url, string json, string bearer, CancellationToken ct) =>
            PrototirNativeTicker.RunOnMainThread(() => SendOnMainThreadAsync(url, json, bearer, ct));

        private async Task<PrototirHttpResponse> SendOnMainThreadAsync(
            string url, string json, string bearer, CancellationToken ct)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json ?? "{}")),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(bearer))
                request.SetRequestHeader("Authorization", $"Bearer {bearer}");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    request.Abort();
                    throw new OperationCanceledException(ct);
                }
                await Task.Yield();
            }

            // Status 0 means the request never reached the server, which the protocol treats as
            // retryable. A status the server chose is never retryable in the same way, so the two
            // must not be collapsed here.
            var status = request.result == UnityWebRequest.Result.ConnectionError
                ? 0
                : (int)request.responseCode;
            return new PrototirHttpResponse(status, request.downloadHandler?.text ?? string.Empty);
        }
    }

    /// <summary>JsonUtility, which is why the payload types in this folder are plain serializable
    /// classes with public fields rather than records or properties.</summary>
    public sealed class PrototirUnityJson : IPrototirJson
    {
        public string Encode<T>(T value) => JsonUtility.ToJson(value);

        public T Decode<T>(string json) where T : new()
        {
            if (string.IsNullOrWhiteSpace(json)) return new T();
            try { return JsonUtility.FromJson<T>(json) ?? new T(); }
            catch (ArgumentException) { return new T(); }
        }
    }

    /// <summary>Keeps the pairing token under <see cref="Application.persistentDataPath"/>.
    ///
    /// <para>Never beside the executable: that directory is often read-only, is shared by everyone
    /// on the machine, and would put one person's credential in another person's reach. One file
    /// per prototype, so unpairing one build cannot disturb another.</para></summary>
    public sealed class PrototirUnityTokenStore : IPrototirTokenStore
    {
        private readonly string _directory;

        /// <summary>Create on Unity's main thread. The path is captured once so reads and writes
        /// after an async pairing continuation never call Unity's API from a worker.</summary>
        public PrototirUnityTokenStore() : this(
            Path.Combine(Application.persistentDataPath, "prototir", "pairings")) { }

        internal PrototirUnityTokenStore(string directory) =>
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));

        public string Read(string slug)
        {
            try
            {
                var path = PathFor(slug);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Write(string slug, string token)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.WriteAllText(PathFor(slug), token);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Clear(string slug)
        {
            try
            {
                var path = PathFor(slug);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>A slug reaches this from configuration, so it is kept to characters that
        /// cannot escape the directory rather than trusted to be well formed.</summary>
        private string PathFor(string slug)
        {
            var safe = new string(System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Where(slug ?? string.Empty,
                    c => char.IsLetterOrDigit(c) || c == '-' || c == '_')));
            if (safe.Length == 0) safe = "default";
            return Path.Combine(_directory, safe + ".token");
        }
    }

    public sealed class PrototirUnityDelay : IPrototirDelay
    {
        public Task WaitAsync(TimeSpan duration, CancellationToken ct) =>
            Task.Delay(duration, ct);
    }
}
