using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Prototir.Native
{
    /// <summary>One quarter-second of play: its frame rate, its slowest frame, and memory.</summary>
    public readonly struct PrototirPerformanceSample
    {
        public PrototirPerformanceSample(float fps, float worstFrameMs, float memoryMb)
        {
            Fps = fps; WorstFrameMs = worstFrameMs; MemoryMb = memoryMb;
        }
        public float Fps { get; }
        public float WorstFrameMs { get; }
        /// <summary>Managed memory in MB, or a negative number when unknown.</summary>
        public float MemoryMb { get; }
    }

    /// <summary>Frame rate, slowest frame and memory while the Performance panel is open (§16.7).
    ///
    /// <para>Engine-free: the Unity side feeds it one frame time per frame, and only while the panel
    /// is open, so a build pays nothing for it otherwise. Frames are folded into
    /// <see cref="BucketSeconds"/> buckets and one minute (<see cref="History"/>) is kept.</para></summary>
    public sealed class PrototirPerformanceSampler
    {
        public const float BucketSeconds = 0.25f;
        public const int History = 240;
        private readonly List<PrototirPerformanceSample> _samples = new List<PrototirPerformanceSample>(History);
        private float _bucketTime, _worst, _recorded;
        private int _frames;

        public IReadOnlyList<PrototirPerformanceSample> Samples => _samples;
        public int Version { get; private set; }

        /// <summary>Records one frame. Returns true when a new sample was completed.</summary>
        public bool AddFrame(float deltaSeconds, float memoryMb = -1)
        {
            if (deltaSeconds <= 0 || float.IsNaN(deltaSeconds)) return false;
            _frames++;
            _bucketTime += deltaSeconds;
            _recorded += deltaSeconds;
            if (deltaSeconds * 1000f > _worst) _worst = deltaSeconds * 1000f;
            if (_bucketTime < BucketSeconds) return false;
            _samples.Add(new PrototirPerformanceSample(_frames / _bucketTime, _worst, memoryMb));
            if (_samples.Count > History) _samples.RemoveAt(0);
            _bucketTime = 0; _frames = 0; _worst = 0;
            Version++;
            return true;
        }

        public void Reset()
        {
            _samples.Clear(); _bucketTime = 0; _frames = 0; _worst = 0; _recorded = 0; Version++;
        }

        /// <summary>A plain-text summary for Copy and comments, in the web SDK's form.</summary>
        public string Summary(string platform)
        {
            if (_samples.Count == 0) return "No performance recorded yet.";
            var fps = _samples.Select(sample => sample.Fps).OrderBy(value => value).ToList();
            var low = fps[(int)Math.Floor(fps.Count * 0.01)];
            var worst = _samples.Max(sample => sample.WorstFrameMs);
            var memory = _samples.Where(sample => sample.MemoryMb >= 0).Select(sample => sample.MemoryMb).ToList();
            var c = CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                $"[performance] {Math.Round(_recorded).ToString(c)}s recorded, {platform}",
                $"[performance] average {fps.Average().ToString("0.0", c)} fps, lowest 1% {low.ToString("0.0", c)} fps, slowest frame {worst.ToString("0.0", c)} ms",
            };
            if (memory.Count > 0)
                lines.Add($"[performance] managed memory {memory.Min().ToString("0", c)}-{memory.Max().ToString("0", c)} MB");
            return string.Join("\n", lines);
        }
    }
}
