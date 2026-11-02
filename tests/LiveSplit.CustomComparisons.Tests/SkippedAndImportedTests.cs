using LiveSplit.Model;
using Xunit;

namespace LiveSplit.CustomComparisons.Tests
{
    /// <summary>
    /// Attempt -2 and attempt 0 are imported history (LiveSplit used non-positive ids before
    /// attempt history existed). Attempt 1 skips the middle split; its following time is the
    /// combined segment and must not count.
    /// </summary>
    public class SkippedAndImportedTests
    {
        private const string Name = "History Min";

        private static readonly string Program = LiveSplitHarness.ProgramJson(
            Name,
            "{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"segmentHistory\"}}");

        [Fact]
        public void Imported_non_positive_attempt_ids_count_and_skipped_splits_do_not()
        {
            using (Session session = LiveSplitHarness.Open("skipped-and-imported.lss", Program))
            {
                IRun run = session.Run;
                Assert.True(run[0].SegmentHistory.ContainsKey(-2));
                Assert.True(run[0].SegmentHistory.ContainsKey(0));
                Assert.Null(run[1].SegmentHistory[1].RealTime);

                Assert.Null(LiveSplitHarness.Generator(run, Name).LastError);
                // Imported times: First 10 and 11, Skipped 20 and 21, Combined 50 and 40.
                // Attempt 1: First 5, Skipped (empty), Combined 1. The 5 counts; the 1 is the
                // combined segment after a skip, so the minimum on Combined stays 40.
                // Splits: 5, 25, 65.
                Assert.Equal(new double?[] { 5, 25, 65 }, LiveSplitHarness.Splits(run, Name, TimingMethod.RealTime));
                Assert.Equal(new double?[] { 5, 25, 65 }, LiveSplitHarness.Splits(run, Name, TimingMethod.GameTime));
            }
        }
    }
}
