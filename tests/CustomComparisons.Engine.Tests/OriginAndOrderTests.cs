using System;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Library;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using Xunit;

namespace CustomComparisons.Engine.Tests;

public class OriginAndOrderTests
{
    private static ComparisonResult Traced(TimesNode root, RunData run) =>
        new ComparisonEvaluator(new[] { new ComparisonProgram("Test", root) }).EvaluateWithOrigins("Test", run);

    [Fact]
    public void Origins_follow_fallback_and_segment_range_choices()
    {
        RunData run = new RunBuilder("A", "B", "C", "D")
            .Attempt(10, 20)                       // C and D never reached: no history there
            .Comparison("Best Segments", 9, 28, 57, 96)
            .Comparison("Personal Best", 10, 30, 60, 100)
            .Build();

        // Safe Pace: golds +0.5 for 1-3, median otherwise, PB where empty.
        ComparisonResult result = Traced(SamplePrograms.SafePace().Root, run);

        Assert.Equal(
            new[]
            {
                "primary.cases.0.times.source",   // A: golds (+0.5 looked through to its source)
                "primary.cases.0.times.source",   // B
                "primary.cases.0.times.source",   // C
                "fallback",                       // D: no history, so PB filled it
            },
            result.Origins);
    }

    [Fact]
    public void Origins_follow_the_side_faster_slower_picked()
    {
        RunData run = new RunBuilder("A", "B")
            .Comparison("X", 10, 30)   // segments 10, 20
            .Comparison("Y", 12, 27)   // segments 12, 15
            .Build();

        ComparisonResult result = Traced(new PickNode(PickKind.Slower, new ComparisonSourceNode("X"), new ComparisonSourceNode("Y")), run);

        Assert.Equal(new[] { "second", "first" }, result.Origins);
    }

    [Fact]
    public void Traced_and_plain_evaluation_give_the_same_times()
    {
        RunData run = new RunBuilder("A", "B", "C", "D")
            .Attempt(10, 20, 30, 40).Attempt(12, 18, 33, 41)
            .Comparison("Best Segments", 9, 27, 57, 96)
            .Comparison("Personal Best", 10, 30, 60, 100)
            .Build();
        var program = SamplePrograms.SafePace();
        var evaluator = new ComparisonEvaluator(new[] { program });

        Assert.Equal(evaluator.Evaluate(program.Name, run).SplitTimes, evaluator.EvaluateWithOrigins(program.Name, run).SplitTimes);
    }

    [Fact]
    public void Plain_evaluation_has_no_origins()
    {
        RunData run = new RunBuilder("A").Attempt(10).Build();
        var program = new ComparisonProgram("P", new AggregateNode(AggregateKind.Min, new SegmentHistoryNode()));

        Assert.Null(new ComparisonEvaluator(new[] { program }).Evaluate("P", run).Origins);
        Assert.Equal(new[] { "" }, new ComparisonEvaluator(new[] { program }).EvaluateWithOrigins("P", run).Origins);
    }

    [Fact]
    public void Move_reorders_and_stops_at_the_ends()
    {
        var library = new ComparisonLibrary();
        foreach (string name in new[] { "A", "B", "C" })
        {
            library.Add(new ProgramDocument(new ComparisonProgram(name, new ComparisonSourceNode("Personal Best"))), new string[0]);
        }

        Assert.True(library.Move(library.Entries[2], -1));
        Assert.Equal(new[] { "A", "C", "B" }, library.Entries.Select(e => e.Name));

        Assert.False(library.Move(library.Entries[0], -1));
        Assert.True(library.Move(library.Entries[0], 5));
        Assert.Equal(new[] { "C", "B", "A" }, library.Entries.Select(e => e.Name));
    }
}
