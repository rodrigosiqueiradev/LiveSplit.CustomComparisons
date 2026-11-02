using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Library;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using Xunit;

namespace CustomComparisons.Engine.Tests;

public class LibraryTests
{
    private static string Json(ComparisonProgram p) => ProgramSerializer.Write(new ProgramDocument(p), indented: false);

    private static ProgramDocument Named(string name) =>
        new(new ComparisonProgram(name, new ComparisonSourceNode("Personal Best")));

    [Fact]
    public void Unreadable_entries_are_kept_exactly_and_saved_back()
    {
        var library = new ComparisonLibrary();
        string broken = "{\"format\":\"livesplit-custom-comparison\",\"version\":99,\"name\":\"From The Future\",\"root\":{}}";

        library.Load(new[] { Json(SamplePrograms.SafePace()), broken });

        Assert.Single(library.Programs);
        StoredComparison entry = library.Entries[1];
        Assert.False(entry.IsReadable);
        Assert.Equal("From The Future", entry.Name);
        Assert.Contains("newer version", entry.LoadError);
        Assert.Equal(broken, library.SaveAll().Last());
    }

    [Fact]
    public void Duplicate_names_in_a_layout_flag_the_second_one()
    {
        var library = new ComparisonLibrary();
        library.Load(new[] { Json(SamplePrograms.SafePace()), Json(SamplePrograms.SafePace()) });

        Assert.Single(library.Programs);
        Assert.Contains("already called", library.Entries[1].LoadError);
    }

    [Fact]
    public void Renaming_one_of_two_duplicates_makes_both_work()
    {
        var library = new ComparisonLibrary();
        library.Load(new[] { Json(SamplePrograms.SafePace()), Json(SamplePrograms.SafePace()) });

        Assert.Null(library.Rename(library.Entries[0], "Safe Pace Old", new string[0]));

        Assert.Equal(2, library.Programs.Count());
        Assert.All(library.Entries, e => Assert.Null(e.LoadError));
    }

    [Fact]
    public void Adding_checks_names_against_the_library_and_the_run()
    {
        var library = new ComparisonLibrary();
        Assert.Null(library.Add(Named("Pace"), new[] { "Personal Best" }));

        Assert.NotNull(library.Add(Named("pace"), new string[0]));           // ours, any case
        Assert.NotNull(library.Add(Named("Sub 20"), new[] { "Sub 20" }));   // hand-made in the splits editor
        Assert.NotNull(library.Add(Named("Best Segments"), new string[0])); // built-in
        Assert.Single(library.Entries);
    }

    [Fact]
    public void Suggested_names_are_free()
    {
        var library = new ComparisonLibrary();
        library.Add(Named("Pace"), new string[0]);
        library.Add(Named("Pace (2)"), new string[0]);

        Assert.Equal("Pace (3)", library.SuggestFreeName("Pace", new string[0]));
        Assert.Equal("Fresh", library.SuggestFreeName("Fresh", new string[0]));
    }

    [Fact]
    public void Editor_save_replaces_readable_entries_and_keeps_unreadable_ones()
    {
        var library = new ComparisonLibrary();
        string broken = "{\"format\":\"livesplit-custom-comparison\",\"version\":99,\"name\":\"Future\",\"root\":{}}";
        library.Load(new[] { Json(SamplePrograms.SafePace()), broken });

        Assert.Null(library.ReplaceReadable(new[] { Named("A"), Named("B") }, new[] { "Personal Best" }));

        Assert.Equal(new[] { "A", "B", "Future" }, library.Entries.Select(e => e.Name));
        Assert.Equal(broken, library.SaveAll().Last());
    }

    [Theory]
    [InlineData("A", "a")]             // duplicate inside the editor
    [InlineData("A", "Future")]        // clashes with the unreadable entry
    [InlineData("A", "Sub 20")]        // clashes with a comparison in the run
    [InlineData("A", "Balanced PB")]   // built-in
    public void Editor_save_is_all_or_nothing(string first, string second)
    {
        var library = new ComparisonLibrary();
        library.Load(new[] { Json(SamplePrograms.SafePace()), "{\"format\":\"livesplit-custom-comparison\",\"version\":99,\"name\":\"Future\",\"root\":{}}" });
        string[] before = library.SaveAll().ToArray();

        Assert.NotNull(library.ReplaceReadable(new[] { Named(first), Named(second) }, new[] { "Sub 20" }));
        Assert.Equal(before, library.SaveAll());
    }

    [Fact]
    public void Renamed_programs_keep_their_blocks()
    {
        var library = new ComparisonLibrary();
        library.Load(new[] { Json(SamplePrograms.SafePace()) });

        library.Rename(library.Entries[0], "  Careful Pace  ", new string[0]);

        ComparisonProgram renamed = library.Programs.Single();
        Assert.Equal("Careful Pace", renamed.Name);
        Assert.Equal(
            Json(new ComparisonProgram("Careful Pace", SamplePrograms.SafePace().Root)),
            library.SaveAll().Single());
    }

    [Fact]
    public void Rename_rewrites_dependents_and_leaves_everyone_else_untouched()
    {
        var library = LibraryWithDependents(out string[] before);

        // A rejected name must not rewrite some dependents and leave the rest.
        Assert.NotNull(library.Rename(library.Entries[0], "Personal Best", new string[0]));
        Assert.NotNull(library.Rename(library.Entries[0], "Future Base", new string[0]));
        Assert.Equal(before, library.SaveAll());
        Assert.Equal(2, library.CountUsers("Base", library.Entries[0]));
        Assert.Equal(2, library.CountUsers("base", library.Entries[0]));

        Assert.Null(library.Rename(library.Entries[0], "  Base Pace  ", new string[0]));

        Assert.Equal("Base Pace", library.Entries[0].Name);
        Assert.Empty(ComparisonNames(library.Entries[0].Json));
        Assert.Equal(new[] { "Base Pace" }, ComparisonNames(library.Entries[1].Json));
        Assert.Equal(
            new[] { "Base Pace", "Personal Best", "Base Pace", "Base Pace", "Baseline" },
            ComparisonNames(library.Entries[2].Json));
        Assert.Equal(before[3], library.Entries[3].Json);
        Assert.Equal(before[4], library.Entries[4].Json);
        Assert.Equal(0, library.CountUsers("Base"));
        Assert.Equal(2, library.CountUsers("Base Pace", library.Entries[0]));

        JsonObject derived = (JsonObject)JsonReader.Parse(library.Entries[1].Json);
        JsonObject block = ComparisonBlock(derived["workspace"]);
        Assert.Equal("Base Pace", ((JsonString)((JsonObject)block["fields"])["NAME"]).Value);
        // A field that merely contains the old name is not a reference.
        Assert.Equal("Base", ((JsonString)((JsonObject)block["fields"])["note"]).Value);
        Assert.Equal(40, ((JsonNumber)block["x"]).Value);
        JsonObject renamedWorkspace = (JsonObject)((JsonObject)JsonReader.Parse(library.Entries[0].Json))["workspace"];
        Assert.Equal("base-layout", ((JsonString)renamedWorkspace["marker"]).Value);

        RunData run = new RunBuilder("A").Attempt(10).Build();
        ComparisonResult result = new ComparisonEvaluator(library.Programs).Evaluate("Derived", run);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(12, result.SegmentTimes[0].Value.TotalSeconds);
    }

    [Fact]
    public void Rename_does_not_rewrite_the_programs_own_blocks()
    {
        var library = new ComparisonLibrary();
        library.Add(new ProgramDocument(new ComparisonProgram("Base", new ComparisonSourceNode("Base"))), new string[0]);
        library.Add(new ProgramDocument(new ComparisonProgram("On Top", new ComparisonSourceNode("Base"))), new string[0]);

        Assert.Equal(1, library.CountUsers("Base", library.Entries[0]));
        Assert.Null(library.Rename(library.Entries[0], "Pace", new string[0]));

        Assert.Equal(new[] { "Base" }, ComparisonNames(library.Entries[0].Json));
        Assert.Equal(new[] { "Pace" }, ComparisonNames(library.Entries[1].Json));
    }

    /// <summary>
    /// Base, a dependent, a program that nests a comparison block in every kind of
    /// parent, a program that doesn't use Base, and an unreadable entry whose text does.
    /// </summary>
    private static ComparisonLibrary LibraryWithDependents(out string[] before)
    {
        SamplesNode history = new ReachedSegmentNode(
            new AtLeastNode(
                new FinishedUnderNode(
                    new TrimOutliersNode(
                        new RecentDaysNode(
                            new LastNNode(new CompletedRunsOnlyNode(new SegmentHistoryNode()), 4),
                            14),
                        10),
                    TimeSpan.FromMinutes(20)),
                2),
            1);
        // One comparison name sits under every kind of block, so a walker that skips a parent fails this test.
        TimesNode nested = new FallbackNode(
            new ClampNode(
                new RoundNode(new ComparisonSourceNode("Base"), TimeSpan.FromSeconds(0.1)),
                new AddPerSegmentNode(new ComparisonSourceNode("Personal Best"), TimeSpan.FromSeconds(1)),
                new ScaleNode(new ComparisonSourceNode("base"), 110)),
            new SegmentRangeSwitchNode(
                new[]
                {
                    new SegmentRangeCase(1, 2, new BlendNode(new ComparisonSourceNode("Base"), new PercentileNode(history, 50), 25)),
                },
                new PickNode(
                    PickKind.Faster,
                    new StretchToTotalNode(new WeightedAverageNode(new SegmentHistoryNode(), 3), TimeSpan.FromSeconds(60)),
                    new FallbackNode(
                        new PickNode(PickKind.Slower, new AttemptNode(1), new BestRecentRunNode(7)),
                        new ComparisonSourceNode("Baseline")))));

        var workspace = new JsonObject()
            .Add("blocks", new JsonObject()
                .Add("languageVersion", 0)
                .Add("blocks", new JsonArray(new JsonValue[]
                {
                    new JsonObject()
                        .Add("type", "cc_comparison")
                        .Add("x", 40)
                        .Add("fields", new JsonObject().Add("NAME", "Base").Add("note", "Base")),
                })));

        string broken = "{\"format\":\"livesplit-custom-comparison\",\"version\":99,\"name\":\"Future Base\",\"root\":{\"type\":\"comparison\",\"name\":\"Base\"}}";
        var library = new ComparisonLibrary();
        library.Load(new[]
        {
            ProgramSerializer.Write(new ProgramDocument(
                new ComparisonProgram("Base", new AggregateNode(AggregateKind.Min, new SegmentHistoryNode())),
                new JsonObject().Add("marker", "base-layout")), indented: false),
            ProgramSerializer.Write(new ProgramDocument(
                new ComparisonProgram("Derived", new AddPerSegmentNode(new ComparisonSourceNode("Base"), TimeSpan.FromSeconds(2))),
                workspace), indented: false),
            ProgramSerializer.Write(new ProgramDocument(new ComparisonProgram("Nested", nested)), indented: false),
            ProgramSerializer.Write(new ProgramDocument(
                new ComparisonProgram("Plain", new ComparisonSourceNode("Personal Best")),
                new JsonObject().Add("note", "Base")), indented: false),
            broken,
        });
        before = library.SaveAll().ToArray();
        return library;
    }

    private static IList<string> ComparisonNames(string json)
    {
        var names = new List<string>();
        const string token = "\"type\":\"comparison\",\"name\":\"";
        int start = 0;
        while ((start = json.IndexOf(token, start, StringComparison.Ordinal)) >= 0)
        {
            int value = start + token.Length;
            int end = json.IndexOf('"', value);
            names.Add(json.Substring(value, end - value));
            start = end + 1;
        }

        return names;
    }

    private static JsonObject ComparisonBlock(JsonValue value)
    {
        switch (value)
        {
            case JsonObject obj:
                if (obj["type"] is JsonString type && type.Value == "cc_comparison")
                {
                    return obj;
                }

                foreach (KeyValuePair<string, JsonValue> member in obj.Members)
                {
                    JsonObject found = ComparisonBlock(member.Value);
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            case JsonArray array:
                foreach (JsonValue item in array.Items)
                {
                    JsonObject found = ComparisonBlock(item);
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }
}
