using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using Xunit;

namespace CustomComparisons.Engine.Tests;

/// <summary>Version-2 blocks: percentile, last N days, stretch to total, blend, faster/slower.</summary>
public class NewBlockTests
{
    private static readonly SamplesNode History = new SegmentHistoryNode();

    private static ComparisonResult Run(TimesNode root, RunData run) =>
        new ComparisonEvaluator(new[] { new ComparisonProgram("Test", root) }).Evaluate("Test", run);

    private static double?[] Seconds(IEnumerable<TimeSpan?> times) =>
        times.Select(t => t.HasValue ? (double?)Math.Round(t.Value.TotalSeconds, 6) : null).ToArray();

    // ------------------------------------------------------------ percentile

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 40)]
    [InlineData(50, 25)]    // equals the median of 10, 20, 30, 40
    [InlineData(70, 31)]    // rank 2.1: 30 + 0.1 * (40 - 30)
    public void Percentile_interpolates_between_neighbours(double percent, double expected)
    {
        RunData run = new RunBuilder("A").Attempt(40).Attempt(10).Attempt(30).Attempt(20).Build();

        Assert.Equal(new double?[] { expected }, Seconds(Run(new PercentileNode(History, percent), run).SegmentTimes));
    }

    [Fact]
    public void Percentile_50_matches_the_median_block()
    {
        RunData run = new RunBuilder("A", "B").Attempt(11, 5).Attempt(14, 9).Attempt(12, 7).Attempt(19, 8).Build();

        Assert.Equal(
            Seconds(Run(new AggregateNode(AggregateKind.Median, History), run).SegmentTimes),
            Seconds(Run(new PercentileNode(History, 50), run).SegmentTimes));
    }

    // ------------------------------------------------------------ last N days

    [Fact]
    public void Recent_days_keeps_only_attempts_inside_the_window()
    {
        RunData run = new RunBuilder("A")
            .AttemptDaysAgo(40, 100)  // too old
            .AttemptDaysAgo(20, 30)
            .AttemptDaysAgo(1, 10)
            .Attempt(5)               // no date recorded: left out
            .Build();

        TimesNode root = new AggregateNode(AggregateKind.Max, new RecentDaysNode(History, 30));

        Assert.Equal(new double?[] { 30 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Recent_days_with_nothing_recent_is_empty()
    {
        RunData run = new RunBuilder("A").AttemptDaysAgo(90, 10).Build();

        Assert.Equal(new double?[] { null }, Seconds(Run(new AggregateNode(AggregateKind.Min, new RecentDaysNode(History, 7)), run).SegmentTimes));
    }

    // ------------------------------------------------------------ stretch to total

    [Fact]
    public void Stretch_scales_every_segment_to_hit_the_goal_exactly()
    {
        // Golds total 90s; stretched to 120s every segment grows by 4/3.
        RunData run = new RunBuilder("A", "B", "C").Comparison("Best Segments", 30, 60, 90).Build();

        ComparisonResult result = Run(new StretchToTotalNode(new ComparisonSourceNode("Best Segments"), TimeSpan.FromSeconds(120)), run);

        Assert.Equal(new double?[] { 40, 40, 40 }, Seconds(result.SegmentTimes));
        Assert.Equal(TimeSpan.FromSeconds(120), result.SplitTimes.Last());
    }

    [Fact]
    public void Stretch_total_is_exact_to_the_tick_despite_rounding()
    {
        RunData run = new RunBuilder("A", "B", "C").Comparison("Best Segments", 1, 2, 3).Build();

        ComparisonResult result = Run(new StretchToTotalNode(new ComparisonSourceNode("Best Segments"), TimeSpan.FromTicks(10_000_001)), run);

        Assert.Equal(10_000_001, result.SplitTimes.Last().Value.Ticks);
    }

    [Fact]
    public void Stretch_with_an_empty_segment_is_empty_everywhere()
    {
        RunData run = new RunBuilder("A", "B").Comparison("Personal Best", 30, null).Build();

        ComparisonResult result = Run(new StretchToTotalNode(new ComparisonSourceNode("Personal Best"), TimeSpan.FromSeconds(60)), run);

        Assert.All(result.SegmentTimes, t => Assert.Null(t));
    }

    // ------------------------------------------------------------ blend

    [Theory]
    [InlineData(0, 10, 20)]
    [InlineData(100, 30, 40)]
    [InlineData(75, 25, 35)]
    public void Blend_moves_from_the_first_rule_toward_the_second(double percent, double a, double b)
    {
        RunData run = new RunBuilder("A", "B")
            .Comparison("Best Segments", 10, 30)   // segments 10, 20
            .Comparison("Personal Best", 30, 70)   // segments 30, 40
            .Build();

        TimesNode root = new BlendNode(new ComparisonSourceNode("Best Segments"), new ComparisonSourceNode("Personal Best"), percent);

        Assert.Equal(new double?[] { a, b }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Blend_is_empty_where_either_side_is_empty()
    {
        RunData run = new RunBuilder("A", "B")
            .Comparison("Best Segments", 10, 30)
            .Comparison("Personal Best", null, 70)
            .Build();

        TimesNode root = new BlendNode(new ComparisonSourceNode("Best Segments"), new ComparisonSourceNode("Personal Best"), 50);

        Assert.Equal(new double?[] { null, null }, Seconds(Run(root, run).SegmentTimes));
    }

    // ------------------------------------------------------------ faster / slower

    [Theory]
    [InlineData(PickKind.Faster, 10, 15)]
    [InlineData(PickKind.Slower, 12, 20)]
    public void Pick_chooses_per_segment(PickKind kind, double a, double b)
    {
        RunData run = new RunBuilder("A", "B")
            .Comparison("X", 10, 30)   // segments 10, 20
            .Comparison("Y", 12, 27)   // segments 12, 15
            .Build();

        TimesNode root = new PickNode(kind, new ComparisonSourceNode("X"), new ComparisonSourceNode("Y"));

        Assert.Equal(new double?[] { a, b }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Pick_uses_the_other_side_where_one_is_empty()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Comparison("X", 10, null, null)  // segments 10, -, -
            .Comparison("Y", 12, 25, null)    // segments 12, 13, -
            .Build();

        TimesNode root = new PickNode(PickKind.Slower, new ComparisonSourceNode("X"), new ComparisonSourceNode("Y"));

        Assert.Equal(new double?[] { 12, 13, null }, Seconds(Run(root, run).SegmentTimes));
    }

    // ------------------------------------------------------------ file format

    private static TimesNode EveryNewBlock() =>
        new PickNode(
            PickKind.Slower,
            new StretchToTotalNode(
                new BlendNode(new ComparisonSourceNode("Best Segments"), new ComparisonSourceNode("Personal Best"), 75),
                TimeSpan.FromSeconds(1200)),
            new PercentileNode(new RecentDaysNode(new SegmentHistoryNode(), 30), 70));

    [Fact]
    public void New_blocks_survive_a_round_trip()
    {
        string once = ProgramSerializer.Write(new ProgramDocument(new ComparisonProgram("New", EveryNewBlock())));
        string twice = ProgramSerializer.Write(ProgramSerializer.Read(once));

        Assert.Equal(once, twice);
        Assert.Contains("\"type\": \"stretchToTotal\"", once);
        Assert.Contains("\"totalSeconds\": 1200", once);
    }

    [Fact]
    public void Files_only_need_version_2_when_they_use_new_blocks()
    {
        string old = ProgramSerializer.Write(new ProgramDocument(SamplePrograms.SafePace()));
        string fresh = ProgramSerializer.Write(new ProgramDocument(new ComparisonProgram("New", EveryNewBlock())));

        Assert.Contains("\"version\": 1", old);
        Assert.Contains("\"version\": 2", fresh);
    }

    [Theory]
    [InlineData("{\"type\":\"percentile\",\"percent\":101,\"source\":{\"type\":\"segmentHistory\"}}", "root.percent")]
    [InlineData("{\"type\":\"stretchToTotal\",\"totalSeconds\":0,\"source\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.totalSeconds")]
    [InlineData("{\"type\":\"blend\",\"percent\":50,\"from\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.to")]
    [InlineData("{\"type\":\"pick\",\"kind\":\"fastest\",\"first\":{\"type\":\"comparison\",\"name\":\"PB\"},\"second\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.kind")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"recentDays\",\"days\":0,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.days")]
    [InlineData("{\"type\":\"recentDays\",\"days\":7,\"source\":{\"type\":\"segmentHistory\"}}", "Put it inside")]
    public void Bad_new_block_values_name_the_place(string root, string expected)
    {
        string text = "{\"format\":\"livesplit-custom-comparison\",\"version\":2,\"name\":\"T\",\"root\":" + root + "}";
        Assert.Contains(expected, Assert.Throws<ProgramFormatException>(() => ProgramSerializer.Read(text)).Message);
    }
}
