#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Prototir.Native
{
    /// <summary>The native comment composer. Closing keeps the draft for this run.
    ///
    /// <para>A screenshot or a console log can be attached (§16.7), but a message is always
    /// required: they are what the comment is about, never sent alone.</para></summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PrototirFeedbackScreen : MonoBehaviour
    {
        private const float CardWidth = 470, Inner = 422;
        private static PrototirFeedbackScreen _instance;
        private readonly ConcurrentQueue<Action> _updates = new ConcurrentQueue<Action>();
        private string _text = "", _scope = "", _sentKey = "", _clientId = "", _status = "";
        private string _attachment, _attachmentKind, _shotUrl;
        private Texture2D _shot;
        private Vector2 _pin = new Vector2(0.5f, 0.5f);
        private bool _visible, _posting;
        private PrototirPairingScreen _pairing;
        private Texture2D _pixel, _field, _accent, _closeIcon, _dot;
        private GUIStyle _heading, _body, _label, _input, _button, _closeButton, _link;
        public event Action Closed;

        /// <summary>Whether the composer is on screen, so Feedback &amp; tools can step aside.</summary>
        internal static bool IsOpen => _instance != null && _instance._visible;

        /// <summary>Opens the composer. <paramref name="attachment"/> is a console log or performance
        /// summary to send with the message; <paramref name="attachmentKind"/> names it for the tester.</summary>
        public static PrototirFeedbackScreen Show(string initialText = "", string attachment = null, string attachmentKind = null)
        {
            var screen = Open();
            if (screen._text.Length == 0) screen._text = (initialText ?? "").Substring(0, Math.Min(2000, (initialText ?? "").Length));
            if (!string.IsNullOrWhiteSpace(attachment) && !screen._posting)
            {
                screen._attachment = attachment;
                screen._attachmentKind = string.IsNullOrEmpty(attachmentKind) ? "log" : attachmentKind;
            }
            return screen;
        }

        /// <summary>Opens the composer with a captured frame, which the tester pins and describes.
        /// Takes ownership of <paramref name="shot"/>.</summary>
        internal static void ShowScreenshot(Texture2D shot, string dataUrl)
        {
            var screen = Open();
            if (screen._posting) { Destroy(shot); return; }
            screen.DropShot();
            screen._shot = shot;
            screen._shotUrl = dataUrl;
            screen._pin = new Vector2(0.5f, 0.5f);
            screen._status = "";
        }

        private static PrototirFeedbackScreen Open()
        {
            if (_instance == null)
            {
                var host = new GameObject(nameof(PrototirFeedbackScreen)) { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<PrototirFeedbackScreen>();
            }
            var scope = PrototirNativeRuntime.PrototypeSlug;
            if (_instance._scope != scope && !_instance._posting)
            {
                _instance._text = "";
                _instance._sentKey = "";
                _instance._clientId = "";
                _instance._scope = scope;
                _instance._status = "";
                _instance._attachment = null;
                _instance.DropShot();
            }
            _instance._visible = true;
            return _instance;
        }

        private void DropShot()
        {
            if (_shot != null) Destroy(_shot);
            _shot = null;
            _shotUrl = null;
        }

        private void Update() { while (_updates.TryDequeue(out var action)) action(); }
        private void Close() { if (_posting) return; _visible = false; Closed?.Invoke(); }
        private void Pair()
        {
            _pairing = PrototirPairingScreen.Show();
            _pairing.Closed += PairingClosed;
        }
        private void PairingClosed()
        {
            if (_pairing != null) _pairing.Closed -= PairingClosed;
            _pairing = null;
            _status = PrototirSdk.IsPaired ? "Connected. Review your comment, then post." : "Your draft is kept. Sign in when you are ready.";
        }
        private async void Post()
        {
            if (_posting || string.IsNullOrWhiteSpace(_text)) return;
            if (_scope != PrototirNativeRuntime.PrototypeSlug) { _status = "This build changed prototype. Close and reopen feedback before posting."; return; }
            if (!PrototirSdk.IsPaired) { Pair(); return; }
            var text = _text.Trim();
            var console = _attachment;
            var screenshot = _shotUrl == null ? null : new PrototirScreenshot(_shotUrl, _pin.x, _pin.y);
            // A retry of the same comment reuses its id, so the server keeps one copy.
            var key = text + "\n" + (console?.Length ?? 0) + "\n" + (_shotUrl?.Length ?? 0) + "\n" + _pin;
            if (_sentKey != key || _clientId.Length == 0) { _sentKey = key; _clientId = Guid.NewGuid().ToString(); }
            _posting = true;
            _status = "Posting...";
            bool saved;
            try { saved = await PrototirNativeRuntime.SendFeedbackAsync(text, CancellationToken.None, _clientId, console, screenshot); }
            catch { saved = false; }
            _updates.Enqueue(() =>
            {
                _posting = false;
                if (saved)
                {
                    _text = ""; _sentKey = ""; _clientId = ""; _attachment = null; DropShot();
                    _status = "Comment posted.";
                }
                else _status = PrototirSdk.IsPaired ? "Not posted. Check your connection or edit the comment, then try again. Your draft is kept." : "Sign in again to post. Your draft is kept.";
            });
        }

        private void OnGUI()
        {
            if (!_visible || _pairing != null) return;
            Styles();
            var matrix = GUI.matrix;
            var depth = GUI.depth;
            var enabled = GUI.enabled;
            GUI.depth = -999;

            var shotHeight = 0f;
            if (_shot != null) shotHeight = Mathf.Min(200, Inner * _shot.height / Mathf.Max(1f, _shot.width));
            var cardHeight = 384 + (_shot != null ? shotHeight + 30 : 0) + (_attachment != null ? 36 : 0);
            var scale = Mathf.Min(Screen.width / (CardWidth + 50), Screen.height / (cardHeight + 56), 1.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale; var height = Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(0.06f, 0.06f, 0.07f, 0.82f));
            var card = new Rect((width - CardWidth) / 2, (height - cardHeight) / 2, CardWidth, cardHeight);
            Fill(card, new Color(0.094f, 0.094f, 0.106f));
            var x = card.x + 24;
            GUI.Label(new Rect(x, card.y + 24, 360, 32), _shot != null ? "Screenshot feedback" : "Comment", _heading);
            GUI.enabled = !_posting;
            var close = new Rect(card.xMax - 48, card.y + 12, 36, 36);
            if (GUI.Button(close, new GUIContent("", "Close feedback"), _closeButton)) Close();
            // Rasterised Lucide X paths avoid font metrics and scaled GUI rotation pivots.
            GUI.DrawTexture(new Rect(close.center.x - 10, close.center.y - 10, 20, 20), _closeIcon);
            var y = card.y + 70;

            if (_shot != null)
            {
                var shotWidth = shotHeight * _shot.width / Mathf.Max(1f, _shot.height);
                var frame = new Rect(x + (Inner - shotWidth) / 2, y, shotWidth, shotHeight);
                GUI.DrawTexture(frame, _shot, ScaleMode.StretchToFill);
                var e = Event.current;
                if (!_posting && e.type == EventType.MouseDown && frame.Contains(e.mousePosition))
                {
                    _pin = new Vector2((e.mousePosition.x - frame.x) / frame.width, (e.mousePosition.y - frame.y) / frame.height);
                    e.Use();
                }
                var pin = new Vector2(frame.x + _pin.x * frame.width, frame.y + _pin.y * frame.height);
                Tinted(new Rect(pin.x - 9, pin.y - 9, 18, 18), _dot, new Color(0.06f, 0.09f, 0.16f));
                Tinted(new Rect(pin.x - 7, pin.y - 7, 14, 14), _dot, new Color(0.376f, 0.647f, 0.98f));
                y += shotHeight + 4;
                GUI.Label(new Rect(x, y, Inner - 160, 22), "Click the screenshot to place the pin.", _body);
                if (GUI.Button(new Rect(x + Inner - 160, y, 160, 22), "Remove screenshot", _link)) DropShot();
                y += 26;
            }
            if (_attachment != null)
            {
                var lines = _attachment.Split('\n').Length;
                Fill(new Rect(x, y, Inner, 30), new Color(0.125f, 0.125f, 0.14f));
                GUI.Label(new Rect(x + 10, y, Inner - 120, 30), $"{Capitalized(_attachmentKind)} attached · {lines} {(lines == 1 ? "line" : "lines")}", _label);
                if (GUI.Button(new Rect(x + Inner - 100, y + 4, 92, 22), "Remove", _link)) _attachment = null;
                y += 36;
            }

            GUI.Label(new Rect(x, y, Inner, 24), "Your message (required)", _label);
            _text = GUI.TextArea(new Rect(x, y + 26, Inner, 116), _text, 2000, _input);
            y += 150;
            GUI.Label(new Rect(x, y, Inner, 48), "Comments are visible to everyone who can access this prototype. Sign-in happens in your browser.", _body);
            y += 54;
            GUI.enabled = !_posting && !string.IsNullOrWhiteSpace(_text);
            if (GUI.Button(new Rect(x, y, 180, 42), _posting ? "Posting..." : PrototirSdk.IsPaired ? "Post comment" : "Sign in to post", _button)) Post();
            GUI.enabled = !_posting;
            GUI.Label(new Rect(x, y + 50, Inner, 40), _status, _body);
            GUI.enabled = enabled; GUI.matrix = matrix; GUI.depth = depth;
        }

        private static string Capitalized(string text) =>
            string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private void Styles()
        {
            if (_pixel != null) return;
            _pixel = PrototirIcons.Solid(Color.white); _field = PrototirIcons.Solid(new Color(0.125f, 0.125f, 0.14f)); _accent = PrototirIcons.Solid(new Color(0.376f, 0.647f, 0.98f));
            _heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(0.957f, 0.957f, 0.961f);
            _body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true }; _body.normal.textColor = new Color(0.631f, 0.631f, 0.667f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = _heading.normal.textColor;
            _link = new GUIStyle(_label) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            _link.normal.textColor = new Color(0.376f, 0.647f, 0.98f); _link.hover.textColor = _heading.normal.textColor;
            _input = new GUIStyle(GUI.skin.textArea) { fontSize = 16, wordWrap = true, padding = new RectOffset(12, 12, 10, 10) };
            _input.normal.background = _field; _input.normal.textColor = _heading.normal.textColor;
            _input.focused.background = _field; _input.focused.textColor = _heading.normal.textColor;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            _button.normal.background = _accent; _button.normal.textColor = new Color(0.06f, 0.09f, 0.16f);
            _button.hover = _button.normal; _button.active = _button.normal;
            _closeButton = new GUIStyle(_button); _closeButton.normal.background = _field;
            _closeButton.hover = _closeButton.normal; _closeButton.active = _closeButton.normal;
            _closeIcon = PrototirIcons.Lines(new[] { (6f, 6f, 18f, 18f), (6f, 18f, 18f, 6f) });
            _dot = PrototirIcons.Dot();
        }
        private void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, _pixel); GUI.color = old; }
        private static void Tinted(Rect rect, Texture2D texture, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = old; }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_pairing != null) _pairing.Closed -= PairingClosed;
            DropShot();
            foreach (var texture in new[] { _pixel, _field, _accent, _closeIcon, _dot }) if (texture != null) Destroy(texture);
        }
    }
}
#endif
