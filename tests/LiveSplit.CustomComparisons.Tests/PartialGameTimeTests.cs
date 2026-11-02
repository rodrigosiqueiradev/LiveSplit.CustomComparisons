using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Model;
using LiveSplit.CustomComparisons;
using LiveSplit.Model;
using Xunit;

namespace LiveSplit.CustomComparisons.Tests
{
    /// <summary>
    /// One attempt recorded game time only from Temple onward. A second attempt has game time
    /// on every segment, so a minimum still has something to show.
    /// </summary>
    public class PartialGameTimeTests
    {
        private const string Name = "History Min";

        private static readonly string Program = LiveSplitHarness.ProgramJson(
            Name,
            "{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"segmentHistory\"}}");

        [Fact]
        public void Real_time_minimum_uses_every_segment()
        {
            using (Session session = LiveSplitHarness.Open("partial-game-time.lss", Program))
            {
                Assert.Null(LiveSplitHarness.Generator(session.Run, Name).LastError);
                // Attempt 1 is 10/20/30, attempt 2 is 100/100/100. Minimum splits are 10, 30, 60.
                Assert.Equal(new double?[] { 10, 30, 60 }, LiveSplitHarness.Splits(session.Run, Name, TimingMethod.RealTime));
            }
        }

        [Fact]
        public void Missing_game_time_is_treated_as_a_skipped_split()
        {
            using (Session session = LiveSplitHarness.Open("partial-game-time.lss", Program))
            {
                var source = new ComparisonSource(session.Run, session.State.Settings, new HashSet<string>());
                RunData game = RunAdapter.ToRunData(session.Run, TimingMethod.GameTime, source);

                // The history entry is there. Game time on Cave is missing, not a deleted key.
                Assert.True(game.Segments[0].History.ContainsKey(1));
                Assert.Null(game.Segments[0].History[1]);
                Assert.Equal(TimeSpan.FromSeconds(8), game.Segments[1].History[1]);
                Assert.Equal(TimeSpan.FromSeconds(12), game.Segments[2].History[1]);

                // Audit §4.1, confirmed: that null is treated as a skipped split, so Temple's 8s
                // is thrown away and the minimum falls back to the other attempt's 100s.
                // A runner should see 100, 108, 120 (Cave 100, Temple 8, Finish 12).
                // The comparison column shows 100, 200, 212 instead. Reported separately.
                Assert.Equal(new double?[] { 100, 200, 212 }, LiveSplitHarness.Splits(session.Run, Name, TimingMethod.GameTime));

                string reply = LiveSplitHarness.Preview(session.Component, session.State, Program, TimingMethod.GameTime);
                // Segment times the editor shows: Temple is 100s, not 8s.
                Assert.Equal(new double?[] { 100_000, 100_000, 12_000 }, LiveSplitHarness.PreviewSegmentMilliseconds(reply));
            }
        }
    }
}
