using System;
using System.Linq;
using Prototir.Native;
using Xunit;

namespace Prototir.Native.Tests
{
    /// <summary>The engine-free halves of Feedback &amp; tools (§16.7): the console buffer and the
    /// performance sampler.</summary>
    public class FeedbackToolsTests
    {
        [Fact]
        public void The_console_keeps_the_newest_entries_in_order()
        {
            var buffer = new PrototirConsoleBuffer();
            for (var i = 1; i <= PrototirConsoleBuffer.Capacity + 20; i++) buffer.Add(PrototirLogLevel.Log, $"tick {i}");
            var entries = buffer.Entries();
            Assert.Equal(PrototirConsoleBuffer.Capacity, entries.Count);
            Assert.Equal("tick 21", entries[0].Text);
            Assert.Equal($"tick {PrototirConsoleBuffer.Capacity + 20}", entries[^1].Text);
        }

        [Fact]
        public void Errors_keep_the_first_lines_of_their_stack_and_text_matches_the_web_form()
        {
            var buffer = new PrototirConsoleBuffer();
            var time = new DateTime(2026, 10, 4, 9, 30, 1, 250, DateTimeKind.Utc);
            buffer.Add(PrototirLogLevel.Warning, "Texture too large", "ignored for warnings", time);
            buffer.Add(PrototirLogLevel.Error, "NullReferenceException", "Player.Update ()\nGame.Tick ()\n", time);
            Assert.Equal(
                "09:30:01.250 [warn] Texture too large\n09:30:01.250 [error] NullReferenceException\n  Player.Update ()\n  Game.Tick ()",
                buffer.Text());
        }

        [Fact]
        public void Clearing_empties_the_console_and_changes_its_version()
        {
            var buffer = new PrototirConsoleBuffer();
            buffer.Add(PrototirLogLevel.Log, "ready");
            var before = buffer.Version;
            buffer.Clear();
            Assert.Empty(buffer.Entries());
            Assert.NotEqual(before, buffer.Version);
        }

        [Fact]
        public void Frames_fold_into_quarter_second_samples_with_the_slowest_frame()
        {
            var sampler = new PrototirPerformanceSampler();
            var completed = 0;
            for (var i = 0; i < 14; i++) if (sampler.AddFrame(1 / 60f, 120)) completed++;
            if (sampler.AddFrame(0.05f, 121)) completed++;
            Assert.Equal(1, completed);
            var sample = sampler.Samples.Single();
            Assert.InRange(sample.Fps, 50, 70);
            Assert.Equal(50, sample.WorstFrameMs, 1);
        }

        [Fact]
        public void One_minute_of_history_is_kept_and_summarised()
        {
            var sampler = new PrototirPerformanceSampler();
            for (var i = 0; i < 60 * 70; i++) sampler.AddFrame(1 / 60f, 100 + i / 15 % 3);
            Assert.Equal(PrototirPerformanceSampler.History, sampler.Samples.Count);
            var summary = sampler.Summary("Windows");
            Assert.Contains("[performance] 70s recorded, Windows", summary);
            Assert.Contains("average 60.0 fps", summary);
            Assert.Contains("managed memory 100-102 MB", summary);
            Assert.Equal("No performance recorded yet.", new PrototirPerformanceSampler().Summary("Windows"));
        }

        [Fact]
        public void A_comment_body_leaves_out_what_was_not_attached()
        {
            Assert.Equal("{\"text\":\"Jump feels late\"}", PrototirFeedbackPayload.Json("  Jump feels late "));
            Assert.Equal("{\"text\":\"a\",\"clientId\":\"id\"}", PrototirFeedbackPayload.Json("a", "id", "  "));
        }

        [Fact]
        public void A_comment_body_carries_the_log_and_a_pinned_screenshot()
        {
            var json = PrototirFeedbackPayload.Json("Boss \"freezes\"", null, "09:00:00.000 [error] boom\n  at Boss.Update",
                new PrototirScreenshot("data:image/jpeg;base64,AAA", 0.25, 1.4));
            Assert.Equal(
                "{\"text\":\"Boss \\\"freezes\\\"\",\"console\":\"09:00:00.000 [error] boom\\n  at Boss.Update\"," +
                "\"screenshot\":{\"image\":\"data:image/jpeg;base64,AAA\",\"x\":0.25,\"y\":1}}",
                json);
        }
    }
}
