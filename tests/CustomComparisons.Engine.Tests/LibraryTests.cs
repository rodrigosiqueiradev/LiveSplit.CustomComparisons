using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Library;
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
}
