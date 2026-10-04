#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace Prototir.Native
{
    /// <summary>"Feedback &amp; tools" in a native build (§16.7): the same control as the web SDK's,
    /// in the bottom-left corner, unfolding Screenshot, Comment, Console and Performance inside
    /// its border.
    ///
    /// <para>Shown by default in a build Prototir knows (its prototype slug is set), so testers get
    /// it without the developer writing anything. <see cref="PrototirSdk.FeedbackTools"/> or the
    /// settings asset turn it off.</para>
    ///
    /// <para>Kept light: folded, it draws one row; the Console panel redraws from a buffer that
    /// was already recorded; the Performance sampler is fed only while its panel is open.</para></summary>
    [DefaultExecutionOrder(10001)]
    public sealed class PrototirToolsDock : MonoBehaviour
    {
        private static PrototirToolsDock _instance;
        private bool _open, _console, _performance, _capturing;
        private Vector2 _scroll;
        private int _seenConsole = -1;
        private string[] _lines = Array.Empty<string>();
        private PrototirLogLevel[] _levels = Array.Empty<PrototirLogLevel>();
        private readonly PrototirPerformanceSampler _sampler = new PrototirPerformanceSampler();
        private Texture2D _pixel, _chevronUp, _chevronDown, _close;
        private GUIStyle _row, _rowState, _head, _title, _small, _line, _button;

        // The website's dark palette, which the web SDK's panels also use over a game.
        private static readonly Color Surface = Hex(0x27272c), Background = Hex(0x18181b), Line = Hex(0x36363c),
            LineStrong = Hex(0x6a6a73), Ink = Hex(0xf5f4f1), Muted = Hex(0xbbb9b3), Accent = Hex(0x60a5fa),
            Warn = Hex(0xe0a54a), Error = Hex(0xf07171);

        /// <summary>Shows or hides the control at runtime.</summary>
        internal static void SetVisible(bool visible)
        {
            if (visible) Ensure();
            else if (_instance != null) Destroy(_instance.gameObject);
        }

        internal static bool Visible => _instance != null;

        internal static void Ensure()
        {
            if (_instance != null) return;
            var host = new GameObject(nameof(PrototirToolsDock)) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<PrototirToolsDock>();
        }

        private void Update()
        {
            if (_performance)
                _sampler.AddFrame(Time.unscaledDeltaTime, GC.GetTotalMemory(false) / 1048576f);
        }

        private static float Scale => Mathf.Clamp(Screen.height / 720f, 1f, 2f);

        private void OnGUI()
        {
            // Steps aside while the comment composer is open: it covers the game, and a click on a
            // tool underneath it would land on a control the tester cannot see.
            if (_capturing || PrototirFeedbackScreen.IsOpen) return;
            Styles();
            var matrix = GUI.matrix;
            var depth = GUI.depth;
            GUI.depth = -998;
            var scale = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            if (_performance) DrawPerformance(new Rect(width - 16 - 340, 16, 340, 190));
            if (_console)
            {
                var top = _performance ? 16 + 190 + 12 : 16;
                var panelHeight = Mathf.Min(420, height - top - 16);
                DrawConsole(new Rect(width - 16 - Mathf.Min(560, width - 32), height - 16 - panelHeight, Mathf.Min(560, width - 32), panelHeight));
            }
            DrawDock(height);
            GUI.matrix = matrix;
            GUI.depth = depth;
        }

        private void DrawDock(float screenHeight)
        {
            const float w = 220, rowH = 40, headH = 40, pad = 6;
            var rows = _open ? 4 : 0;
            var h = headH + (_open ? rows * rowH + pad * 2 + 1 : 0);
            var dock = new Rect(16, screenHeight - 16 - h, w, h);
            Box(dock, Surface, LineStrong);
            var y = dock.y;
            if (_open)
            {
                y += pad;
                if (Row(new Rect(dock.x + pad, y, w - pad * 2, rowH), "Screenshot", null)) { _open = false; StartCoroutine(Screenshot()); }
                y += rowH;
                if (Row(new Rect(dock.x + pad, y, w - pad * 2, rowH), "Comment", null)) { _open = false; PrototirFeedbackScreen.Show(); }
                y += rowH;
                if (Row(new Rect(dock.x + pad, y, w - pad * 2, rowH), "Console", _console)) _console = !_console;
                y += rowH;
                if (Row(new Rect(dock.x + pad, y, w - pad * 2, rowH), "Performance", _performance)) SetPerformance(!_performance);
                y += rowH + pad;
                Fill(new Rect(dock.x, y, w, 1), Line);
                y += 1;
            }
            var head = new Rect(dock.x, y, w, headH);
            if (GUI.Button(head, GUIContent.none, GUIStyle.none)) _open = !_open;
            GUI.Label(new Rect(head.x + 14, head.y, w - 50, headH), "Feedback & tools", _head);
            // Points the way the tools open (up), and down again to fold them.
            Tinted(new Rect(head.xMax - 30, head.y + 12, 16, 16), _open ? _chevronDown : _chevronUp, Muted);
        }

        private bool Row(Rect rect, string label, bool? state)
        {
            if (rect.Contains(Event.current.mousePosition)) Fill(rect, Hex(0x313137));
            var clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.Label(new Rect(rect.x + 10, rect.y, rect.width - 60, rect.height), label, _row);
            if (state.HasValue)
            {
                _rowState.normal.textColor = state.Value ? Accent : Muted;
                GUI.Label(new Rect(rect.xMax - 50, rect.y, 40, rect.height), state.Value ? "On" : "Off", _rowState);
            }
            return clicked;
        }

        private void SetPerformance(bool on)
        {
            _performance = on;
            if (on) _sampler.Reset();
        }

        private void DrawConsole(Rect panel)
        {
            Box(panel, Background, LineStrong);
            if (PanelHead(panel, "Console")) { _console = false; return; }
            var footer = new Rect(panel.x, panel.yMax - 44, panel.width, 44);
            var body = new Rect(panel.x, panel.y + 44, panel.width, panel.height - 88);
            // Rebuilt only when something was logged: OnGUI runs several times a frame. Each new
            // line scrolls to the bottom, where the newest entry is.
            var version = PrototirNativeRuntime.ConsoleLog.Version;
            if (version != _seenConsole)
            {
                _seenConsole = version;
                var recorded = PrototirNativeRuntime.ConsoleLog.Entries();
                _lines = new string[recorded.Count];
                _levels = new PrototirLogLevel[recorded.Count];
                for (var i = 0; i < recorded.Count; i++)
                {
                    var text = recorded[i].Text;
                    var end = text.IndexOf('\n');
                    _lines[i] = recorded[i].Time.ToLocalTime().ToString("HH:mm:ss") + " " + (end < 0 ? text : text.Substring(0, end));
                    _levels[i] = recorded[i].Level;
                }
                _scroll.y = float.MaxValue;
            }
            var content = new Rect(0, 0, body.width - 28, Mathf.Max(body.height, _lines.Length * 18 + 8));
            _scroll = GUI.BeginScrollView(new Rect(body.x + 8, body.y + 4, body.width - 12, body.height - 8), _scroll, content);
            if (_lines.Length == 0) { _line.normal.textColor = Muted; GUI.Label(new Rect(4, 4, content.width, 18), "Nothing logged yet.", _line); }
            // Only the rows in view are drawn; the rest of the 300 cost nothing.
            var firstRow = Mathf.Max(0, (int)((_scroll.y - 4) / 18));
            var lastRow = Mathf.Min(_lines.Length, firstRow + (int)(body.height / 18) + 2);
            for (var i = firstRow; i < lastRow; i++)
            {
                _line.normal.textColor = _levels[i] == PrototirLogLevel.Error ? Error : _levels[i] == PrototirLogLevel.Warning ? Warn : Ink;
                GUI.Label(new Rect(4, 4 + i * 18, content.width, 18), _lines[i], _line);
            }
            GUI.EndScrollView();
            Fill(new Rect(footer.x, footer.y, footer.width, 1), Line);
            var x = footer.xMax - 12;
            if (FooterButton(ref x, footer, "Attach to comment")) PrototirFeedbackScreen.Show("", PrototirNativeRuntime.ConsoleLog.Text(), "console log");
            if (FooterButton(ref x, footer, "Clear")) PrototirNativeRuntime.ConsoleLog.Clear();
            if (FooterButton(ref x, footer, "Copy")) GUIUtility.systemCopyBuffer = PrototirNativeRuntime.ConsoleLog.Text();
        }

        private void DrawPerformance(Rect panel)
        {
            Box(panel, Background, LineStrong);
            if (PanelHead(panel, "Performance")) { SetPerformance(false); return; }
            // A left gutter keeps the 30 and 60 fps marks off the line.
            var chart = new Rect(panel.x + 40, panel.y + 52, panel.width - 52, 70);
            var samples = _sampler.Samples;
            var top = 80f;
            foreach (var sample in samples) top = Mathf.Max(top, sample.Fps);
            top *= 1.1f;
            foreach (var mark in new[] { 30f, 60f })
            {
                var yMark = chart.yMax - mark / top * chart.height;
                Fill(new Rect(chart.x, yMark, chart.width, 1), Line);
                _small.normal.textColor = Muted;
                GUI.Label(new Rect(panel.x + 12, yMark - 9, 26, 18), mark.ToString("0"), _small);
            }
            // A line of points, one per quarter-second sample, spread over the width as the minute fills.
            var count = samples.Count;
            for (var i = 0; i < count; i++)
            {
                var barWidth = Mathf.Max(1, chart.width / Mathf.Max(count, 1));
                var barHeight = samples[i].Fps / top * chart.height;
                Fill(new Rect(chart.x + i * chart.width / Mathf.Max(count, 1), chart.yMax - barHeight, barWidth, 2), Accent);
            }
            var latest = count > 0 ? samples[count - 1] : default;
            _small.normal.textColor = Muted;
            GUI.Label(new Rect(panel.x + 12, chart.yMax + 4, panel.width - 24, 18), count == 0 ? "Recording…" :
                $"{latest.Fps:0} fps · slowest frame {latest.WorstFrameMs:0} ms · {latest.MemoryMb:0} MB", _small);
            var footer = new Rect(panel.x, panel.yMax - 44, panel.width, 44);
            Fill(new Rect(footer.x, footer.y, footer.width, 1), Line);
            var x = footer.xMax - 12;
            var summary = _sampler.Summary($"{Application.platform}, {SystemInfo.graphicsDeviceName}");
            if (FooterButton(ref x, footer, "Attach to comment")) PrototirFeedbackScreen.Show("", summary, "performance summary");
            if (FooterButton(ref x, footer, "Copy")) GUIUtility.systemCopyBuffer = summary;
        }

        private bool PanelHead(Rect panel, string title)
        {
            GUI.Label(new Rect(panel.x + 14, panel.y, panel.width - 60, 44), title, _title);
            Fill(new Rect(panel.x, panel.y + 43, panel.width, 1), Line);
            var close = new Rect(panel.xMax - 40, panel.y + 7, 30, 30);
            var clicked = GUI.Button(close, new GUIContent("", "Close " + title), _button);
            Tinted(new Rect(close.center.x - 8, close.center.y - 8, 16, 16), _close, Ink);
            return clicked;
        }

        private bool FooterButton(ref float right, Rect footer, string label)
        {
            var width = _button.CalcSize(new GUIContent(label)).x + 20;
            right -= width;
            var clicked = GUI.Button(new Rect(right, footer.y + 8, width, 28), label, _button);
            right -= 6;
            return clicked;
        }

        /// <summary>Captures the frame without this control on it, then opens the comment with the
        /// screenshot attached, where the tester places the pin and writes the message.</summary>
        private IEnumerator Screenshot()
        {
            _capturing = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try { shot = ScreenCapture.CaptureScreenshotAsTexture(); }
            finally { _capturing = false; }
            if (shot == null) yield break;
            var scaled = Downscale(shot, 1280);
            if (scaled != shot) Destroy(shot);
            // Prototir takes screenshots up to 1 MiB; a busy frame at full quality can pass that.
            var jpeg = scaled.EncodeToJPG(82);
            for (var quality = 70; jpeg.Length > 900 * 1024 && quality >= 40; quality -= 15) jpeg = scaled.EncodeToJPG(quality);
            PrototirFeedbackScreen.ShowScreenshot(scaled, "data:image/jpeg;base64," + Convert.ToBase64String(jpeg));
        }

        private static Texture2D Downscale(Texture2D source, int maxSide)
        {
            var factor = Mathf.Min(1f, (float)maxSide / Mathf.Max(source.width, source.height));
            if (factor >= 1f) return source;
            var w = Mathf.RoundToInt(source.width * factor);
            var h = Mathf.RoundToInt(source.height * factor);
            var target = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(source, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var result = new Texture2D(w, h, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }

        private void Styles()
        {
            if (_pixel != null) return;
            _pixel = new Texture2D(1, 1); _pixel.SetPixel(0, 0, Color.white); _pixel.Apply();
            _chevronUp = PrototirIcons.Lines(new[] { (6f, 15f, 12f, 9f), (12f, 9f, 18f, 15f) });
            _chevronDown = PrototirIcons.Lines(new[] { (6f, 9f, 12f, 15f), (12f, 15f, 18f, 9f) });
            _close = PrototirIcons.Lines(new[] { (6f, 6f, 18f, 18f), (6f, 18f, 18f, 6f) });
            _row = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _row.normal.textColor = Ink;
            _rowState = new GUIStyle(_row) { fontSize = 12, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleRight };
            _head = new GUIStyle(_row); _head.normal.textColor = Muted;
            _title = new GUIStyle(_row) { fontSize = 15 };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            var mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" }, 12);
            if (mono != null) _line.font = mono;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            _button.normal.background = PrototirIcons.Solid(Surface); _button.normal.textColor = Ink;
            _button.hover.background = PrototirIcons.Solid(Hex(0x313137)); _button.hover.textColor = Ink;
            _button.active = _button.hover;
        }

        private void Box(Rect rect, Color fill, Color border)
        {
            Fill(rect, border);
            Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), fill);
        }

        private void Fill(Rect rect, Color color)
        {
            var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, _pixel); GUI.color = old;
        }

        private static void Tinted(Rect rect, Texture2D texture, Color color)
        {
            var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = old;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            foreach (var texture in new[] { _pixel, _chevronUp, _chevronDown, _close }) if (texture != null) Destroy(texture);
        }
    }

    /// <summary>Line icons rasterised from Lucide's coordinates (a 24-unit grid), so the native
    /// control needs no font glyphs or image assets.</summary>
    internal static class PrototirIcons
    {
        public static Texture2D Lines((float x1, float y1, float x2, float y2)[] segments)
        {
            var tex = new Texture2D(24, 24, TextureFormat.RGBA32, false);
            for (var y = 0; y < 24; y++) for (var x = 0; x < 24; x++)
            {
                // IMGUI draws textures bottom-up, Lucide coordinates are top-down.
                var point = new Vector2(x + 0.5f, 24 - (y + 0.5f));
                var distance = float.MaxValue;
                foreach (var s in segments)
                    distance = Mathf.Min(distance, Segment(point, new Vector2(s.x1, s.y1), new Vector2(s.x2, s.y2)));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1.5f - distance)));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>A filled circle, for the screenshot pin.</summary>
        public static Texture2D Dot()
        {
            var tex = new Texture2D(24, 24, TextureFormat.RGBA32, false);
            for (var y = 0; y < 24; y++) for (var x = 0; x < 24; x++)
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(12.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(12, 12)))));
            tex.Apply();
            return tex;
        }

        public static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, color); tex.Apply(); return tex;
        }

        private static float Segment(Vector2 point, Vector2 a, Vector2 b)
        {
            var delta = b - a;
            return Vector2.Distance(point, a + Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude) * delta);
        }
    }
}
#endif
