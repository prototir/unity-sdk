using System.Threading;
using UnityEngine;

namespace Prototir.Samples
{
    /// <summary>A working pairing screen for a downloadable build, in one file with no scene
    /// setup: drop this component on any GameObject and run.
    ///
    /// <para>The SDK draws nothing itself, because it cannot know your art direction, your input
    /// model, or whether you are in VR. This sample exists so you can see the whole flow work before
    /// you build your own, and so there is something to point at when the answer is "draw
    /// it yourself".</para>
    ///
    /// <para><b>IMGUI on purpose.</b> It needs no canvas, no prefab and no fonts, which makes it
    /// a sample rather than a component you should ship. Read it, then rebuild it in whatever UI
    /// your game already uses.</para></summary>
    public sealed class PrototirPairingPanel : MonoBehaviour
    {
        [Tooltip("Where the panel sits. Only affects this sample.")]
        [SerializeField] private Vector2 origin = new(24f, 24f);

        private string _code;
        private string _verificationUrl;
        private string _title;
        private string _message;
        private CancellationTokenSource _pairing;

        private void OnEnable()
        {
            PrototirSdk.PairingStarted += OnPairingStarted;
            PrototirSdk.PairingSucceeded += OnPairingSucceeded;
            PrototirSdk.PairingFailed += OnPairingFailed;
        }

        private void OnDisable()
        {
            PrototirSdk.PairingStarted -= OnPairingStarted;
            PrototirSdk.PairingSucceeded -= OnPairingSucceeded;
            PrototirSdk.PairingFailed -= OnPairingFailed;
            // A pairing left running past this object is a poll loop nobody is watching.
            _pairing?.Cancel();
            _pairing?.Dispose();
            _pairing = null;
        }

        private void OnPairingStarted(Native.PrototirPairingRequest request)
        {
            _code = request.Code;
            _verificationUrl = request.VerificationUrl;
            _title = request.PrototypeTitle;
            _message = null;
            // request.QrSvg is the same code as an SVG the server rendered. Draw it if you have
            // an SVG renderer; the code and the link work on their own if you do not.
        }

        private void OnPairingSucceeded()
        {
            _code = null;
            _message = "Paired. This build can now report sessions and send feedback.";
        }

        private void OnPairingFailed(string reason)
        {
            _code = null;
            _message = reason;
        }

        private void OnGUI()
        {
            const float width = 420f;
            GUILayout.BeginArea(new Rect(origin.x, origin.y, width, 320f), GUI.skin.box);

            GUILayout.Label("<b>Prototir</b>", Rich());
            GUILayout.Label($"Status: {PrototirSdk.PairingState}");

            if (!string.IsNullOrEmpty(_code))
            {
                GUILayout.Space(8f);
                GUILayout.Label(string.IsNullOrEmpty(_title) ? "Pair this build" : $"Pair: {_title}");
                GUILayout.Label($"<size=28><b>{_code}</b></size>", Rich());
                GUILayout.Label($"Approve it at {_verificationUrl}");
                if (GUILayout.Button("Cancel")) CancelPairing();
            }
            else if (PrototirSdk.IsPaired)
            {
                GUILayout.Space(8f);
                if (GUILayout.Button("Send test feedback")) SendFeedback();
                if (GUILayout.Button("Unpair")) PrototirSdk.Unpair();
            }
            else
            {
                GUILayout.Space(8f);
                if (GUILayout.Button("Pair this build")) StartPairing();
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(8f);
                GUILayout.Label(_message);
            }

            GUILayout.EndArea();
        }

        private void StartPairing()
        {
            _message = null;
            _pairing?.Dispose();
            _pairing = new CancellationTokenSource();
            // Deliberately not awaited: the panel reacts to the events instead, which is what a
            // game's own screen would do.
            _ = PrototirSdk.BeginPairingAsync(_pairing.Token);
        }

        private void CancelPairing()
        {
            PrototirSdk.CancelPairing();
            _pairing?.Cancel();
            _code = null;
            _message = "Pairing cancelled.";
        }

        private async void SendFeedback()
        {
            _message = "Sending...";
            var sent = await PrototirSdk.SendFeedbackAsync("Hello from a downloaded build.");
            _message = sent ? "Feedback posted on the prototype page." : "That feedback was not accepted.";
        }

        private static GUIStyle Rich() => new(GUI.skin.label) { richText = true };
    }
}
