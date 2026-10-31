using System;
using System.Collections.Generic;
using CustomComparisons.Engine.Evaluation;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;

namespace LiveSplit.CustomComparisons;

/// <summary>
/// Exposes one user program to LiveSplit as a regular comparison generator.
/// LiveSplit calls <see cref="Generate"/> itself whenever it regenerates
/// comparisons (for example after a reset), as for its built-in ones.
/// </summary>
internal sealed class ProgramComparisonGenerator : IComparisonGenerator
{
    private readonly ComparisonEvaluator evaluator;
    private readonly ISet<string> ownComparisonNames;

    public ProgramComparisonGenerator(IRun run, string name, ComparisonEvaluator evaluator, ISet<string> ownComparisonNames)
    {
        Run = run;
        Name = name;
        this.evaluator = evaluator;
        this.ownComparisonNames = ownComparisonNames;
    }

    public IRun Run { get; set; }

    public string Name { get; }

    /// <summary>Why the last generation produced nothing, or null if it worked. Shown in settings.</summary>
    public string LastError { get; private set; }

    public void Generate(ISettings settings)
    {
        if (Run == null)
        {
            return;
        }

        try
        {
            // One source for both timing methods, so a switched-off built-in is computed once.
            var comparisons = new ComparisonSource(Run, settings, ownComparisonNames);
            string realTimeError = Generate(TimingMethod.RealTime, comparisons);
            string gameTimeError = Generate(TimingMethod.GameTime, comparisons);
            LastError = realTimeError ?? gameTimeError;
        }
        catch (Exception e)
        {
            // Never let a bad program take LiveSplit down mid-session.
            LastError = "Unexpected error: " + e.Message;
            Clear();
        }
    }

    private string Generate(TimingMethod method, ComparisonSource comparisons)
    {
        ComparisonResult result = evaluator.Evaluate(Name, RunAdapter.ToRunData(Run, method, comparisons));

        for (int i = 0; i < Run.Count; i++)
        {
            TimeSpan? split = i < result.SplitTimes.Count ? result.SplitTimes[i] : null;
            Time time = Run[i].Comparisons.TryGetValue(Name, out Time existing) ? existing : default;
            time[method] = split;
            Run[i].Comparisons[Name] = time;
        }

        return result.Error;
    }

    private void Clear()
    {
        foreach (ISegment segment in Run)
        {
            segment.Comparisons[Name] = default;
        }
    }
}
