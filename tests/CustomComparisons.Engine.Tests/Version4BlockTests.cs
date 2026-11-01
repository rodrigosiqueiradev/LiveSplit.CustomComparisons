using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using Xunit;

namespace CustomComparisons.Engine.Tests;

/// <summary>Version-4 blocks: reached segment, weighted average, keep between, round, best recent run.</summary>
public class Version4BlockTests
{
    private static readonly SamplesNode History = new SegmentHistoryNode();

    private static ComparisonResult Run(TimesNode root, RunData run) =>
        new ComparisonEvaluator(new[] { new ComparisonProgram("Test", root) }).Evaluate("Test", run);

    private static double?[] Seconds(IEnumerable<TimeSpan?> times) =>
        times.Select(t => t.HasValue ? (double?)Math.Round(t.Value.TotalSeconds, 6) : null).ToArray();

    private static TimesNode Pb => new ComparisonSourceNode("Personal Best");

    // ------------------------------------------------------------ reached segment

    [Fact]
    public void Reached_segment_keeps_only_attempts_that_got_that_far()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Attempt(10)            // reset in B
            .Attempt(20, 5)         // reached B, reset in C
            .Attempt(30, 6, 7)      // finished
            .Build();

        TimesNode root = new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 2));

        // A: only attempts 2 and 3 reached B, so 10 is left out. B: both. C: only attempt 3 has a time.
        Assert.Equal(new double?[] { 30, 6, 7 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Reached_segment_counts_a_skipped_segment_as_reached()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Attempt(10, null, 20)  // skipped B but kept going
            .Attempt(99)            // reset in B
            .Build();

        TimesNode root = new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 2));

        Assert.Equal(10, Seconds(Run(root, run).SegmentTimes)[0]);
    }

    [Fact]
    public void Reached_segment_past_the_end_keeps_nothing()
    {
        RunData run = new RunBuilder("A").Attempt(10).Build();

        TimesNode root = new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 5));

        Assert.Equal(new double?[] { null }, Seconds(Run(root, run).SegmentTimes));
    }

    // ------------------------------------------------------------ weighted average

    [Fact]
    public void Weighted_average_halves_the_weight_every_half_life()
    {
        // Newest first: 10 (weight 1), 20 (0.5), 40 (0.25). (10 + 10 + 10) / 1.75
        RunData run = new RunBuilder("A").Attempt(40).Attempt(20).Attempt(10).Build();

        TimesNode root = new WeightedAverageNode(History, 1);

        Assert.Equal(Math.Round(30 / 1.75, 6), Seconds(Run(root, run).SegmentTimes)[0]);
    }

    [Fact]
    public void Weighted_average_leans_towards_recent_times()
    {
        RunData run = new RunBuilder("A").Attempt(30).Attempt(30).Attempt(30).Attempt(10).Build();

        double weighted = Seconds(Run(new WeightedAverageNode(History, 2), run).SegmentTimes)[0].Value;
        double plain = Seconds(Run(new AggregateNode(AggregateKind.Average, History), run).SegmentTimes)[0].Value;

        Assert.True(weighted < plain);
        Assert.True(weighted > 10);
    }

    [Fact]
    public void Weighted_average_of_nothing_is_empty()
    {
        RunData run = new RunBuilder("A", "B").Attempt(10).Build();

        Assert.Equal(new double?[] { 10, null }, Seconds(Run(new WeightedAverageNode(History, 5), run).SegmentTimes));
    }

    // ------------------------------------------------------------ keep between

    [Fact]
    public void Keep_between_pulls_times_inside_the_limits()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Comparison("Source", 5, 25, 35)   // segments 5, 20, 10
            .Comparison("Low", 8, 16, 24)      // segments 8, 8, 8
            .Comparison("High", 9, 21, 33)     // segments 9, 12, 12
            .Build();

        TimesNode root = new ClampNode(new ComparisonSourceNode("Source"), new ComparisonSourceNode("Low"), new ComparisonSourceNode("High"));

        Assert.Equal(new double?[] { 8, 12, 10 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Keep_between_ignores_a_missing_limit_and_never_faster_wins_when_they_cross()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Comparison("Source", 50, 90, 160)  // segments 50, 40, 70
            .Comparison("Low", 20, 40, 60)      // segments 20, 20, 20
            .Comparison("High", 100, 115, null) // segments 100, 15 (below "low"), unknown
            .Build();

        TimesNode root = new ClampNode(new ComparisonSourceNode("Source"), new ComparisonSourceNode("Low"), new ComparisonSourceNode("High"));

        Assert.Equal(new double?[] { 50, 20, 70 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Keep_between_traces_to_the_limit_that_was_used()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Comparison("Source", 5, 25, 35)
            .Comparison("Low", 8, 16, 24)
            .Comparison("High", 9, 21, 33)
            .Build();
        var program = new ComparisonProgram("T", new ClampNode(new ComparisonSourceNode("Source"), new ComparisonSourceNode("Low"), new ComparisonSourceNode("High")));

        IReadOnlyList<string> origins = new ComparisonEvaluator(new[] { program }).EvaluateWithOrigins("T", run).Origins;

        Assert.Equal(new[] { "low", "high", "source" }, origins);
    }

    // ------------------------------------------------------------ round

    [Theory]
    [InlineData(0.1, 12.34, 12.3)]
    [InlineData(0.1, 12.35, 12.4)]
    [InlineData(1, 59.5, 60)]
    [InlineData(1, 59.49, 59)]
    public void Round_goes_to_the_nearest_step(double step, double value, double expected)
    {
        RunData run = new RunBuilder("A").Comparison("X", value).Build();

        TimesNode root = new RoundNode(new ComparisonSourceNode("X"), TimeSpan.FromSeconds(step));

        Assert.Equal(expected, Seconds(Run(root, run).SegmentTimes)[0]);
    }

    [Fact]
    public void Rounded_segments_give_rounded_splits()
    {
        RunData run = new RunBuilder("A", "B").Comparison("X", 10.26, 20.68).Build();

        ComparisonResult result = Run(new RoundNode(new ComparisonSourceNode("X"), TimeSpan.FromSeconds(0.1)), run);

        Assert.Equal(new double?[] { 10.3, 10.4 }, Seconds(result.SegmentTimes));
        Assert.Equal(new double?[] { 10.3, 20.7 }, Seconds(result.SplitTimes));
    }

    // ------------------------------------------------------------ best recent run

    [Fact]
    public void Best_recent_run_is_the_fastest_finished_run_in_the_window()
    {
        RunData run = new RunBuilder("A", "B")
            .AttemptDaysAgo(60, 5, 5)       // fastest overall, but too old
            .AttemptDaysAgo(10, 10, 12)     // 22
            .AttemptDaysAgo(5, 9, 11)       // 20: the pick
            .AttemptDaysAgo(1, 1)           // reset
            .Build();

        Assert.Equal(new double?[] { 9, 11 }, Seconds(Run(new BestRecentRunNode(30), run).SegmentTimes));
    }

    [Fact]
    public void Best_recent_run_is_empty_when_nothing_finished_in_the_window()
    {
        RunData run = new RunBuilder("A", "B").AttemptDaysAgo(60, 5, 5).Build();

        ComparisonResult result = Run(new BestRecentRunNode(30), run);

        Assert.True(result.Succeeded);
        Assert.Equal(new double?[] { null, null }, Seconds(result.SegmentTimes));
    }

    // ------------------------------------------------------------ file format

    private static TimesNode EveryV4Block() =>
        new RoundNode(
            new ClampNode(
                new WeightedAverageNode(new ReachedSegmentNode(History, 3), 5),
                new ComparisonSourceNode("Best Segments"),
                new BestRecentRunNode(30)),
            TimeSpan.FromSeconds(0.1));

    [Fact]
    public void Version_4_blocks_round_trip()
    {
        string once = ProgramSerializer.Write(new ProgramDocument(new ComparisonProgram("V4", EveryV4Block())));
        string twice = ProgramSerializer.Write(ProgramSerializer.Read(once));

        Assert.Equal(once, twice);
        Assert.Contains("\"version\": 4", once);
    }

    [Fact]
    public void Older_blocks_still_save_as_older_versions()
    {
        var program = new ComparisonProgram("Old", new AddPerSegmentNode(Pb, TimeSpan.FromSeconds(1)));

        Assert.Contains("\"version\": 1", ProgramSerializer.Write(new ProgramDocument(program)));
    }

    [Fact]
    public void A_version_4_block_deep_inside_needs_version_4()
    {
        var program = new ComparisonProgram("Nested", new FallbackNode(Pb, new AggregateNode(AggregateKind.Median, new ReachedSegmentNode(History, 2))));

        Assert.Contains("\"version\": 4", ProgramSerializer.Write(new ProgramDocument(program)));
    }

    [Theory]
    [InlineData("{\"type\":\"bestRecentRun\",\"days\":0}", "root.days")]
    [InlineData("{\"type\":\"round\",\"step\":0,\"source\":{\"type\":\"comparison\",\"name\":\"X\"}}", "root.step")]
    [InlineData("{\"type\":\"weightedAverage\",\"halfLife\":0,\"source\":{\"type\":\"segmentHistory\"}}", "root.halfLife")]
    [InlineData("{\"type\":\"clamp\",\"source\":{\"type\":\"comparison\",\"name\":\"X\"},\"low\":{\"type\":\"comparison\",\"name\":\"Y\"}}", "root.high")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"reachedSegment\",\"segment\":0,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.segment")]
    [InlineData("{\"type\":\"reachedSegment\",\"segment\":2,\"source\":{\"type\":\"segmentHistory\"}}", "Put it inside")]
    public void Bad_version_4_values_name_the_place(string root, string expected)
    {
        string text = "{\"format\":\"livesplit-custom-comparison\",\"version\":4,\"name\":\"T\",\"root\":" + root + "}";
        Assert.Contains(expected, Assert.Throws<ProgramFormatException>(() => ProgramSerializer.Read(text)).Message);
    }
}
