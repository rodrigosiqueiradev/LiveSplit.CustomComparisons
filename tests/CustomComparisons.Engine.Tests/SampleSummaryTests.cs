using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using Xunit;

namespace CustomComparisons.Engine.Tests;

/// <summary>
/// Hexagon blocks previewed on their own: a count and fastest/middle/slowest per segment,
/// using the same median as the median aggregate.
/// </summary>
public class SampleSummaryTests
{
    private static readonly SamplesNode History = new SegmentHistoryNode();

    private static IReadOnlyList<SampleSegmentSummary> Summarize(SamplesNode node, RunData run) =>
        new ComparisonEvaluator(Enumerable.Empty<ComparisonProgram>()).SummarizeSamples(node, run);

    [Fact]
    public void History_reports_count_and_the_same_min_median_max_as_aggregates()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 20)
            .Attempt(30, 40)
            .Attempt(20, 10)
            .Attempt(99) // reset after A: a fourth sample for A only
            .Build();

        IReadOnlyList<SampleSegmentSummary> summaries = Summarize(History, run);

        // A: {10, 30, 20, 99}. Median matches AggregateKind.Median: (20+30)/2 = 25.
        AssertSegment(summaries[0], 4, 10, 25, 99);
        // B: {20, 40, 10}.
        AssertSegment(summaries[1], 3, 10, 20, 40);
    }

    [Fact]
    public void LastN_counts_only_the_times_it_keeps()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 20)
            .Attempt(30, 40)
            .Attempt(20, 10)
            .Attempt(99)
            .Build();

        IReadOnlyList<SampleSegmentSummary> summaries = Summarize(new LastNNode(History, 2), run);

        // A's last two completions are 20 and 99. B's last two are 40 and 10.
        AssertSegment(summaries[0], 2, 20, 59.5, 99);
        AssertSegment(summaries[1], 2, 10, 25, 40);
    }

    [Fact]
    public void Completed_runs_drop_resets_before_the_summary()
    {
        RunData run = new RunBuilder("A", "B")
            .Attempt(10, 10)
            .Attempt(1) // reset: not a completed run
            .Attempt(20, 30)
            .Build();

        IReadOnlyList<SampleSegmentSummary> summaries = Summarize(new CompletedRunsOnlyNode(History), run);

        AssertSegment(summaries[0], 2, 10, 15, 20);
        AssertSegment(summaries[1], 2, 10, 20, 30);
    }

    [Fact]
    public void A_filter_that_keeps_nothing_reports_a_zero_count()
    {
        RunData run = new RunBuilder("A").Attempt(10).Attempt(20).Build();

        SampleSegmentSummary summary = Summarize(new AtLeastNode(History, 5), run)[0];

        Assert.Equal(0, summary.Count);
        Assert.Null(summary.Min);
        Assert.Null(summary.Median);
        Assert.Null(summary.Max);
    }

    private static void AssertSegment(SampleSegmentSummary summary, int count, double min, double median, double max)
    {
        Assert.Equal(count, summary.Count);
        Assert.Equal(TimeSpan.FromSeconds(min), summary.Min);
        Assert.Equal(TimeSpan.FromSeconds(median), summary.Median);
        Assert.Equal(TimeSpan.FromSeconds(max), summary.Max);
    }
}
