using System;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using Xunit;

namespace CustomComparisons.Engine.Tests;

public class SegmentReferenceStatusTests
{
    private static readonly SamplesNode History = new SegmentHistoryNode();
    private static readonly TimesNode Pb = new ComparisonSourceNode("Personal Best");

    [Fact]
    public void Nothing_out_of_range_is_not_a_warning()
    {
        Assert.Null(SegmentReferenceStatus.Describe(null, 6));
        Assert.Null(SegmentReferenceStatus.Describe(Pb, -1));
        Assert.Null(SegmentReferenceStatus.Describe(Pb, 6));
        Assert.Null(SegmentReferenceStatus.Describe(new SegmentRangeSwitchNode(new[] { new SegmentRangeCase(1, 6, Pb) }, Pb), 6));
        Assert.Null(SegmentReferenceStatus.Describe(new SegmentRangeSwitchNode(new[] { new SegmentRangeCase(1, 3, Pb) }, Pb), 6));
        Assert.Null(SegmentReferenceStatus.Describe(new AggregateNode(AggregateKind.Median, new ReachedSegmentNode(History, 6)), 6));
        Assert.Null(SegmentReferenceStatus.Describe(new AggregateNode(AggregateKind.Median, new ReachedSegmentNode(History, 1)), 6));
    }

    [Fact]
    public void Range_that_ends_past_the_splits_names_that_end()
    {
        // Segments 4–6 still match. 9 is the number on the block that doesn't exist.
        TimesNode root = new SegmentRangeSwitchNode(new[] { new SegmentRangeCase(4, 9, Pb) }, Pb);

        Assert.Equal("Uses segment 9, but your splits have 6", SegmentReferenceStatus.Describe(root, 6));
    }

    [Fact]
    public void Range_that_starts_past_the_splits_names_the_whole_range()
    {
        TimesNode root = new SegmentRangeSwitchNode(new[] { new SegmentRangeCase(9, 12, Pb) }, Pb);

        Assert.Equal("Uses segments 9 to 12, but your splits have 6", SegmentReferenceStatus.Describe(root, 6));
    }

    [Fact]
    public void Single_segment_range_past_the_end_names_that_segment()
    {
        TimesNode root = new SegmentRangeSwitchNode(new[] { new SegmentRangeCase(9, 9, Pb) }, Pb);

        Assert.Equal("Uses segment 9, but your splits have 6", SegmentReferenceStatus.Describe(root, 6));
    }

    [Fact]
    public void Reached_segment_past_the_end_names_that_segment()
    {
        TimesNode root = new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 9));

        Assert.Equal("Uses segment 9, but your splits have 6", SegmentReferenceStatus.Describe(root, 6));
    }

    [Fact]
    public void Several_references_are_listed_and_adjacent_ones_join()
    {
        TimesNode root = new FallbackNode(
            new SegmentRangeSwitchNode(new[] { new SegmentRangeCase(4, 9, Pb) }, Pb),
            new AggregateNode(AggregateKind.Min, new ReachedSegmentNode(History, 12)));

        Assert.Equal("Uses segments 9 and 12, but your splits have 6", SegmentReferenceStatus.Describe(root, 6));

        TimesNode adjacent = new FallbackNode(
            new AggregateNode(AggregateKind.Min, new ReachedSegmentNode(History, 8)),
            new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 9)));

        Assert.Equal("Uses segments 8 to 9, but your splits have 6", SegmentReferenceStatus.Describe(adjacent, 6));

        TimesNode three = new SegmentRangeSwitchNode(
            new[]
            {
                new SegmentRangeCase(7, 7, Pb),
                new SegmentRangeCase(9, 9, Pb),
                new SegmentRangeCase(12, 12, Pb),
            },
            Pb);

        Assert.Equal("Uses segments 7, 9 and 12, but your splits have 6", SegmentReferenceStatus.Describe(three, 6));
    }

    [Fact]
    public void The_same_segment_is_not_repeated()
    {
        TimesNode root = new FallbackNode(
            new AggregateNode(AggregateKind.Min, new ReachedSegmentNode(History, 9)),
            new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(new LastNNode(History, 5), 9)));

        Assert.Equal("Uses segment 9, but your splits have 6", SegmentReferenceStatus.Describe(root, 6));
    }

    [Fact]
    public void No_splits_still_reports_the_segments_that_were_named()
    {
        TimesNode root = new AggregateNode(AggregateKind.Min, new ReachedSegmentNode(History, 1));

        Assert.Equal("Uses segment 1, but your splits have 0", SegmentReferenceStatus.Describe(root, 0));
    }

    [Fact]
    public void Nested_blocks_are_all_checked()
    {
        SamplesNode filters = new LastNNode(
            new CompletedRunsOnlyNode(
                new RecentDaysNode(
                    new TrimOutliersNode(
                        new FinishedUnderNode(
                            new AtLeastNode(new ReachedSegmentNode(History, 8), 2),
                            TimeSpan.FromMinutes(20)),
                        10),
                    30)),
            5);

        TimesNode math = new AddPerSegmentNode(
            new ScaleNode(
                new RoundNode(
                    new StretchToTotalNode(
                        new BlendNode(
                            new AggregateNode(AggregateKind.Min, filters),
                            new PercentileNode(new ReachedSegmentNode(History, 10), 70),
                            25),
                        TimeSpan.FromMinutes(20)),
                    TimeSpan.FromSeconds(0.1)),
                105),
            TimeSpan.FromSeconds(1));

        TimesNode picked = new PickNode(
            PickKind.Slower,
            new ClampNode(math, Pb, new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 18))),
            new WeightedAverageNode(new ReachedSegmentNode(History, 12), 4));

        TimesNode root = new FallbackNode(
            new SegmentRangeSwitchNode(
                new[] { new SegmentRangeCase(1, 2, picked) },
                new AggregateNode(AggregateKind.Median, new ReachedSegmentNode(History, 14))),
            new SegmentRangeSwitchNode(
                new[] { new SegmentRangeCase(20, 22, new BestRecentRunNode(30)) },
                new AggregateNode(AggregateKind.Max, new ReachedSegmentNode(History, 16))));

        Assert.Equal(
            "Uses segments 8, 10, 12, 14, 16, 18 and 20 to 22, but your splits have 6",
            SegmentReferenceStatus.Describe(root, 6));
    }
}
