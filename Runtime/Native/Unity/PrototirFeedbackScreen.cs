#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Prototir.Native
{
    /// <summary>Opt-in desktop text feedback. Closing keeps the draft for this run.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PrototirFeedbackScreen : MonoBehaviour
    {
        private static PrototirFeedbackScreen _instance;
        private readonly ConcurrentQueue<Action> _updates = new ConcurrentQueue<Action>();
        private string _text = "", _scope = "", _sentText = "", _clientId = "", _status = "";
        private bool _visible, _posting;
        private PrototirPairingScreen _pairing;
        private Texture2D _pixel, _field, _accent;
        private GUIStyle _heading, _body, _input, _button;
        public event Action Closed;

        public static PrototirFeedbackScreen Show(string initialText = "")
        {
            if (_instance == null)
            {
                var host = new GameObject(nameof(PrototirFeedbackScreen)) { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<PrototirFeedbackScreen>();
            }
            var scope = PrototirNativeRuntime.PrototypeSlug;
            if (_instance._scope != scope)
            {
                if (_instance._posting) return _instance;
                _instance._text = "";
                _instance._sentText = "";
                _instance._clientId = "";
                _instance._scope = scope;
                _instance._status = "";
            }
            if (_instance._text.Length == 0) _instance._text = (initialText ?? "").Substring(0, Math.Min(2000, (initialText ?? "").Length));
            _instance._visible = true;
            return _instance;
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
            if (_sentText != text || _clientId.Length == 0) { _sentText = text; _clientId = Guid.NewGuid().ToString(); }
            _posting = true;
            _status = "Posting...";
            bool saved;
            try { saved = await PrototirNativeRuntime.SendFeedbackAsync(text, CancellationToken.None, _clientId); }
            catch { saved = false; }
            _updates.Enqueue(() =>
            {
                _posting = false;
                if (saved) { _text = ""; _sentText = ""; _clientId = ""; _status = "Comment posted."; }
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
            var scale = Mathf.Min(Screen.width / 520f, Screen.height / 470f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale; var height = Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(0.06f, 0.06f, 0.07f, 0.82f));
            var card = new Rect((width - 470) / 2, (height - 414) / 2, 470, 414);
            Fill(card, new Color(0.094f, 0.094f, 0.106f));
            GUI.Label(new Rect(card.x + 24, card.y + 24, 360, 32), "Give feedback", _heading);
            GUI.enabled = !_posting;
            var close = new Rect(card.xMax - 48, card.y + 12, 36, 36);
            if (GUI.Button(close, new GUIContent("", "Close feedback"))) Close();
            // Lucide X geometry, centred independently of font metrics.
            Line(close.center + new Vector2(-6, -6), close.center + new Vector2(6, 6));
            Line(close.center + new Vector2(-6, 6), close.center + new Vector2(6, -6));
            GUI.Label(new Rect(card.x + 24, card.y + 67, 422, 42), "What worked? What would you change?", _body);
            _text = GUI.TextArea(new Rect(card.x + 24, card.y + 110, 422, 136), _text, 2000, _input);
            GUI.Label(new Rect(card.x + 24, card.y + 257, 422, 48), "Comments are visible to everyone who can access this prototype. Sign-in happens in your browser.", _body);
            if (GUI.Button(new Rect(card.x + 24, card.y + 312, 180, 42), _posting ? "Posting..." : PrototirSdk.IsPaired ? "Post comment" : "Sign in to post", _button)) Post();
            GUI.Label(new Rect(card.x + 24, card.y + 366, 422, 40), _status, _body);
            GUI.enabled = enabled; GUI.matrix = matrix; GUI.depth = depth;
        }
        private void Styles()
        {
            if (_pixel != null) return;
            _pixel = Texture(Color.white); _field = Texture(new Color(0.125f, 0.125f, 0.14f)); _accent = Texture(new Color(0.376f, 0.647f, 0.98f));
            _heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            _heading.normal.textColor = new Color(0.957f, 0.957f, 0.961f);
            _body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true }; _body.normal.textColor = new Color(0.631f, 0.631f, 0.667f);
            _input = new GUIStyle(GUI.skin.textArea) { fontSize = 16, wordWrap = true, padding = new RectOffset(12, 12, 10, 10) };
            _input.normal.background = _field; _input.normal.textColor = _heading.normal.textColor;
            _input.focused.background = _field; _input.focused.textColor = _heading.normal.textColor;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            _button.normal.background = _accent; _button.normal.textColor = new Color(0.06f, 0.09f, 0.16f);
            _button.hover = _button.normal; _button.active = _button.normal;
        }
        private static Texture2D Texture(Color color) { var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, color); tex.Apply(); return tex; }
        private void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, _pixel); GUI.color = old; }
        private void Line(Vector2 a, Vector2 b)
        {
            var old = GUI.matrix; GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - 1, Vector2.Distance(a, b), 2), Color.white); GUI.matrix = old;
        }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_pairing != null) _pairing.Closed -= PairingClosed;
            if (_pixel != null) Destroy(_pixel); if (_field != null) Destroy(_field); if (_accent != null) Destroy(_accent);
        }
    }
}
#endif
