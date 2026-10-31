using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using Xunit;

namespace CustomComparisons.Engine.Tests;

/// <summary>Version-3 blocks: trim outliers, finished under, at least N, attempt #.</summary>
public class MoreBlockTests
{
    private static readonly SamplesNode History = new SegmentHistoryNode();

    private static ComparisonResult Run(TimesNode root, RunData run) =>
        new ComparisonEvaluator(new[] { new ComparisonProgram("Test", root) }).Evaluate("Test", run);

    private static double?[] Seconds(IEnumerable<TimeSpan?> times) =>
        times.Select(t => t.HasValue ? (double?)Math.Round(t.Value.TotalSeconds, 6) : null).ToArray();

    private static RunData TenAttempts()
    {
        var builder = new RunBuilder("A");
        foreach (double t in new double[] { 50, 1, 20, 21, 22, 23, 24, 25, 26, 99 })
        {
            builder.Attempt(t);
        }

        return builder.Build();
    }

    // ------------------------------------------------------------ trim outliers

    [Fact]
    public void Trim_drops_the_fastest_and_slowest_share()
    {
        // 10% of 10 times = 1 from each end: the 1 and the 99 go, leaving 20..26 and 50.
        TimesNode min = new AggregateNode(AggregateKind.Min, new TrimOutliersNode(History, 10));
        TimesNode max = new AggregateNode(AggregateKind.Max, new TrimOutliersNode(History, 10));

        Assert.Equal(new double?[] { 20 }, Seconds(Run(min, TenAttempts()).SegmentTimes));
        Assert.Equal(new double?[] { 50 }, Seconds(Run(max, TenAttempts()).SegmentTimes));
    }

    [Fact]
    public void Trim_leaves_small_lists_alone()
    {
        // 10% of 3 rounds down to 0, so nothing is dropped.
        RunData run = new RunBuilder("A").Attempt(1).Attempt(5).Attempt(9).Build();

        TimesNode root = new AggregateNode(AggregateKind.Min, new TrimOutliersNode(History, 10));

        Assert.Equal(new double?[] { 1 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Trim_keeps_attempt_order_for_last_n()
    {
        // After trimming 1 and 99, "last 2" must still mean the two most recent survivors (25, 26).
        TimesNode root = new AggregateNode(AggregateKind.Average, new LastNNode(new TrimOutliersNode(History, 10), 2));

        Assert.Equal(new double?[] { 25.5 }, Seconds(Run(root, TenAttempts()).SegmentTimes));
    }

    // ------------------------------------------------------------ finished under

    [Fact]
    public void Finished_under_keeps_only_runs_faster_than_the_limit()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 50)   // finished 60
            .Attempt(12, 40)   // finished 52
            .Attempt(11)       // reset: no final time
            .Attempt(30, 30)   // finished 60, not under
            .Build();

        TimesNode root = new AggregateNode(AggregateKind.Max, new FinishedUnderNode(History, TimeSpan.FromSeconds(60)));

        Assert.Equal(new double?[] { 12, 40 }, Seconds(Run(root, run).SegmentTimes));
    }

    // ------------------------------------------------------------ at least N

    [Fact]
    public void At_least_empties_segments_with_too_few_times_so_a_fallback_can_fill_them()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 20)
            .Attempt(12, 22)
            .Attempt(14)       // B has only two times
            .Comparison("Personal Best", 11, 31)
            .Build();

        TimesNode root = new FallbackNode(
            new AggregateNode(AggregateKind.Median, new AtLeastNode(History, 3)),
            new ComparisonSourceNode("Personal Best"));

        Assert.Equal(new double?[] { 12, 20 }, Seconds(Run(root, run).SegmentTimes));
    }

    // ------------------------------------------------------------ attempt #

    [Fact]
    public void Attempt_gives_that_runs_segments()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Attempt(10, 20, 30)
            .Attempt(11, 21, 31)
            .Build();

        Assert.Equal(new double?[] { 11, 21, 31 }, Seconds(Run(new AttemptNode(2), run).SegmentTimes));
    }

    [Fact]
    public void Attempt_is_empty_where_that_run_skipped_or_reset()
    {
        RunData run = new RunBuilder("A", "B", "C", "D")
            .Attempt(10, null, 50)   // skipped B, so C covers B+C; reset before D
            .Build();

        Assert.Equal(new double?[] { 10, null, null, null }, Seconds(Run(new AttemptNode(1), run).SegmentTimes));
    }

    [Fact]
    public void An_attempt_that_doesnt_exist_is_reported()
    {
        RunData run = new RunBuilder("A").Attempt(10).Build();

        ComparisonResult result = Run(new AttemptNode(42), run);

        Assert.False(result.Succeeded);
        Assert.Contains("no attempt #42", result.Error);
    }

    // ------------------------------------------------------------ file format

    private static TimesNode EveryV3Block() =>
        new PickNode(
            PickKind.Faster,
            new AttemptNode(123),
            new AggregateNode(
                AggregateKind.Median,
                new AtLeastNode(new FinishedUnderNode(new TrimOutliersNode(History, 10), TimeSpan.FromMinutes(20)), 5)));

    [Fact]
    public void Version_3_blocks_survive_a_round_trip()
    {
        string once = ProgramSerializer.Write(new ProgramDocument(new ComparisonProgram("V3", EveryV3Block())));
        string twice = ProgramSerializer.Write(ProgramSerializer.Read(once));

        Assert.Equal(once, twice);
        Assert.Contains("\"version\": 3", once);
    }

    [Fact]
    public void A_version_3_block_nested_inside_a_version_1_block_still_needs_version_3()
    {
        var program = new ComparisonProgram("Nested", new AddPerSegmentNode(new AttemptNode(5), TimeSpan.FromSeconds(1)));

        Assert.Contains("\"version\": 3", ProgramSerializer.Write(new ProgramDocument(program)));
    }

    [Theory]
    [InlineData("{\"type\":\"attempt\",\"number\":0}", "root.number")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"trimOutliers\",\"percent\":50,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.percent")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"atLeast\",\"count\":0,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.count")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"finishedUnder\",\"totalSeconds\":-5,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.totalSeconds")]
    [InlineData("{\"type\":\"atLeast\",\"count\":5,\"source\":{\"type\":\"segmentHistory\"}}", "Put it inside")]
    public void Bad_version_3_values_name_the_place(string root, string expected)
    {
        string text = "{\"format\":\"livesplit-custom-comparison\",\"version\":3,\"name\":\"T\",\"root\":" + root + "}";
        Assert.Contains(expected, Assert.Throws<ProgramFormatException>(() => ProgramSerializer.Read(text)).Message);
    }
}
