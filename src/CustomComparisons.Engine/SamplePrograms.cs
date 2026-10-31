using System;
using CustomComparisons.Engine.Ast;

namespace CustomComparisons.Engine;

/// <summary>
/// Built-in examples. A component added to a layout for the first time starts
/// with <see cref="MedianOfLast10Plus1s"/> so it does something right away.
/// </summary>
public static class SamplePrograms
{
    /// <summary>Per segment: the median of the last 10 times from completed runs, plus 1 second.</summary>
    public static ComparisonProgram MedianOfLast10Plus1s() =>
        new ComparisonProgram(
            "Median Last 10 +1s",
            new AddPerSegmentNode(
                new AggregateNode(
                    AggregateKind.Median,
                    new LastNNode(new CompletedRunsOnlyNode(new SegmentHistoryNode()), 10)),
                TimeSpan.FromSeconds(1)));

    /// <summary>
    /// Best segments + 0.5s for segments 1-3, the median of the last 10 completed runs
    /// after that, and Personal Best wherever that leaves a gap.
    /// </summary>
    public static ComparisonProgram SafePace() =>
        new ComparisonProgram(
            "Safe Pace",
            new FallbackNode(
                new SegmentRangeSwitchNode(
                    new[]
                    {
                        new SegmentRangeCase(1, 3, new AddPerSegmentNode(new ComparisonSourceNode("Best Segments"), TimeSpan.FromSeconds(0.5))),
                    },
                    new AggregateNode(
                        AggregateKind.Median,
                        new LastNNode(new CompletedRunsOnlyNode(new SegmentHistoryNode()), 10))),
                new ComparisonSourceNode("Personal Best")));
}
