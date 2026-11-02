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

    /// <summary>Why the last generation failed, or null when neither timing method reported an error. Shown ahead of any empty-segment warning.</summary>
    public string LastError { get; private set; }

    /// <summary>
    /// Empty-segment warning after the last successful real-time generation.
    /// Null when real time failed, or when every real-time segment has a time.
    /// </summary>
    public string RealTimeEmptyWarning { get; private set; }

    /// <summary>Same as <see cref="RealTimeEmptyWarning"/>, for game time.</summary>
    public string GameTimeEmptyWarning { get; private set; }

    /// <summary>The empty-segment warning for the timing method the runner is looking at.</summary>
    public string EmptyWarningFor(TimingMethod method) =>
        method == TimingMethod.GameTime ? GameTimeEmptyWarning : RealTimeEmptyWarning;

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
            string realTimeError = Generate(TimingMethod.RealTime, comparisons, out string realGap);
            string gameTimeError = Generate(TimingMethod.GameTime, comparisons, out string gameGap);
            LastError = realTimeError ?? gameTimeError;
            // A failure already explains a blank column. Only keep the warning when that method succeeded.
            RealTimeEmptyWarning = realTimeError == null ? realGap : null;
            GameTimeEmptyWarning = gameTimeError == null ? gameGap : null;
        }
        catch (Exception e)
        {
            // Never let a bad program take LiveSplit down mid-session.
            LastError = "Unexpected error: " + e.Message;
            RealTimeEmptyWarning = null;
            GameTimeEmptyWarning = null;
            Clear();
        }
    }

    private string Generate(TimingMethod method, ComparisonSource comparisons, out string emptyWarning)
    {
        ComparisonResult result = evaluator.Evaluate(Name, RunAdapter.ToRunData(Run, method, comparisons));

        for (int i = 0; i < Run.Count; i++)
        {
            TimeSpan? split = i < result.SplitTimes.Count ? result.SplitTimes[i] : null;
            Time time = Run[i].Comparisons.TryGetValue(Name, out Time existing) ? existing : default;
            time[method] = split;
            Run[i].Comparisons[Name] = time;
        }

        emptyWarning = result.Error == null ? EmptySegmentStatus.Describe(SegmentNames(), result.SegmentTimes) : null;
        return result.Error;
    }

    private string[] SegmentNames()
    {
        var names = new string[Run.Count];
        for (int i = 0; i < Run.Count; i++)
        {
            names[i] = Run[i].Name;
        }

        return names;
    }

    private void Clear()
    {
        foreach (ISegment segment in Run)
        {
            segment.Comparisons[Name] = default;
        }
    }
}
