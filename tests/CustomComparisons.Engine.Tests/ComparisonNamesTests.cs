using Xunit;

namespace CustomComparisons.Engine.Tests;

public class ComparisonNamesTests
{
    [Theory]
    [InlineData("Personal Best")]
    [InlineData("best segments")]   // case doesn't help
    [InlineData("  Balanced PB  ")] // nor does whitespace
    public void Built_in_names_are_rejected(string name)
    {
        Assert.NotNull(ComparisonNames.Validate(name, new string[0]));
    }

    [Fact]
    public void Names_already_in_the_run_are_rejected_ignoring_case()
    {
        Assert.NotNull(ComparisonNames.Validate("sub 20 pace", new[] { "Sub 20 Pace" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[Race] Bob")]
    public void Empty_and_race_names_are_rejected(string name)
    {
        Assert.NotNull(ComparisonNames.Validate(name, new string[0]));
    }

    [Fact]
    public void A_fresh_name_is_fine()
    {
        Assert.Null(ComparisonNames.Validate("Median Last 10 +1s", new[] { "Personal Best", "Best Segments" }));
    }
}
