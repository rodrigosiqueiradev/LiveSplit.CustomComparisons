using System.Collections.Generic;
using System.Linq;
using LiveSplit.CustomComparisons;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using Xunit;

namespace LiveSplit.CustomComparisons.Tests
{
    public class SwitchedOffBuiltInTests
    {
        private const string Name = "Using Golds";

        private static readonly string Program = LiveSplitHarness.ProgramJson(
            Name,
            "{\"type\":\"comparison\",\"name\":\"Best Segments\"}");

        [Fact]
        public void A_switched_off_built_in_is_resolved_on_a_private_copy()
        {
            IRun run = LiveSplitHarness.Load("switched-off-builtin.lss");
            IComparisonGenerator builtIn = run.ComparisonGenerators.Single(g => g.Name == "Best Segments");
            run.ComparisonGenerators.Remove(builtIn);
            Assert.DoesNotContain("Best Segments", run.Comparisons);

            var settings = new LiveSplit.Options.Settings();
            // A fresh Settings leaves this null. LiveSplit fills it when it loads settings.
            settings.ComparisonGeneratorStates = new Dictionary<string, bool>
            {
                ["Best Segments"] = false
            };
            var state = new LiveSplitState(run, null, new LiveSplit.UI.Layout(), null, settings);
            var component = new CustomComparisonsComponent(state);
            try
            {
                component.SetSettings(LiveSplitHarness.ComparisonsSettings(new[] { Program }));

                Assert.Null(LiveSplitHarness.Generator(run, Name).LastError);
                // Still switched off on the runner's run. The private clone absorbed the generator.
                Assert.DoesNotContain("Best Segments", run.Comparisons);
                // Golds are 4s and 8s. Best Segments sums them: 4, 12.
                Assert.Equal(new double?[] { 4, 12 }, LiveSplitHarness.Splits(run, Name, TimingMethod.RealTime));
                Assert.Equal(new double?[] { 4, 12 }, LiveSplitHarness.Splits(run, Name, TimingMethod.GameTime));

                IRun independent = (IRun)run.Clone();
                IComparisonGenerator check = new StandardComparisonGeneratorsFactory()
                    .GetAllGenerators(independent)
                    .Single(g => g.Name == "Best Segments");
                check.Generate(settings);
                Assert.Equal(
                    LiveSplitHarness.Splits(independent, "Best Segments", TimingMethod.RealTime),
                    LiveSplitHarness.Splits(run, Name, TimingMethod.RealTime));

                string reply = LiveSplitHarness.Preview(component, state, Program, TimingMethod.RealTime);
                Assert.Equal(new double?[] { 4_000, 8_000 }, LiveSplitHarness.PreviewSegmentMilliseconds(reply));
                Assert.DoesNotContain("Best Segments", run.Comparisons);
            }
            finally
            {
                component.Dispose();
            }
        }
    }
}
