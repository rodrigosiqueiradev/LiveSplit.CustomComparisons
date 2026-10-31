using System.Linq;
using CustomComparisons.Engine.Model;
using LiveSplit.Model;

namespace LiveSplit.CustomComparisons;

/// <summary>Translates a LiveSplit run into the engine's LiveSplit-free model, for one timing method.</summary>
internal static class RunAdapter
{
    public static RunData ToRunData(IRun run, TimingMethod method, ComparisonSource comparisons)
    {
        var segments = run.Select(segment => new SegmentData(
            segment.Name,
            segment.SegmentHistory.ToDictionary(entry => entry.Key, entry => entry.Value[method])));

        // LiveSplit records attempt times in UTC. Prefer when it ended; fall back to when it started.
        var attempts = run.AttemptHistory.Select(attempt => new AttemptData(
            attempt.Index,
            attempt.Time[method] != null,
            attempt.Ended?.Time ?? attempt.Started?.Time,
            attempt.Time[method]));

        return new RunData(segments, attempts, name => comparisons.Get(name, method));
    }
}
