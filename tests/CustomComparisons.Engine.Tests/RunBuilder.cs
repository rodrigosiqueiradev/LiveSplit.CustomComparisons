using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Model;

namespace CustomComparisons.Engine.Tests;

/// <summary>
/// Builds RunData the way LiveSplit records it. Each attempt lists segment times in
/// seconds; a shorter list means the runner reset there, and null means the segment
/// was skipped. An attempt is "completed" only if it has a time for the last segment.
/// </summary>
internal sealed class RunBuilder
{
    private readonly string[] segmentNames;
    private readonly List<(int Index, double?[] Seconds, DateTime? When)> attempts = new();
    private readonly DateTime now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, double?[]> comparisons = new(StringComparer.Ordinal);

    public RunBuilder(params string[] segmentNames)
    {
        this.segmentNames = segmentNames;
    }

    public RunBuilder Attempt(params double?[] seconds)
    {
        attempts.Add((attempts.Count + 1, seconds, null));
        return this;
    }

    /// <summary>An attempt that ended <paramref name="daysAgo"/> days before "now".</summary>
    public RunBuilder AttemptDaysAgo(double daysAgo, params double?[] seconds)
    {
        attempts.Add((attempts.Count + 1, seconds, now.AddDays(-daysAgo)));
        return this;
    }

    /// <summary>Adds an existing comparison as cumulative split times in seconds.</summary>
    public RunBuilder Comparison(string name, params double?[] splitSeconds)
    {
        comparisons[name] = splitSeconds;
        return this;
    }

    public RunData Build()
    {
        var histories = segmentNames.Select(_ => new Dictionary<int, TimeSpan?>()).ToArray();
        foreach ((int index, double?[] seconds, _) in attempts)
        {
            for (int i = 0; i < seconds.Length && i < segmentNames.Length; i++)
            {
                histories[i][index] = Seconds(seconds[i]);
            }
        }

        var segments = segmentNames.Select((name, i) => new SegmentData(name, histories[i]));
        // Completed = has a time for the last segment. Its final time is the sum of its segments.
        var attemptData = attempts.Select(a =>
        {
            bool completed = a.Seconds.Length == segmentNames.Length && a.Seconds[a.Seconds.Length - 1] != null;
            TimeSpan? final = completed ? TimeSpan.FromSeconds(a.Seconds.Sum(x => x ?? 0)) : null;
            return new AttemptData(a.Index, completed, a.When, final);
        });

        IReadOnlyList<TimeSpan?> Lookup(string name) =>
            comparisons.TryGetValue(name, out double?[] splits) ? splits.Select(Seconds).ToList() : null;

        return new RunData(segments, attemptData, Lookup, now);
    }

    private static TimeSpan? Seconds(double? s) => s.HasValue ? TimeSpan.FromSeconds(s.Value) : null;
}
