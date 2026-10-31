using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using Xunit;

namespace CustomComparisons.Engine.Tests;

public class EvaluatorTests
{
    private static readonly SamplesNode History = new SegmentHistoryNode();

    private static ComparisonResult Run(TimesNode root, RunData run, params ComparisonProgram[] others)
    {
        var program = new ComparisonProgram("Test", root);
        return new ComparisonEvaluator(new[] { program }.Concat(others)).Evaluate("Test", run);
    }

    private static double?[] Seconds(IEnumerable<TimeSpan?> times) =>
        times.Select(t => t.HasValue ? (double?)t.Value.TotalSeconds : null).ToArray();

    [Fact]
    public void Median_uses_middle_value_or_mean_of_two_middles()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 20)
            .Attempt(30, 40)
            .Attempt(20, 10)
            .Attempt(99) // reset after A: adds a 4th sample to A only
            .Build();

        ComparisonResult result = Run(new AggregateNode(AggregateKind.Median, History), run);

        Assert.True(result.Succeeded);
        // A: {10,30,20,99} -> (20+30)/2 = 25. B: {20,40,10} -> 20.
        Assert.Equal(new double?[] { 25, 20 }, Seconds(result.SegmentTimes));
        Assert.Equal(new double?[] { 25, 45 }, Seconds(result.SplitTimes));
    }

    [Theory]
    [InlineData(AggregateKind.Min, 10)]
    [InlineData(AggregateKind.Max, 40)]
    [InlineData(AggregateKind.Average, 25)]
    public void Other_aggregates(AggregateKind kind, double expected)
    {
        RunData run = new RunBuilder("A").Attempt(10).Attempt(40).Attempt(20).Attempt(30).Build();

        ComparisonResult result = Run(new AggregateNode(kind, History), run);

        Assert.Equal(new double?[] { expected }, Seconds(result.SegmentTimes));
    }

    [Fact]
    public void LastN_counts_completions_of_each_segment_not_attempts()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(100, 100)
            .Attempt(1, 2)
            .Attempt(3)   // resets: B gets nothing from this attempt
            .Attempt(5)
            .Build();

        ComparisonResult result = Run(new AggregateNode(AggregateKind.Max, new LastNNode(History, 2)), run);

        // A's last two completions: attempts 3 and 4 -> max 5.
        // B's last two completions: attempts 1 and 2 -> max 100 (the reset attempts don't count).
        Assert.Equal(new double?[] { 5, 100 }, Seconds(result.SegmentTimes));
    }

    [Fact]
    public void CompletedRunsOnly_drops_times_from_reset_attempts()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 10)
            .Attempt(1)          // reset
            .Attempt(20, 30)
            .Build();

        ComparisonResult result = Run(new AggregateNode(AggregateKind.Min, new CompletedRunsOnlyNode(History)), run);

        Assert.Equal(new double?[] { 10, 10 }, Seconds(result.SegmentTimes));
    }

    [Fact]
    public void Filter_order_matters_and_both_orders_work()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 10)
            .Attempt(1)   // reset, most recent
            .Build();

        TimesNode completedThenLast = new AggregateNode(AggregateKind.Min, new LastNNode(new CompletedRunsOnlyNode(History), 1));
        TimesNode lastThenCompleted = new AggregateNode(AggregateKind.Min, new CompletedRunsOnlyNode(new LastNNode(History, 1)));

        Assert.Equal(new double?[] { 10, 10 }, Seconds(Run(completedThenLast, run).SegmentTimes));
        // A's last time is from the reset attempt, which is then filtered out -> empty.
        Assert.Equal(new double?[] { null, 10 }, Seconds(Run(lastThenCompleted, run).SegmentTimes));
    }

    [Fact]
    public void Skipped_segment_and_the_combined_segment_after_it_are_ignored()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Attempt(10, null, 50) // B skipped, so C's 50 covers B and C together
            .Attempt(12, 20, 30)
            .Build();

        ComparisonResult result = Run(new AggregateNode(AggregateKind.Max, History), run);

        Assert.Equal(new double?[] { 12, 20, 30 }, Seconds(result.SegmentTimes));
    }

    [Fact]
    public void A_segment_without_data_empties_every_later_split()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Attempt(10, 20)  // never reached C
            .Build();

        ComparisonResult result = Run(new AggregateNode(AggregateKind.Min, History), run);

        Assert.Equal(new double?[] { 10, 20, null }, Seconds(result.SegmentTimes));
        Assert.Equal(new double?[] { 10, 30, null }, Seconds(result.SplitTimes));
    }

    [Fact]
    public void Fallback_fills_only_the_gaps()
    {
        RunData run = new RunBuilder("A", "B", "C")
            .Attempt(10, 20)
            .Comparison("Best Segments", 5, 10, 18) // segments 5, 5, 8
            .Build();

        TimesNode root = new FallbackNode(
            new AggregateNode(AggregateKind.Min, History),
            new ComparisonSourceNode("Best Segments"));

        Assert.Equal(new double?[] { 10, 20, 8 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Comparison_source_turns_splits_into_segments_and_empties_around_gaps()
    {
        RunData run = new RunBuilder("A", "B", "C", "D")
            .Comparison("Personal Best", 10, null, 40, 55)
            .Build();

        ComparisonResult result = Run(new ComparisonSourceNode("Personal Best"), run);

        Assert.Equal(new double?[] { 10, null, null, 15 }, Seconds(result.SegmentTimes));
    }

    [Fact]
    public void Segment_range_switch_picks_first_matching_case_then_otherwise()
    {
        RunData run = new RunBuilder("A", "B", "C", "D")
            .Attempt(10, 10, 10, 10)
            .Attempt(20, 20, 20, 20)
            .Build();

        TimesNode min = new AggregateNode(AggregateKind.Min, History);
        TimesNode max = new AggregateNode(AggregateKind.Max, History);
        TimesNode avg = new AggregateNode(AggregateKind.Average, History);

        TimesNode root = new SegmentRangeSwitchNode(
            new[]
            {
                new SegmentRangeCase(1, 2, min),
                new SegmentRangeCase(2, 3, max), // segment 2 already taken by the first case
                new SegmentRangeCase(9, 12, min), // past the end: never matches, not an error
            },
            avg);

        Assert.Equal(new double?[] { 10, 10, 20, 15 }, Seconds(Run(root, run).SegmentTimes));
    }

    [Fact]
    public void Add_and_scale_apply_per_segment_and_never_go_negative()
    {
        RunData run = new RunBuilder("A", "B").Attempt(10, 0.5).Build();
        TimesNode best = new AggregateNode(AggregateKind.Min, History);

        Assert.Equal(new double?[] { 11, 1.5 }, Seconds(Run(new AddPerSegmentNode(best, TimeSpan.FromSeconds(1)), run).SegmentTimes));
        Assert.Equal(new double?[] { 9, 0 }, Seconds(Run(new AddPerSegmentNode(best, TimeSpan.FromSeconds(-1)), run).SegmentTimes));
        Assert.Equal(new double?[] { 11, 0.55 }, Seconds(Run(new ScaleNode(best, 110), run).SegmentTimes));
    }

    [Fact]
    public void Programs_can_build_on_other_programs()
    {
        RunData run = new RunBuilder("A", "B").Attempt(10, 20).Build();
        var basePace = new ComparisonProgram("Base", new AggregateNode(AggregateKind.Min, History));

        ComparisonResult result = Run(new AddPerSegmentNode(new ComparisonSourceNode("Base"), TimeSpan.FromSeconds(2)), run, basePace);

        Assert.Equal(new double?[] { 12, 22 }, Seconds(result.SegmentTimes));
    }

    [Fact]
    public void Cycles_are_reported_and_produce_empty_comparisons()
    {
        RunData run = new RunBuilder("A").Attempt(10).Build();
        var a = new ComparisonProgram("Pace A", new ComparisonSourceNode("Pace B"));
        var b = new ComparisonProgram("Pace B", new ComparisonSourceNode("Pace A"));
        var evaluator = new ComparisonEvaluator(new[] { a, b });

        ComparisonResult result = evaluator.Evaluate("Pace A", run);

        Assert.False(result.Succeeded);
        Assert.Contains("Pace A → Pace B → Pace A", result.Error);
        Assert.All(result.SplitTimes, t => Assert.Null(t));
    }

    [Fact]
    public void One_broken_program_does_not_break_the_others()
    {
        RunData run = new RunBuilder("A").Attempt(10).Build();
        var good = new ComparisonProgram("Good", new AggregateNode(AggregateKind.Min, History));
        var bad = new ComparisonProgram("Bad", new ComparisonSourceNode("Does Not Exist"));

        IReadOnlyDictionary<string, ComparisonResult> results = new ComparisonEvaluator(new[] { good, bad }).EvaluateAll(run);

        Assert.True(results["Good"].Succeeded);
        Assert.Contains("Does Not Exist", results["Bad"].Error);
    }

    [Fact]
    public void Duplicate_program_names_are_rejected()
    {
        var one = new ComparisonProgram("Same", new SegmentRangeSwitchNode(Array.Empty<SegmentRangeCase>(), new ComparisonSourceNode("PB")));
        var two = new ComparisonProgram("Same", new ComparisonSourceNode("PB"));

        Assert.Throws<ArgumentException>(() => new ComparisonEvaluator(new[] { one, two }));
    }

    [Fact]
    public void Milestone_program_median_of_last_10_completed_plus_one_second()
    {
        var builder = new RunBuilder("A", "B");
        for (int i = 1; i <= 12; i++)
        {
            builder.Attempt(i, 100 + i); // A: 1..12, B: 101..112
        }

        builder.Attempt(0.1); // a reset, excluded by "completed runs only"
        RunData run = builder.Build();
        ComparisonProgram program = SamplePrograms.MedianOfLast10Plus1s();

        ComparisonResult result = new ComparisonEvaluator(new[] { program }).Evaluate(program.Name, run);

        // Last 10 completed: A 3..12 -> median 7.5, B 103..112 -> 107.5. Plus 1s each.
        Assert.Equal(new double?[] { 8.5, 108.5 }, Seconds(result.SegmentTimes));
        Assert.Equal(new double?[] { 8.5, 117 }, Seconds(result.SplitTimes));
    }
}
