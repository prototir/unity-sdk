using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>Uploads a ZIP straight to Prototir's storage and returns the claim the upload page
    /// adopts (§16.5.35). The same handshake the website uses for large bundles: the API mints
    /// presigned part URLs after checking plan limits and slots, the parts go directly to storage,
    /// and completion re-checks the real size before a claim is issued.</summary>
    internal static class PrototirDirectUpload
    {
        [Serializable] private sealed class StartBody { public long sizeBytes; }
        [Serializable] private sealed class Part { public int partNumber; public string url; }
        [Serializable] private sealed class StartReply { public string uploadRef; public string uploadId; public long partSizeBytes; public Part[] parts; public string error; }
        [Serializable] private sealed class DonePart { public int partNumber; public string etag; }
        [Serializable] private sealed class CompleteBody { public string uploadRef; public string uploadId; public DonePart[] parts; }
        [Serializable] private sealed class CompleteReply { public string claim; public string error; }
        [Serializable] private sealed class RegisterBody { public string claim; public string fileName; public string engine; public string kind; public string platform; public string architecture; public string slug; }
        [Serializable] private sealed class RegisterReply { public string id; public string error; }

        private const int Attempts = 3;

        /// <summary>Uploads <paramref name="path"/> and returns the claim. <paramref name="sent"/>
        /// counts bytes whose part has landed, so progress never reports bytes a retry may undo.</summary>
        internal static async Task<string> UploadAsync(
            string apiBase, string token, string path, Action<long> sent, CancellationToken ct)
        {
            var size = new FileInfo(path).Length;
            var start = await Control<StartReply>(apiBase, token, "me/uploads/direct",
                JsonUtility.ToJson(new StartBody { sizeBytes = size }), ct).ConfigureAwait(false);

            try
            {
                var done = new DonePart[start.parts.Length];
                using (var file = File.OpenRead(path))
                {
                    // Sequential and one part in memory at a time: an editor uploading a build is
                    // not worth tuning for throughput at the cost of holding the whole ZIP in RAM.
                    for (var i = 0; i < start.parts.Length; i++)
                    {
                        var part = start.parts[i];
                        var offset = (part.partNumber - 1) * start.partSizeBytes;
                        var length = (int)Math.Min(start.partSizeBytes, size - offset);
                        var buffer = new byte[length];
                        file.Seek(offset, SeekOrigin.Begin);
                        var read = 0;
                        while (read < length)
                        {
                            var n = await file.ReadAsync(buffer, read, length - read, ct).ConfigureAwait(false);
                            if (n == 0) throw new IOException("The build changed while it was being uploaded.");
                            read += n;
                        }
                        done[i] = new DonePart { partNumber = part.partNumber, etag = await PutPart(part.url, buffer, ct).ConfigureAwait(false) };
                        sent(length);
                    }
                }

                var complete = await Control<CompleteReply>(apiBase, token, "me/uploads/direct/complete",
                    JsonUtility.ToJson(new CompleteBody { uploadRef = start.uploadRef, uploadId = start.uploadId, parts = done }), ct)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(complete.claim))
                    throw new InvalidOperationException("Prototir finished the upload but returned no claim.");
                return complete.claim;
            }
            catch
            {
                // Abandoned parts are billed until aborted, so every failure clears them. Best
                // effort: a failed cleanup must not hide the error the creator needs to see.
                try
                {
                    using var abort = new HttpRequestMessage(HttpMethod.Delete,
                        $"{apiBase}/me/uploads/direct/{start.uploadRef}?uploadId={Uri.EscapeDataString(start.uploadId)}");
                    abort.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var _ = await PrototirEditorLink.Http.SendAsync(abort, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception) { }
                throw;
            }
        }

        /// <summary>Tells Prototir the upload is waiting (§16.5.37), so Studio or the upload page
        /// lists it with its upload time until it is used, and a reload cannot lose it. A newer
        /// upload for the same prototype and platform replaces the older one.</summary>
        internal static Task RegisterAsync(
            string apiBase, string token, string claim, string fileName, string kind,
            string platform, string architecture, string slug, CancellationToken ct) =>
            Control<RegisterReply>(apiBase, token, "editor/uploads", JsonUtility.ToJson(new RegisterBody
            {
                claim = claim,
                fileName = fileName,
                engine = "unity",
                kind = kind,
                platform = platform,
                architecture = architecture,
                slug = slug,
            }), ct);

        private static async Task<T> Control<T>(string apiBase, string token, string route, string json, CancellationToken ct)
            where T : class
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/{route}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await PrototirEditorLink.Http.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("This editor is no longer linked to your Prototir account.");
            T reply = null;
            try { reply = JsonUtility.FromJson<T>(text); } catch (ArgumentException) { }
            if (!response.IsSuccessStatusCode)
            {
                var error = reply?.GetType().GetField("error")?.GetValue(reply) as string;
                throw new InvalidOperationException(error ?? $"Prototir refused the upload ({(int)response.StatusCode}).");
            }
            return reply ?? throw new InvalidOperationException("Prototir sent an unreadable answer.");
        }

        private static async Task<string> PutPart(string url, byte[] bytes, CancellationToken ct)
        {
            Exception last = null;
            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    using var content = new ByteArrayContent(bytes);
                    using var response = await PrototirEditorLink.Http.PutAsync(url, content, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"Storage rejected a part (HTTP {(int)response.StatusCode}).");
                    var etag = response.Headers.ETag?.Tag
                               ?? response.Headers.TryGetValues("ETag", out var values) switch { true => values.FirstOrDefault(), _ => null };
                    if (string.IsNullOrEmpty(etag)) throw new InvalidOperationException("Storage did not return an ETag for a part.");
                    return etag;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    last = error;
                    if (attempt < Attempts) await Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << attempt)), ct).ConfigureAwait(false);
                }
            }
            throw new InvalidOperationException(last?.Message ?? "A part of the upload failed.");
        }
    }
}
