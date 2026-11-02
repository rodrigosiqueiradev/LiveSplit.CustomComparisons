using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Model;
using LiveSplit.Model;

namespace LiveSplit.CustomComparisons;

/// <summary>Translates a LiveSplit run into the engine's LiveSplit-free model, for one timing method.</summary>
internal static class RunAdapter
{
    public static RunData ToRunData(IRun run, TimingMethod method, ComparisonSource comparisons)
    {
        var segments = run.Select(segment =>
        {
            var history = new Dictionary<int, TimeSpan?>();
            var missingTime = new List<int>();
            foreach (var entry in segment.SegmentHistory)
            {
                TimeSpan? time = entry.Value[method];
                history[entry.Key] = time;
                // A skipped split stores an empty time (both methods null). A time in the
                // other method means this segment was reached and only this method is missing.
                if (time == null && OtherMethod(entry.Value, method) != null)
                {
                    missingTime.Add(entry.Key);
                }
            }

            return new SegmentData(segment.Name, history, missingTime);
        });

        // LiveSplit records attempt times in UTC. Prefer when it ended; fall back to when it started.
        var attempts = run.AttemptHistory.Select(attempt => new AttemptData(
            attempt.Index,
            attempt.Time[method] != null,
            attempt.Ended?.Time ?? attempt.Started?.Time,
            attempt.Time[method]));

        return new RunData(segments, attempts, name => comparisons.Get(name, method));
    }

    private static TimeSpan? OtherMethod(Time time, TimingMethod method) =>
        time[method == TimingMethod.RealTime ? TimingMethod.GameTime : TimingMethod.RealTime];
}
