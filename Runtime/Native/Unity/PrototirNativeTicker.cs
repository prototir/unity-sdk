using System.Collections;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Prototir.Native
{
    /// <summary>The heartbeat behind <see cref="PrototirNativeRuntime.FlushAsync"/>.
    ///
    /// <para>The runtime stays a plain static class, because everything that decides anything
    /// should be testable without an engine. This is the one thing it cannot do for itself: a
    /// coroutine needs a MonoBehaviour, and nothing else here has one.</para>
    ///
    /// <para>Hidden and unsaved, so it never appears in a creator's hierarchy and never ends up
    /// in a scene file. <see cref="HideFlags.HideAndDontSave"/> also survives scene loads, which
    /// is what keeps one play reporting as one session across a level change.</para></summary>
    internal sealed class PrototirNativeTicker : MonoBehaviour
    {
        private static PrototirMainThread _mainThread;

        internal static void Install()
        {
            _mainThread = new PrototirMainThread(Thread.CurrentThread.ManagedThreadId);
            var host = new GameObject(nameof(PrototirNativeTicker))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            host.AddComponent<PrototirNativeTicker>();
        }

        /// <summary>The protocol deliberately uses ConfigureAwait(false), so later requests may
        /// start on a worker. UnityWebRequest must be created and driven by the player loop.</summary>
        internal static Task<T> RunOnMainThread<T>(Func<Task<T>> work)
        {
            if (_mainThread == null)
                throw new InvalidOperationException("Prototir's Unity runtime has not started.");
            return _mainThread.Run(work);
        }

        private void Awake() => PrototirNativeRuntime.InitializeStorage(Application.persistentDataPath);

        private void Update() => _mainThread?.Drain();

        private void Start() => StartCoroutine(Report());

        private IEnumerator Report()
        {
            // Realtime, so a game sitting on a pause menu with timeScale at zero still reports the
            // play it is in the middle of.
            var interval = new WaitForSecondsRealtime(PrototirNativeRuntime.FlushIntervalSeconds);
            while (true)
            {
                yield return interval;
                Flush();
            }
        }

        /// <summary>Alt-tabbing away is where a play most often ends for good, and it is one of
        /// the last moments the player loop is still running to carry a request.</summary>
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Flush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        /// <summary>Deliberately not awaited: nothing in the game should wait on a report, and a
        /// failure needs no handling here because the session stays in memory for the next one.</summary>
        private static void Flush() => _ = PrototirNativeRuntime.FlushAsync(CancellationToken.None);
    }
}
