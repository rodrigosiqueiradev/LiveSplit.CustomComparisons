using System;
using System.Collections.Generic;

namespace CustomComparisons.Engine.Evaluation;

/// <summary>
/// The status sentence for a comparison that evaluated successfully but left
/// some segments without a time. Counts segment times, not cumulative splits:
/// once a segment is empty every later split is empty too, but a later segment
/// that still has its own time is not counted.
/// </summary>
public static class EmptySegmentStatus
{
    /// <summary>
    /// Null when every segment has a time, or when there are no segments.
    /// "No times at all" when every segment is empty. Otherwise
    /// "Working — no times for N of M segments (first: Name)".
    /// A blank name is reported as "segment N", counting from 1.
    /// </summary>
    public static string Describe(IReadOnlyList<string> segmentNames, IReadOnlyList<TimeSpan?> segmentTimes)
    {
        int total = segmentNames?.Count ?? 0;
        if (total == 0)
        {
            return null;
        }

        int empty = 0;
        string first = null;
        for (int i = 0; i < total; i++)
        {
            bool missing = segmentTimes == null || i >= segmentTimes.Count || !segmentTimes[i].HasValue;
            if (!missing)
            {
                continue;
            }

            empty++;
            if (first == null)
            {
                string name = segmentNames[i];
                first = string.IsNullOrWhiteSpace(name) ? "segment " + (i + 1) : name;
            }
        }

        if (empty == 0)
        {
            return null;
        }

        if (empty == total)
        {
            return "No times at all";
        }

        return $"Working \u2014 no times for {empty} of {total} segments (first: {first})";
    }
}
