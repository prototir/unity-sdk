using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;

namespace Prototir
{
    /// <summary>Add explicitly to a scene to enable screenshot feedback in Web builds.</summary>
    public sealed class PrototirReview : MonoBehaviour
    {
        public string ProjectId = "my-prototype";
        public string BuildId = "development";
        public string Corner = "bottom-left";
        [Tooltip("auto lets Prototir draw the control on its own surfaces; watermark always shows the Prototir mark.")]
        public string Launcher = "auto";
        [Tooltip("auto follows the player's light/dark preference.")]
        public string Theme = "auto";
        public bool PauseWhileReviewing = true;
        public UnityEvent<bool> ReviewVisibilityChanged = new UnityEvent<bool>();
        private float previousTimeScale;
        private bool reviewing;
        private bool previousKeyboardCapture;
        private GameObject receiverObject;

        [Serializable] private class Options { public string project; public string build; public string corner; public string launcher; public string theme; }
        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Unique bridge target avoids ambiguous SendMessage delivery.
            receiverObject = new GameObject("__PrototirReview_" + GetInstanceID());
            receiverObject.transform.SetParent(transform, false);
            receiverObject.AddComponent<PrototirReviewReceiver>().Owner = this;
            Prototir_ReviewEnable(JsonUtility.ToJson(new Options { project = ProjectId, build = BuildId, corner = Corner, launcher = Launcher, theme = Theme }), receiverObject.name);
#endif
        }
        public void OnReviewVisibility(string value)
        {
            bool open = value == "true";
            if (open == reviewing) return;
            if (open) { previousTimeScale = Time.timeScale; if (PauseWhileReviewing) Time.timeScale = 0; }
            else if (PauseWhileReviewing) Time.timeScale = previousTimeScale;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (open) { previousKeyboardCapture = WebGLInput.captureAllKeyboardInput; WebGLInput.captureAllKeyboardInput = false; }
            else WebGLInput.captureAllKeyboardInput = previousKeyboardCapture;
#endif
            reviewing = open;
            ReviewVisibilityChanged.Invoke(open);
        }
        public void CaptureReview(string unused) { StartCoroutine(Capture()); }
        private IEnumerator Capture()
        {
            yield return new WaitForEndOfFrame();
#if UNITY_WEBGL && !UNITY_EDITOR
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            try { Prototir_ReviewCaptured("data:image/jpeg;base64," + Convert.ToBase64String(screenshot.EncodeToJPG(80))); }
            finally { Destroy(screenshot); }
#endif
        }
        private void OnDestroy()
        {
            if (reviewing) OnReviewVisibility("false");
#if UNITY_WEBGL && !UNITY_EDITOR
            Prototir_ReviewDisable();
#endif
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void Prototir_ReviewEnable(string json, string receiver);
        [DllImport("__Internal")] private static extern void Prototir_ReviewCaptured(string data);
        [DllImport("__Internal")] private static extern void Prototir_ReviewDisable();
#endif
    }
}
