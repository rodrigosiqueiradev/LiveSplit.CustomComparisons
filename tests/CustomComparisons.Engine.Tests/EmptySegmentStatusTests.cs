using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using Xunit;

namespace CustomComparisons.Engine.Tests;

public class EmptySegmentStatusTests
{
    private static string Describe(string[] names, params double?[] seconds)
    {
        TimeSpan?[] times = seconds.Select(s => s.HasValue ? (TimeSpan?)TimeSpan.FromSeconds(s.Value) : null).ToArray();
        return EmptySegmentStatus.Describe(names, times);
    }

    [Fact]
    public void Every_segment_filled_is_not_a_warning()
    {
        Assert.Null(Describe(new[] { "Intro", "Caves" }, 10, 20));
    }

    [Fact]
    public void A_zero_time_is_still_a_time()
    {
        Assert.Null(Describe(new[] { "Intro", "Caves" }, 0, 20));
    }

    [Fact]
    public void No_segments_is_not_a_warning()
    {
        Assert.Null(EmptySegmentStatus.Describe(new string[0], new TimeSpan?[0]));
        Assert.Null(EmptySegmentStatus.Describe(null, null));
    }

    [Fact]
    public void Partial_names_the_first_empty_segment_and_counts_only_empty_ones()
    {
        // C has its own segment time. Its split would be blank because B is empty,
        // and that still must not count C as empty.
        Assert.Equal(
            "Working \u2014 no times for 1 of 3 segments (first: B)",
            Describe(new[] { "A", "B", "C" }, 10, null, 5));
    }

    [Fact]
    public void Several_gaps_count_every_gap_and_name_the_earliest()
    {
        Assert.Equal(
            "Working \u2014 no times for 2 of 4 segments (first: Caves)",
            Describe(new[] { "Intro", "Forest", "Caves", "Castle" }, 10, 20, null, null));
    }

    [Fact]
    public void Times_missing_off_the_end_count_as_empty()
    {
        Assert.Equal(
            "Working \u2014 no times for 2 of 4 segments (first: Caves)",
            Describe(new[] { "Intro", "Forest", "Caves", "Castle" }, 10, 20));
    }

    [Fact]
    public void A_blank_name_is_reported_by_segment_number()
    {
        Assert.Equal(
            "Working \u2014 no times for 2 of 3 segments (first: segment 2)",
            Describe(new[] { "Intro", "  ", "Castle" }, 10, null, null));
        Assert.Equal(
            "Working \u2014 no times for 1 of 2 segments (first: segment 1)",
            EmptySegmentStatus.Describe(new string[] { null, "Castle" }, new TimeSpan?[] { null, TimeSpan.FromSeconds(5) }));
    }

    [Fact]
    public void Every_segment_empty_says_no_times_at_all()
    {
        Assert.Equal("No times at all", Describe(new[] { "Intro", "Caves" }, null, null));
        Assert.Equal("No times at all", Describe(new[] { "Only" }, new double?[] { null }));
        Assert.Equal("No times at all", EmptySegmentStatus.Describe(new[] { "Intro", "Caves" }, null));
    }

    [Fact]
    public void A_run_that_resets_early_names_the_first_segment_with_no_samples()
    {
        RunData run = new RunBuilder("Intro", "Forest", "Caves", "Castle")
            .Attempt(10, 20)
            .Attempt(12, 22)
            .Build();
        ComparisonResult result = Evaluate(new AggregateNode(AggregateKind.Median, new SegmentHistoryNode()), run);

        Assert.Null(result.Error);
        // Splits stop at Forest, but only Caves and Castle have no segment time.
        Assert.Equal(new double?[] { 11, 21, null, null }, Seconds(result.SegmentTimes));
        Assert.Equal(
            "Working \u2014 no times for 2 of 4 segments (first: Caves)",
            EmptySegmentStatus.Describe(run.Segments.Select(s => s.Name).ToList(), result.SegmentTimes));
    }

    [Fact]
    public void A_run_with_no_history_has_no_times_at_all()
    {
        RunData run = new RunBuilder("Intro", "Forest", "Caves").Build();
        ComparisonResult result = Evaluate(new AggregateNode(AggregateKind.Median, new SegmentHistoryNode()), run);

        Assert.Null(result.Error);
        Assert.Equal("No times at all", EmptySegmentStatus.Describe(run.Segments.Select(s => s.Name).ToList(), result.SegmentTimes));
    }

    [Fact]
    public void A_full_history_has_nothing_to_warn_about()
    {
        RunData run = new RunBuilder("Intro", "Forest").Attempt(10, 20).Attempt(14, 16).Build();
        ComparisonResult result = Evaluate(new AggregateNode(AggregateKind.Median, new SegmentHistoryNode()), run);

        Assert.Null(result.Error);
        Assert.Null(EmptySegmentStatus.Describe(run.Segments.Select(s => s.Name).ToList(), result.SegmentTimes));
    }

    private static ComparisonResult Evaluate(TimesNode root, RunData run)
    {
        var program = new ComparisonProgram("Test", root);
        return new ComparisonEvaluator(new[] { program }).Evaluate("Test", run);
    }

    private static double?[] Seconds(IEnumerable<TimeSpan?> times) =>
        times.Select(t => t.HasValue ? (double?)t.Value.TotalSeconds : null).ToArray();
}
