using System.Linq;
using LiveSplit.Model;
using Xunit;

namespace LiveSplit.CustomComparisons.Tests
{
    /// <summary>
    /// Three finished attempts. The fast one has a start and no end. The fastest one has
    /// neither. The slow one has both, years ago.
    /// </summary>
    public class AttemptsWithoutEndedTests
    {
        private static readonly string BestRecent = LiveSplitHarness.ProgramJson(
            "Best Recent",
            "{\"type\":\"bestRecentRun\",\"days\":36500}",
            version: 4);

        private static readonly string RecentMin = LiveSplitHarness.ProgramJson(
            "Recent Min",
            "{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"recentDays\",\"days\":36500,\"source\":{\"type\":\"segmentHistory\"}}}",
            version: 2);

        [Fact]
        public void A_start_time_is_enough_and_a_missing_timestamp_is_dropped_quietly()
        {
            using (Session session = LiveSplitHarness.Open("attempts-without-ended.lss", BestRecent, RecentMin))
            {
                IRun run = session.Run;
                Assert.Null(run.AttemptHistory.Single(a => a.Index == 2).Ended);
                Assert.NotNull(run.AttemptHistory.Single(a => a.Index == 2).Started);
                Assert.Null(run.AttemptHistory.Single(a => a.Index == 3).Ended);
                Assert.Null(run.AttemptHistory.Single(a => a.Index == 3).Started);

                // Attempt 2 (start only, final 70s, segments 30+40) beats attempt 1 (final 100s).
                // Attempt 3 (final 2s, no timestamps) would win if it were kept.
                Assert.Null(LiveSplitHarness.Generator(run, "Best Recent").LastError);
                Assert.Equal(new double?[] { 30, 70 }, LiveSplitHarness.Splits(run, "Best Recent", TimingMethod.RealTime));

                Assert.Null(LiveSplitHarness.Generator(run, "Recent Min").LastError);
                // Minimum of attempt 1 (40+60) and attempt 2 (30+40). Attempt 3's 1+1 is absent.
                Assert.Equal(new double?[] { 30, 70 }, LiveSplitHarness.Splits(run, "Recent Min", TimingMethod.RealTime));

                // Nothing on the comparison says an attempt was left out.
                Assert.Null(session.Component.StatusOf(session.Component.Library.Entries.Single(e => e.Name == "Best Recent")));
                Assert.Null(session.Component.StatusOf(session.Component.Library.Entries.Single(e => e.Name == "Recent Min")));
            }
        }
    }
}
