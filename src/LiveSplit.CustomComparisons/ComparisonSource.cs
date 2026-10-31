using System;
using System.Collections.Generic;
using System.Linq;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;

namespace LiveSplit.CustomComparisons;

/// <summary>
/// Supplies existing comparisons to programs during one generation pass.
///
/// A shared program must work no matter which built-in comparisons a runner has
/// switched on, so a built-in that's switched off (say "Best Segments") is computed
/// here with LiveSplit's own generator, on a private copy of the run. Nothing shows
/// up in the runner's menu that they didn't enable.
/// </summary>
internal sealed class ComparisonSource
{
    private readonly IRun run;
    private readonly ISettings settings;
    private readonly ISet<string> ownComparisons;
    private readonly Dictionary<string, List<Time>> computed = new(StringComparer.Ordinal);
    private IRun privateCopy;

    /// <param name="ownComparisons">
    /// Our own comparison names. Programs referencing each other are resolved inside the
    /// engine, so our (possibly stale) results are never read back from the run.
    /// </param>
    public ComparisonSource(IRun run, ISettings settings, ISet<string> ownComparisons)
    {
        this.run = run;
        this.settings = settings;
        this.ownComparisons = ownComparisons;
    }

    /// <summary>Cumulative split times for <paramref name="name"/>, or null if there's no such comparison.</summary>
    public IReadOnlyList<TimeSpan?> Get(string name, TimingMethod method)
    {
        if (ownComparisons.Contains(name))
        {
            return null;
        }

        if (run.Comparisons.Contains(name))
        {
            return run.Select(segment => segment.Comparisons.TryGetValue(name, out Time time) ? time[method] : null).ToList();
        }

        List<Time> builtIn = ComputeBuiltIn(name);
        return builtIn?.Select(time => time[method]).ToList();
    }

    private List<Time> ComputeBuiltIn(string name)
    {
        if (computed.TryGetValue(name, out List<Time> cached))
        {
            return cached;
        }

        privateCopy ??= (IRun)run.Clone();
        IComparisonGenerator generator = new StandardComparisonGeneratorsFactory()
            .GetAllGenerators(privateCopy)
            .FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.Ordinal));

        List<Time> result = null;
        if (generator != null)
        {
            generator.Generate(settings);
            result = privateCopy
                .Select(segment => segment.Comparisons.TryGetValue(name, out Time time) ? time : default)
                .ToList();
        }

        computed[name] = result;
        return result;
    }
}
