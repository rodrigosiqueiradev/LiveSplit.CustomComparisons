using System;
using System.IO;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using Xunit;

namespace CustomComparisons.Engine.Tests;

public class SerializerTests
{
    private static string Wrap(string root, string name = "Test") =>
        "{\"format\":\"livesplit-custom-comparison\",\"version\":1,\"name\":\"" + name + "\",\"root\":" + root + "}";

    private static string ErrorFor(string text) =>
        Assert.Throws<ProgramFormatException>(() => ProgramSerializer.Read(text)).Message;

    [Fact]
    public void Every_block_survives_a_round_trip()
    {
        // One program that uses every block type.
        ComparisonProgram program = SamplePrograms.SafePace();
        var everything = new ComparisonProgram(
            "Everything",
            new FallbackNode(
                new ScaleNode(new AggregateNode(AggregateKind.Average, new SegmentHistoryNode()), 97.5),
                new AggregateNode(AggregateKind.Max, new LastNNode(new SegmentHistoryNode(), 3))));

        foreach (ComparisonProgram p in new[] { program, everything, SamplePrograms.MedianOfLast10Plus1s() })
        {
            string once = ProgramSerializer.Write(new ProgramDocument(p));
            string twice = ProgramSerializer.Write(ProgramSerializer.Read(once));
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void A_read_program_evaluates_the_same_as_the_original()
    {
        RunData run = new RunBuilder("A", "B", "C", "D")
            .Attempt(10, 20, 30, 40)
            .Attempt(12, 18, 33, 41)
            .Comparison("Best Segments", 10, 28, 58, 98)
            .Comparison("Personal Best", 10, 30, 60, 100)
            .Build();

        ComparisonProgram original = SamplePrograms.SafePace();
        ComparisonProgram reloaded = ProgramSerializer.Read(ProgramSerializer.Write(new ProgramDocument(original))).Program;

        ComparisonResult a = new ComparisonEvaluator(new[] { original }).Evaluate(original.Name, run);
        ComparisonResult b = new ComparisonEvaluator(new[] { reloaded }).Evaluate(reloaded.Name, run);
        Assert.Equal(a.SplitTimes, b.SplitTimes);
    }

    [Fact]
    public void The_editor_workspace_is_kept_but_not_interpreted()
    {
        string text = "{\"format\":\"livesplit-custom-comparison\",\"version\":1,\"name\":\"Test\"," +
            "\"root\":{\"type\":\"comparison\",\"name\":\"Personal Best\"}," +
            "\"workspace\":{\"blocks\":[{\"x\":10,\"y\":-4.5,\"note\":\"hi \\\"there\\\"\"}]}}";

        ProgramDocument doc = ProgramSerializer.Read(text);
        string written = ProgramSerializer.Write(doc, indented: false);

        Assert.Contains("\"workspace\":{\"blocks\":[{\"x\":10,\"y\":-4.5,\"note\":\"hi \\\"there\\\"\"}]}", written);
    }

    [Theory]
    [InlineData("examples/safe-pace.json", "Safe Pace")]
    [InlineData("examples/median-last-10-plus-1s.json", "Median Last 10 +1s")]
    [InlineData("examples/golds-plus-3-percent.json", "Golds +3%")]
    public void Shipped_example_files_load(string path, string name)
    {
        ProgramDocument doc = ProgramSerializer.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path)));
        Assert.Equal(name, doc.Program.Name);
    }

    [Fact]
    public void Example_files_match_the_built_in_samples()
    {
        string file = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples/safe-pace.json"));
        Assert.Equal(
            ProgramSerializer.Write(new ProgramDocument(SamplePrograms.SafePace())),
            ProgramSerializer.Write(ProgramSerializer.Read(file)));
    }

    [Fact]
    public void Wrong_kind_of_block_in_a_slot_explains_the_types()
    {
        // An aggregate needs a list of times, not a single time per segment.
        string message = ErrorFor(Wrap("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"comparison\",\"name\":\"Personal Best\"}}"));
        Assert.StartsWith("root.source:", message);
        Assert.Contains("needs a list of times", message);

        // And a list of times can't be the result on its own.
        Assert.Contains("Put it inside a min, max, average, median or percentile block", ErrorFor(Wrap("{\"type\":\"segmentHistory\"}")));
    }

    [Theory]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"lastN\",\"count\":0,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.count")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"lastN\",\"count\":2.5,\"source\":{\"type\":\"segmentHistory\"}}}", "root.source.count")]
    [InlineData("{\"type\":\"aggregate\",\"kind\":\"mode\",\"source\":{\"type\":\"segmentHistory\"}}", "root.kind")]
    [InlineData("{\"type\":\"scale\",\"percent\":0,\"source\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.percent")]
    [InlineData("{\"type\":\"addTime\",\"seconds\":\"1\",\"source\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.seconds")]
    [InlineData("{\"type\":\"fallback\",\"primary\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.fallback")]
    [InlineData("{\"type\":\"segmentRange\",\"cases\":[{\"from\":4,\"to\":2,\"times\":{\"type\":\"comparison\",\"name\":\"PB\"}}],\"otherwise\":{\"type\":\"comparison\",\"name\":\"PB\"}}", "root.cases[0].to")]
    [InlineData("{\"type\":\"script\",\"code\":\"anything\"}", "unknown block \"script\"")]
    public void Bad_values_name_the_exact_place(string root, string expected)
    {
        Assert.Contains(expected, ErrorFor(Wrap(root)));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"format\":\"something-else\",\"version\":1}")]
    [InlineData("[1,2,3]")]
    public void Other_files_are_rejected_politely(string text)
    {
        string message = ErrorFor(text);
        Assert.True(message.Contains("isn't valid JSON") || message.Contains("isn't a Custom Comparisons file"), message);
    }

    [Fact]
    public void Files_from_a_newer_version_ask_for_an_update()
    {
        string text = Wrap("{\"type\":\"comparison\",\"name\":\"PB\"}").Replace("\"version\":1", "\"version\":99");
        Assert.Contains("newer version", ErrorFor(text));
    }

    [Fact]
    public void Deeply_nested_files_are_rejected_without_crashing()
    {
        string deep = string.Concat(Enumerable.Repeat("{\"type\":\"scale\",\"percent\":100,\"source\":", 500))
            + "{\"type\":\"comparison\",\"name\":\"PB\"}" + new string('}', 500);
        Assert.Contains("nested too deeply", ErrorFor(Wrap(deep)));
    }

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        Assert.Contains("appears twice", ErrorFor("{\"format\":\"livesplit-custom-comparison\",\"format\":\"x\"}"));
    }
}
