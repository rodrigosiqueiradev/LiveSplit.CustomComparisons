using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomComparisons.Engine.Model;

/// <summary>
/// Everything the engine needs from a run, for ONE timing method.
/// The LiveSplit component builds this twice per regeneration: once for real
/// time, once for game time. Keeping it free of LiveSplit types is what lets
/// the engine be tested anywhere.
/// </summary>
public sealed class RunData
{
    private readonly Func<string, IReadOnlyList<TimeSpan?>> comparisonLookup;

    public IReadOnlyList<SegmentData> Segments { get; }

    /// <summary>Attempts, in any order. Keyed by <see cref="AttemptData.Index"/>.</summary>
    public IReadOnlyDictionary<int, AttemptData> Attempts { get; }

    /// <param name="comparisonLookup">
    /// Returns an existing comparison's cumulative split times (one per segment), or null
    /// if no comparison has that name. May be null if the run has no comparisons to offer.
    /// </param>
    /// <param name="nowUtc">"Now", for blocks like "from the last 30 days". Defaults to the clock.</param>
    public RunData(
        IEnumerable<SegmentData> segments,
        IEnumerable<AttemptData> attempts,
        Func<string, IReadOnlyList<TimeSpan?>> comparisonLookup = null,
        DateTime? nowUtc = null)
    {
        NowUtc = nowUtc ?? DateTime.UtcNow;
        Segments = (segments ?? throw new ArgumentNullException(nameof(segments))).ToList();
        Attempts = (attempts ?? Enumerable.Empty<AttemptData>())
            .GroupBy(a => a.Index)
            .ToDictionary(g => g.Key, g => g.Last());
        this.comparisonLookup = comparisonLookup ?? (_ => null);
    }

    public DateTime NowUtc { get; }

    public int SegmentCount => Segments.Count;

    /// <summary>Cumulative split times for an existing comparison, or null if unknown.</summary>
    public IReadOnlyList<TimeSpan?> GetComparisonSplits(string name) => comparisonLookup(name);
}

public sealed class SegmentData
{
    public string Name { get; }

    /// <summary>
    /// Attempt index -> recorded segment time. A null value means the segment was
    /// skipped in that attempt. A missing key means the attempt never reached it.
    /// </summary>
    public IReadOnlyDictionary<int, TimeSpan?> History { get; }

    public SegmentData(string name, IReadOnlyDictionary<int, TimeSpan?> history)
    {
        Name = name ?? "";
        History = history ?? new Dictionary<int, TimeSpan?>();
    }
}

public sealed class AttemptData
{
    public int Index { get; }

    /// <summary>True if this attempt finished the whole run (has a final time).</summary>
    public bool Completed { get; }

    /// <summary>When the attempt ended (or started, if no end was recorded), in UTC. Null if unknown.</summary>
    public DateTime? WhenUtc { get; }

    /// <summary>The attempt's final time for this timing method. Null if it didn't finish.</summary>
    public TimeSpan? FinalTime { get; }

    public AttemptData(int index, bool completed, DateTime? whenUtc = null, TimeSpan? finalTime = null)
    {
        Index = index;
        Completed = completed;
        WhenUtc = whenUtc;
        FinalTime = finalTime;
    }
}
