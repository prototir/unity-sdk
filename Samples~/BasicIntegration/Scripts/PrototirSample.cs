using System;
using UnityEngine;
using UnityEngine.UI;

namespace Prototir.Samples
{
    public sealed class PrototirSample : MonoBehaviour
    {
        [SerializeField] private Text status;
        private int score;

        private async void Start()
        {
            PrototirSdk.Ready();
            status.text = "Ready";
            try
            {
                var saved = await PrototirSdk.StorageGetAsync("sample.score");
                int.TryParse(saved, out score);
                status.text = $"Ready - saved score {score}";
            }
            catch (Exception exception)
            {
                status.text = $"Storage unavailable: {exception.Message}";
            }
        }

        public async void AddPoint()
        {
            score += 1;
            PrototirSdk.Score(score);
            PrototirSdk.Event("sample_point", new SampleEvent { score = score });
            try
            {
                await PrototirSdk.StorageSetAsync("sample.score", score.ToString());
                status.text = $"Score {score}";
            }
            catch (Exception exception)
            {
                status.text = $"Score {score} - save unavailable: {exception.Message}";
            }
        }

        [Serializable] private sealed class SampleEvent { public int score; }
    }
}
