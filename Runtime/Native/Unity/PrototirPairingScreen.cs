#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Prototir.Native
{
    /// <summary>A ready-to-use pairing overlay. Games with their own UI can continue using the
    /// pairing events directly. Created only when Show is called, with no scene setup.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PrototirPairingScreen : MonoBehaviour
    {
        private enum View { Idle, Requesting, Approval, Connected, JustConnected, Failed }

        private static PrototirPairingScreen _instance;
        private static readonly Color Background = Hex("#0f0f11");
        private static readonly Color Surface = Hex("#18181b");
        private static readonly Color Raised = Hex("#202024");
        private static readonly Color Text = Hex("#f4f4f5");
        private static readonly Color Muted = Hex("#a1a1aa");
        private static readonly Color Accent = Hex("#60a5fa");
        private static readonly Color AccentText = Hex("#0f172a");

        private readonly ConcurrentQueue<Action> _updates = new ConcurrentQueue<Action>();
        private int _mainThreadId;
        private CancellationTokenSource _pairing;
        private View _view;
        private string _code;
        private string _url;
        private string _title;
        private string _failure;
        private string _note;
        private Texture2D _qr;
        private Texture2D _pixel;
        private Texture2D _buttonBackground;
        private Texture2D _buttonHover;
        private Texture2D _primaryBackground;
        private Texture2D _primaryHover;
        private GUIStyle _brandStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _noteStyle;
        private GUIStyle _codeStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _primaryStyle;

        /// <summary>Show the screen over the running game. Repeated calls reuse the open screen.</summary>
        public static PrototirPairingScreen Show()
        {
            if (_instance != null) return _instance;
            var host = new GameObject(nameof(PrototirPairingScreen))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _instance = host.AddComponent<PrototirPairingScreen>();
            return _instance;
        }

        public event Action Closed;

        private void OnEnable()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _view = PrototirSdk.IsPaired ? View.Connected : View.Idle;
            PrototirSdk.PairingStarted += Started;
            PrototirSdk.PairingSucceeded += Succeeded;
            PrototirSdk.PairingFailed += Failed;
        }

        private void OnDisable()
        {
            PrototirSdk.PairingStarted -= Started;
            PrototirSdk.PairingSucceeded -= Succeeded;
            PrototirSdk.PairingFailed -= Failed;
            CancelActive();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_qr != null) Destroy(_qr);
            if (_pixel != null) Destroy(_pixel);
            if (_buttonBackground != null) Destroy(_buttonBackground);
            if (_buttonHover != null) Destroy(_buttonHover);
            if (_primaryBackground != null) Destroy(_primaryBackground);
            if (_primaryHover != null) Destroy(_primaryHover);
        }

        private void Update()
        {
            while (_updates.TryDequeue(out var action)) action();
        }

        private void Started(PrototirPairingRequest request) => OnMainThread(() =>
        {
            if (_view != View.Requesting) return;
            _code = request.Code;
            _url = request.VerificationUrl;
            _title = request.PrototypeTitle;
            _note = "Waiting for approval. This screen updates on its own.";
            if (_qr != null) Destroy(_qr);
            _qr = Rasterize(request.QrSvg);
            _view = View.Approval;
        });

        private void Succeeded() => OnMainThread(() =>
        {
            ClearCode();
            _view = View.JustConnected;
        });

        private void Failed(string reason) => OnMainThread(() =>
        {
            ClearCode();
            _failure = reason;
            _view = View.Failed;
        });

        private void OnMainThread(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                action();
            else
                _updates.Enqueue(action);
        }

        private void ClearCode()
        {
            _code = null;
            _url = null;
            if (_qr != null) Destroy(_qr);
            _qr = null;
        }

        private void StartPairing()
        {
            _pairing?.Dispose();
            _pairing = new CancellationTokenSource();
            _view = View.Requesting;
            _note = null;
            _ = BeginAsync(_pairing.Token);
        }

        private async System.Threading.Tasks.Task BeginAsync(CancellationToken token)
        {
            try { await PrototirSdk.BeginPairingAsync(token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                PrototirSdk.CancelPairing();
                Failed("Could not reach Prototir: " + error.Message);
            }
        }

        private void CancelActive()
        {
            if (_view is View.Requesting or View.Approval)
                PrototirSdk.CancelPairing();
            _pairing?.Cancel();
            _pairing?.Dispose();
            _pairing = null;
        }

        private void Cancel()
        {
            CancelActive();
            ClearCode();
            _view = PrototirSdk.IsPaired ? View.Connected : View.Idle;
        }

        private void Close()
        {
            if (_instance == this) _instance = null;
            Closed?.Invoke();
            Destroy(gameObject);
        }

        private void OnGUI()
        {
            EnsureStyles();
            var previousMatrix = GUI.matrix;
            var previousDepth = GUI.depth;
            GUI.depth = -1000;
            var scale = Mathf.Min(Screen.width / 520f, Screen.height / 620f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            Fill(new Rect(0, 0, width, height), new Color(Background.r, Background.g, Background.b, 0.82f));

            var cardHeight = _view == View.Approval ? (_qr == null ? 398f : 588f) : 344f;
            var card = new Rect((width - 470f) / 2f, (height - cardHeight) / 2f, 470f, cardHeight);
            Fill(card, Surface);
            Fill(new Rect(card.x, card.y, card.width, 1), Hex("#303036"));
            GUILayout.BeginArea(new Rect(card.x + 24, card.y + 22, card.width - 48, card.height - 42));
            GUILayout.Label("PROTOTIR", _brandStyle);
            GUILayout.Space(12);
            GUILayout.Label(Heading(), _headingStyle);
            GUILayout.Space(10);
            GUILayout.Label(Body(), _bodyStyle);
            GUILayout.Space(18);

            if (_view == View.Approval)
            {
                Fill(GUILayoutUtility.GetRect(422, _qr == null ? 72 : 258), Background);
                var area = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(area.x, area.y + 12, area.width, 44), _code ?? "", _codeStyle);
                if (_qr != null)
                    GUI.DrawTexture(new Rect(area.center.x - 88, area.y + 65, 176, 176), _qr, ScaleMode.ScaleToFit);
                GUILayout.Space(12);
            }

            GUILayout.BeginHorizontal();
            switch (_view)
            {
                case View.Idle:
                    Button("Get a code", StartPairing, true);
                    Button("Not now", Close);
                    break;
                case View.Requesting:
                    Button("Cancel", Cancel);
                    break;
                case View.Approval:
                    if (!string.IsNullOrEmpty(_url))
                        Button("Open in browser", () => Application.OpenURL(_url), true);
                    Button("Copy code", () => { GUIUtility.systemCopyBuffer = _code; _note = "Code copied. Waiting for approval."; });
                    Button("Cancel", Cancel);
                    break;
                case View.Connected:
                    Button("Done", Close, true);
                    Button("Disconnect", () => { PrototirSdk.Unpair(); _view = View.Idle; });
                    break;
                case View.JustConnected:
                    Button("Play", Close, true);
                    break;
                case View.Failed:
                    Button("Try again", StartPairing, true);
                    Button("Not now", Close);
                    break;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(15);
            GUILayout.Label(Note(), _noteStyle);
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }

        private string Heading() => _view switch
        {
            View.Idle => "Connect this build",
            View.Requesting => "Getting a code",
            View.Approval => "Approve this build",
            View.Connected => "This build is connected",
            View.JustConnected => "Connected",
            _ => "Not connected",
        };

        private string Body() => _view switch
        {
            View.Idle => "Pairing links this copy to your Prototir account, so the plays and feedback it reports are attributed to you. It takes one approval on prototir.com and lasts for this machine.",
            View.Requesting => "Asking Prototir for a one-time code for this build.",
            View.Approval => "Open the link on any device, sign in, and enter this code" +
                (string.IsNullOrEmpty(_title) ? "." : " for " + _title + "."),
            View.Connected => "Plays and feedback from this machine are recorded against your Prototir account.",
            View.JustConnected => "This build can now report plays and send feedback as you.",
            _ => _failure ?? "Pairing did not finish.",
        };

        private string Note() => _view switch
        {
            View.Idle => "Until then this build reports nothing: there is nobody to attribute a play to.",
            View.Approval => _note,
            View.Connected => "You can disconnect it here, or from your account settings on prototir.com.",
            View.Failed => "Nothing was recorded. You can try again, or keep playing without pairing.",
            _ => "",
        };

        private void Button(string label, Action click, bool primary = false)
        {
            if (GUILayout.Button(label, primary ? _primaryStyle : _buttonStyle, GUILayout.Height(40)))
                click();
        }

        private void EnsureStyles()
        {
            if (_pixel != null) return;
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
            _brandStyle = Label(12, Accent, true);
            _headingStyle = Label(22, Text, true);
            _bodyStyle = Label(15, Muted);
            _noteStyle = Label(12, Muted);
            _codeStyle = Label(34, Text, true);
            _codeStyle.alignment = TextAnchor.MiddleCenter;
            _buttonBackground = Solid(Raised);
            _buttonHover = Solid(Hex("#303036"));
            _primaryBackground = Solid(Accent);
            _primaryHover = Solid(Hex("#93c5fd"));
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 40 };
            _buttonStyle.normal.textColor = Text;
            _buttonStyle.hover.textColor = Text;
            _buttonStyle.active.textColor = Text;
            _buttonStyle.normal.background = _buttonBackground;
            _buttonStyle.hover.background = _buttonHover;
            _buttonStyle.active.background = _buttonBackground;
            _primaryStyle = new GUIStyle(_buttonStyle);
            _primaryStyle.normal.textColor = AccentText;
            _primaryStyle.hover.textColor = AccentText;
            _primaryStyle.active.textColor = AccentText;
            _primaryStyle.normal.background = _primaryBackground;
            _primaryStyle.hover.background = _primaryHover;
            _primaryStyle.active.background = _primaryStyle.normal.background;
        }

        private GUIStyle Label(int size, Color color, bool bold = false) => new(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            wordWrap = true,
            normal = { textColor = color },
        };

        private Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = previous;
        }

        private static Texture2D Rasterize(string svg)
        {
            if (!PrototirQrSvg.TryDecode(svg, out var modules)) return null;
            var size = modules.GetLength(0);
            const int pixelsPerModule = 4;
            var texture = new Texture2D(size * pixelsPerModule, size * pixelsPerModule,
                TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[texture.width * texture.height];
            var dark = new Color32(17, 17, 19, 255);
            var light = new Color32(255, 255, 255, 255);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            for (var py = 0; py < pixelsPerModule; py++)
            for (var px = 0; px < pixelsPerModule; px++)
                pixels[(size - 1 - y) * pixelsPerModule * texture.width +
                       py * texture.width + x * pixelsPerModule + px] = modules[x, y] ? dark : light;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color Hex(string value) => ColorUtility.TryParseHtmlString(value, out var color)
            ? color : Color.white;
    }
}
#endif
