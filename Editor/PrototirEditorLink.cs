using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>This editor, linked to a creator's Prototir account so Publish can upload for
    /// them (§16.5.35).
    ///
    /// <para>The same device-code rendezvous as a paired build: the editor asks for a code, opens
    /// the approval page in the creator's browser, and polls until they approve. The token it gets
    /// back can only stage uploads; it cannot publish, comment or change anything, and the creator
    /// can unlink it from their account page.</para>
    ///
    /// <para>The async calls run off Unity's main thread and touch no Unity API; the caller stores
    /// the result with <see cref="Remember"/> on the main thread, because EditorPrefs refuses to be
    /// written from anywhere else.</para>
    ///
    /// <para>Kept in EditorPrefs, which is per machine rather than per project: one approval
    /// covers every project on this computer. Keyed by API address, so a local Prototir and the
    /// real one never share a token.</para></summary>
    internal static class PrototirEditorLink
    {
        internal sealed class Link
        {
            internal string Token;
            internal string DisplayName;
            /// <summary>The website to open for the handoff, taken from the approval URL the API
            /// returned, so a local or staging Prototir sends the creator to its own site.</summary>
            internal string AppOrigin;
        }

        internal sealed class Pending
        {
            internal string Code;
            internal string VerificationUrl;
            internal int IntervalSeconds;
            internal DateTime ExpiresAtUtc;
        }

        [Serializable] private sealed class StartBody { public string engine; public string editorLabel; }
        [Serializable] private sealed class StartReply { public string code; public string verificationUrl; public int expiresInSeconds; public int intervalSeconds; public string error; }
        [Serializable] private sealed class PollBody { public string code; }
        [Serializable] private sealed class PollReply { public string token; public string displayName; public string error; }
        [Serializable] private sealed class MeReply { public string displayName; }

        internal static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        internal static string ApiBase()
        {
            var settings = PrototirSettings.Load();
            return (settings != null ? settings.ApiBaseUrl : PrototirSettings.DefaultApiBaseUrl).TrimEnd('/');
        }

        private static string Key(string apiBase, string name)
        {
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(apiBase))).Replace("-", "").Substring(0, 12);
            return $"Prototir.EditorLink.{hash}.{name}";
        }

        internal static Link Stored(string apiBase)
        {
            var token = EditorPrefs.GetString(Key(apiBase, "Token"), string.Empty);
            if (string.IsNullOrEmpty(token)) return null;
            return new Link
            {
                Token = token,
                DisplayName = EditorPrefs.GetString(Key(apiBase, "Name"), string.Empty),
                AppOrigin = EditorPrefs.GetString(Key(apiBase, "Origin"), "https://prototir.com"),
            };
        }

        internal static void Remember(string apiBase, Link link)
        {
            EditorPrefs.SetString(Key(apiBase, "Token"), link.Token);
            EditorPrefs.SetString(Key(apiBase, "Name"), link.DisplayName ?? string.Empty);
            EditorPrefs.SetString(Key(apiBase, "Origin"), link.AppOrigin);
        }

        internal static void Forget(string apiBase)
        {
            EditorPrefs.DeleteKey(Key(apiBase, "Token"));
            EditorPrefs.DeleteKey(Key(apiBase, "Name"));
            EditorPrefs.DeleteKey(Key(apiBase, "Origin"));
        }

        /// <summary>Whether a stored token still works. Checked before building, so an editor
        /// unlinked from the account page finds out in a second rather than after a long build.
        /// Only a definite 401 forgets the token; a network error leaves it for the next try.</summary>
        internal static async Task<Link> VerifyAsync(string apiBase, Link link, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/editor/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", link.Token);
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Prototir answered {(int)response.StatusCode} when checking this editor's link.");
            var me = JsonUtility.FromJson<MeReply>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!string.IsNullOrEmpty(me?.displayName)) link.DisplayName = me.displayName;
            return link;
        }

        /// <summary>Shown on the approval page so the creator recognizes their own editor. Read on
        /// the main thread and passed in, like everything else from Unity's API.</summary>
        internal static string Label() => $"Unity {Application.unityVersion} on {Environment.MachineName}";

        internal static async Task<Pending> StartAsync(string apiBase, string editorLabel, CancellationToken ct)
        {
            var body = JsonUtility.ToJson(new StartBody { engine = "unity", editorLabel = editorLabel });
            using var response = await Http.PostAsync($"{apiBase}/editor/pair",
                new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            var reply = JsonUtility.FromJson<StartReply>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(reply?.code))
                throw new InvalidOperationException(reply?.error ?? $"Prototir could not start linking this editor ({(int)response.StatusCode}).");
            return new Pending
            {
                Code = reply.code,
                VerificationUrl = reply.verificationUrl,
                IntervalSeconds = Math.Max(2, reply.intervalSeconds),
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, reply.expiresInSeconds)),
            };
        }

        /// <summary>Polls until the creator approves in their browser. The caller stores the link.
        /// 202 means not yet; 410 means the code is dead and they have to start again.</summary>
        internal static async Task<Link> WaitForApprovalAsync(string apiBase, Pending pending, CancellationToken ct)
        {
            var body = JsonUtility.ToJson(new PollBody { code = pending.Code });
            while (DateTime.UtcNow < pending.ExpiresAtUtc)
            {
                await Task.Delay(TimeSpan.FromSeconds(pending.IntervalSeconds), ct).ConfigureAwait(false);
                HttpResponseMessage response;
                try
                {
                    response = await Http.PostAsync($"{apiBase}/editor/pair/poll",
                        new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // A dropped poll is not a refusal: the approval may already be waiting.
                    continue;
                }
                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Accepted) continue;
                    var reply = JsonUtility.FromJson<PollReply>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(reply?.token))
                        throw new InvalidOperationException(reply?.error ?? "This code is no longer valid. Publish again to get a new one.");
                    return new Link
                    {
                        Token = reply.token,
                        DisplayName = reply.displayName,
                        AppOrigin = new Uri(pending.VerificationUrl).GetLeftPart(UriPartial.Authority),
                    };
                }
            }
            throw new TimeoutException("Nobody approved the link in time. Publish again to get a new code.");
        }

        [MenuItem("Prototir/Unlink This Editor", priority = 12)]
        private static void Unlink()
        {
            var apiBase = ApiBase();
            if (Stored(apiBase) == null)
            {
                EditorUtility.DisplayDialog("Prototir", "This editor is not linked to a Prototir account.", "OK");
                return;
            }
            Forget(apiBase);
            EditorUtility.DisplayDialog("Prototir",
                "This editor is unlinked. It still appears under Linked editors on your Prototir " +
                "account page until you remove it there.", "OK");
        }
    }
}
