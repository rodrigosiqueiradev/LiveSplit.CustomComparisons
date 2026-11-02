using System.Linq;
using LiveSplit.CustomComparisons;
using LiveSplit.Model;
using LiveSplit.UI;
using Xunit;

namespace LiveSplit.CustomComparisons.Tests
{
    public class EditedCloneTests
    {
        private const string Name = "History Min";

        private static readonly string Program = LiveSplitHarness.ProgramJson(
            Name,
            "{\"type\":\"aggregate\",\"kind\":\"min\",\"source\":{\"type\":\"segmentHistory\"}}");

        [Fact]
        public void Attach_follows_a_run_replaced_by_an_edited_clone()
        {
            using (Session session = LiveSplitHarness.Open("edited-clone.lss", Program))
            {
                IRun original = session.Run;
                ProgramComparisonGenerator generator = LiveSplitHarness.Generator(original, Name);
                Assert.Equal(new double?[] { 3, 7 }, LiveSplitHarness.Splits(original, Name, TimingMethod.RealTime));

                // LiveSplit's splits editor clones the run and copies the generator list.
                // The copied generator still points at the run it was built for.
                IRun edited = (IRun)((Run)original).Clone();
                edited[0].Name = "Alpha Edited";
                Assert.Contains(generator, edited.ComparisonGenerators);
                Assert.Same(original, generator.Run);

                session.State.Run = edited;
                session.Component.Update(null, session.State, 100, 100, LayoutMode.Vertical);

                Assert.Same(edited, generator.Run);
                Assert.Contains(generator, edited.ComparisonGenerators);
                Assert.DoesNotContain(generator, original.ComparisonGenerators);
                Assert.Equal("Alpha Edited", edited[0].Name);
                Assert.Equal(new double?[] { 3, 7 }, LiveSplitHarness.Splits(edited, Name, TimingMethod.RealTime));

                int registered = edited.ComparisonGenerators.Count(g => g.Name == Name);
                session.Component.Update(null, session.State, 100, 100, LayoutMode.Vertical);
                Assert.Equal(registered, edited.ComparisonGenerators.Count(g => g.Name == Name));
                Assert.Same(edited, generator.Run);
            }
        }
    }
}
