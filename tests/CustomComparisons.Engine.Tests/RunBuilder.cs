using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Model;

namespace CustomComparisons.Engine.Tests;

/// <summary>
/// Builds RunData the way LiveSplit records it. Each attempt lists segment times in
/// seconds; a shorter list means the runner reset there, and null means the segment
/// was skipped. <see cref="NoTime"/> marks a reached segment with no time for this
/// timing method, which is not a skip. An attempt is "completed" only if it has a
/// time for the last segment.
/// </summary>
internal sealed class RunBuilder
{
    private readonly string[] segmentNames;
    private readonly List<(int Index, double?[] Seconds, DateTime? When, int[] Missing)> attempts = new();
    private readonly DateTime now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, double?[]> comparisons = new(StringComparer.Ordinal);

    public RunBuilder(params string[] segmentNames)
    {
        this.segmentNames = segmentNames;
    }

    public RunBuilder Attempt(params double?[] seconds)
    {
        attempts.Add((attempts.Count + 1, seconds, null, null));
        return this;
    }

    /// <summary>An attempt that ended <paramref name="daysAgo"/> days before "now".</summary>
    public RunBuilder AttemptDaysAgo(double daysAgo, params double?[] seconds)
    {
        attempts.Add((attempts.Count + 1, seconds, now.AddDays(-daysAgo), null));
        return this;
    }

    /// <summary>
    /// The latest attempt reached these segments (0-based) but has no time for this
    /// timing method. Unlike a null passed to <see cref="Attempt"/>, this is not a skip.
    /// </summary>
    public RunBuilder NoTime(params int[] segmentIndexes)
    {
        if (attempts.Count == 0)
        {
            throw new InvalidOperationException("Call Attempt before NoTime.");
        }

        (int index, double?[] seconds, DateTime? when, int[] missing) = attempts[attempts.Count - 1];
        var merged = new HashSet<int>(missing ?? Array.Empty<int>());
        foreach (int segment in segmentIndexes)
        {
            merged.Add(segment);
        }

        attempts[attempts.Count - 1] = (index, seconds, when, merged.ToArray());
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
        var missing = segmentNames.Select(_ => new HashSet<int>()).ToArray();
        foreach ((int index, double?[] seconds, _, int[] missingSegments) in attempts)
        {
            for (int i = 0; i < seconds.Length && i < segmentNames.Length; i++)
            {
                histories[i][index] = Seconds(seconds[i]);
            }

            if (missingSegments == null)
            {
                continue;
            }

            foreach (int i in missingSegments)
            {
                if (i < 0 || i >= segmentNames.Length)
                {
                    continue;
                }

                histories[i][index] = null;
                missing[i].Add(index);
            }
        }

        var segments = segmentNames.Select((name, i) => new SegmentData(name, histories[i], missing[i]));
        // Completed = has a time for the last segment. Its final time is the sum of its segments.
        var attemptData = attempts.Select(a =>
        {
            bool lastMissing = a.Missing != null && a.Missing.Contains(segmentNames.Length - 1);
            bool completed = a.Seconds.Length == segmentNames.Length && a.Seconds[a.Seconds.Length - 1] != null && !lastMissing;
            TimeSpan? final = completed ? TimeSpan.FromSeconds(a.Seconds.Sum(x => x ?? 0)) : null;
            return new AttemptData(a.Index, completed, a.When, final);
        });

        IReadOnlyList<TimeSpan?> Lookup(string name) =>
            comparisons.TryGetValue(name, out double?[] splits) ? splits.Select(Seconds).ToList() : null;

        return new RunData(segments, attemptData, Lookup, now);
    }

    private static TimeSpan? Seconds(double? s) => s.HasValue ? TimeSpan.FromSeconds(s.Value) : null;
}
