using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomComparisons.Engine.Ast;

// The AST mirrors the typed blocks in the editor. Each block's output type is a
// C# base class, so an ill-typed tree cannot be constructed:
//
//   SamplesNode  - per segment, a list of historical segment times (with the
//                  attempt each came from). Sources and filters produce these.
//   TimesNode    - per segment, one time (or empty). Aggregates, comparisons,
//                  math and control blocks produce these. A program's root is
//                  always a TimesNode.
//
// Everything is per segment. The evaluator sums segment times into cumulative
// split times only at the very end.

public abstract class Node
{
}

public abstract class SamplesNode : Node
{
}

public abstract class TimesNode : Node
{
}

/// <summary>A named comparison defined by the user.</summary>
public sealed class ComparisonProgram
{
    /// <summary>
    /// The newest format this build understands. Version 2 added percentile, recentDays,
    /// stretchToTotal, blend and pick; version 3 added trimOutliers, finishedUnder, atLeast
    /// and attempt; version 4 added reachedSegment, weightedAverage, clamp, round and
    /// bestRecentRun. Files are written with the lowest version that can hold their blocks,
    /// so they open in older builds whenever possible.
    /// </summary>
    public const int CurrentAstVersion = 4;

    public string Name { get; }
    public TimesNode Root { get; }
    public int AstVersion { get; }

    public ComparisonProgram(string name, TimesNode root, int astVersion = CurrentAstVersion)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A comparison needs a name.", nameof(name));
        }

        Name = name.Trim();
        Root = root ?? throw new ArgumentNullException(nameof(root));
        AstVersion = astVersion;
    }
}

// ---------------------------------------------------------------- Sources

/// <summary>
/// Every recorded time for each segment, from the run's segment history.
/// Skipped segments are left out, and so is the segment right after a skip,
/// because its recorded time covers both segments.
/// </summary>
public sealed class SegmentHistoryNode : SamplesNode
{
}

/// <summary>An existing comparison (built-in or another custom program), as segment times.</summary>
public sealed class ComparisonSourceNode : TimesNode
{
    public string ComparisonName { get; }

    public ComparisonSourceNode(string comparisonName)
    {
        if (string.IsNullOrWhiteSpace(comparisonName))
        {
            throw new ArgumentException("A comparison name is required.", nameof(comparisonName));
        }

        ComparisonName = comparisonName.Trim();
    }
}

// ---------------------------------------------------------------- Filters

/// <summary>
/// Keeps, for each segment, the N most recent times recorded for that segment.
/// N counts completions of the segment, not attempts overall.
/// </summary>
public sealed class LastNNode : SamplesNode
{
    public SamplesNode Source { get; }
    public int Count { get; }

    public LastNNode(SamplesNode source, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Must keep at least one time.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Count = count;
    }
}

/// <summary>Keeps only times that came from attempts which finished the whole run.</summary>
public sealed class CompletedRunsOnlyNode : SamplesNode
{
    public SamplesNode Source { get; }

    public CompletedRunsOnlyNode(SamplesNode source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }
}

/// <summary>
/// Keeps only times from attempts that ended within the last <see cref="Days"/> days.
/// Attempts with no recorded date are left out, since their age is unknown.
/// </summary>
public sealed class RecentDaysNode : SamplesNode
{
    public SamplesNode Source { get; }
    public int Days { get; }

    public RecentDaysNode(SamplesNode source, int days)
    {
        if (days < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Must cover at least one day.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Days = days;
    }
}

/// <summary>
/// Per segment, drops the fastest and the slowest <see cref="Percent"/>% of times
/// (rounded down, so small lists are left alone) before anything else sees them.
/// </summary>
public sealed class TrimOutliersNode : SamplesNode
{
    public const double MaxPercent = 45;

    public SamplesNode Source { get; }
    public double Percent { get; }

    public TrimOutliersNode(SamplesNode source, double percent)
    {
        if (double.IsNaN(percent) || percent < 0 || percent > MaxPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), $"Must be from 0 to {MaxPercent}.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Percent = percent;
    }
}

/// <summary>Keeps only times from attempts that finished the run faster than <see cref="Limit"/>.</summary>
public sealed class FinishedUnderNode : SamplesNode
{
    public SamplesNode Source { get; }
    public TimeSpan Limit { get; }

    public FinishedUnderNode(SamplesNode source, TimeSpan limit)
    {
        if (limit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The time must be positive.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Limit = limit;
    }
}

/// <summary>
/// Leaves a segment's list empty unless it has at least <see cref="Count"/> times, so a
/// rule built on too little data can be replaced with "if empty, use".
/// </summary>
public sealed class AtLeastNode : SamplesNode
{
    public SamplesNode Source { get; }
    public int Count { get; }

    public AtLeastNode(SamplesNode source, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Must be at least 1.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Count = count;
    }
}

/// <summary>
/// The segment times of one past attempt, by its number in LiveSplit's attempt history.
/// Segments that attempt skipped or never reached are empty.
/// </summary>
public sealed class AttemptNode : TimesNode
{
    public int Number { get; }

    public AttemptNode(int number)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Attempt numbers start at 1.");
        }

        Number = number;
    }
}

/// <summary>
/// Keeps only times from attempts that got at least as far as segment <see cref="Segment"/>
/// (1-based): they have a recorded or skipped time for it. Good for reset-heavy games, where
/// few runs finish. A segment past the end of the run keeps nothing.
/// </summary>
public sealed class ReachedSegmentNode : SamplesNode
{
    public SamplesNode Source { get; }
    public int Segment { get; }

    public ReachedSegmentNode(SamplesNode source, int segment)
    {
        if (segment < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segment), "Segment numbers start at 1.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Segment = segment;
    }
}

/// <summary>
/// The segments of the fastest finished attempt that ended within the last <see cref="Days"/>
/// days: a "recent PB". Empty if no run finished in that time.
/// </summary>
public sealed class BestRecentRunNode : TimesNode
{
    public int Days { get; }

    public BestRecentRunNode(int days)
    {
        if (days < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Must cover at least one day.");
        }

        Days = days;
    }
}

// ---------------------------------------------------------------- Aggregates

public enum AggregateKind
{
    Min,
    Max,
    Average,
    Median,
}

/// <summary>
/// Reduces each segment's samples to one time, independently per segment.
/// A segment with no samples becomes empty.
/// </summary>
public sealed class AggregateNode : TimesNode
{
    public AggregateKind Kind { get; }
    public SamplesNode Source { get; }

    public AggregateNode(AggregateKind kind, SamplesNode source)
    {
        Kind = kind;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }
}

/// <summary>
/// The given percentile of each segment's samples, interpolating between neighbouring
/// times. 0 is the fastest, 100 the slowest, 50 equals the median.
/// </summary>
public sealed class PercentileNode : TimesNode
{
    public SamplesNode Source { get; }
    public double Percent { get; }

    public PercentileNode(SamplesNode source, double percent)
    {
        if (double.IsNaN(percent) || percent < 0 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Percentile must be from 0 to 100.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Percent = percent;
    }
}

/// <summary>
/// An average where recent times count more: each time's weight halves for every
/// <see cref="HalfLife"/> newer times recorded for that segment. The newest time weighs 1,
/// the time HalfLife places older weighs 0.5, and so on.
/// </summary>
public sealed class WeightedAverageNode : TimesNode
{
    public SamplesNode Source { get; }
    public int HalfLife { get; }

    public WeightedAverageNode(SamplesNode source, int halfLife)
    {
        if (halfLife < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(halfLife), "Must be at least 1.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        HalfLife = halfLife;
    }
}

// ---------------------------------------------------------------- Math

/// <summary>
/// Per segment, keeps <see cref="Source"/> between <see cref="Low"/> (never faster than) and
/// <see cref="High"/> (never slower than). A limit that's empty for a segment doesn't apply
/// there. If the limits cross, "never faster than" wins. Empty where the source is empty.
/// </summary>
public sealed class ClampNode : TimesNode
{
    public TimesNode Source { get; }
    public TimesNode Low { get; }
    public TimesNode High { get; }

    public ClampNode(TimesNode source, TimesNode low, TimesNode high)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Low = low ?? throw new ArgumentNullException(nameof(low));
        High = high ?? throw new ArgumentNullException(nameof(high));
    }
}

/// <summary>
/// Rounds every segment to the nearest multiple of <see cref="Step"/> (halves round up).
/// Split times, being sums of segments, then land on the same step.
/// </summary>
public sealed class RoundNode : TimesNode
{
    public TimesNode Source { get; }
    public TimeSpan Step { get; }

    public RoundNode(TimesNode source, TimeSpan step)
    {
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The step must be positive.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Step = step;
    }
}

/// <summary>Adds (or, if negative, subtracts) a fixed amount to every non-empty segment.</summary>
public sealed class AddPerSegmentNode : TimesNode
{
    public TimesNode Source { get; }
    public TimeSpan Amount { get; }

    public AddPerSegmentNode(TimesNode source, TimeSpan amount)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Amount = amount;
    }
}

/// <summary>Scales every non-empty segment by a percentage (105 = 5% slower).</summary>
public sealed class ScaleNode : TimesNode
{
    public TimesNode Source { get; }
    public double Percent { get; }

    public ScaleNode(TimesNode source, double percent)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent) || percent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Percent must be a positive number.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Percent = percent;
    }
}

/// <summary>
/// Scales every segment by the same factor so they add up exactly to <see cref="Total"/>.
/// If any segment is empty the total can't be known, so the whole result is empty.
/// </summary>
public sealed class StretchToTotalNode : TimesNode
{
    public TimesNode Source { get; }
    public TimeSpan Total { get; }

    public StretchToTotalNode(TimesNode source, TimeSpan total)
    {
        if (total <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "The goal time must be positive.");
        }

        Source = source ?? throw new ArgumentNullException(nameof(source));
        Total = total;
    }
}

/// <summary>
/// Per segment, <see cref="Percent"/>% of the way from <see cref="From"/> to <see cref="To"/>.
/// 0 is all From, 100 all To. Empty where either side is empty.
/// </summary>
public sealed class BlendNode : TimesNode
{
    public TimesNode From { get; }
    public TimesNode To { get; }
    public double Percent { get; }

    public BlendNode(TimesNode from, TimesNode to, double percent)
    {
        if (double.IsNaN(percent) || percent < 0 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Must be from 0 to 100.");
        }

        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Percent = percent;
    }
}

public enum PickKind
{
    Faster,
    Slower,
}

/// <summary>
/// Per segment, the faster (or slower) of two rules. Where one side is empty, the other is used.
/// </summary>
public sealed class PickNode : TimesNode
{
    public PickKind Kind { get; }
    public TimesNode First { get; }
    public TimesNode Second { get; }

    public PickNode(PickKind kind, TimesNode first, TimesNode second)
    {
        Kind = kind;
        First = first ?? throw new ArgumentNullException(nameof(first));
        Second = second ?? throw new ArgumentNullException(nameof(second));
    }
}

// ---------------------------------------------------------------- Control

/// <summary>One arm of a segment-range switch. Segment numbers are 1-based and inclusive.</summary>
public sealed class SegmentRangeCase
{
    public int FromSegment { get; }
    public int ToSegment { get; }
    public TimesNode Times { get; }

    public SegmentRangeCase(int fromSegment, int toSegment, TimesNode times)
    {
        if (fromSegment < 1 || toSegment < fromSegment)
        {
            throw new ArgumentOutOfRangeException(nameof(fromSegment), "Range must satisfy 1 <= from <= to.");
        }

        FromSegment = fromSegment;
        ToSegment = toSegment;
        Times = times ?? throw new ArgumentNullException(nameof(times));
    }

    public bool Contains(int segmentNumber) => segmentNumber >= FromSegment && segmentNumber <= ToSegment;
}

/// <summary>
/// "Use A for segments 1-4, B for the rest". The first case containing a segment wins;
/// segments no case covers use <see cref="Otherwise"/>. Ranges past the end of the run
/// simply never match.
/// </summary>
public sealed class SegmentRangeSwitchNode : TimesNode
{
    public IReadOnlyList<SegmentRangeCase> Cases { get; }
    public TimesNode Otherwise { get; }

    public SegmentRangeSwitchNode(IEnumerable<SegmentRangeCase> cases, TimesNode otherwise)
    {
        Cases = (cases ?? throw new ArgumentNullException(nameof(cases))).ToList();
        Otherwise = otherwise ?? throw new ArgumentNullException(nameof(otherwise));
    }
}

/// <summary>Uses <see cref="Primary"/>, filling only its empty segments from <see cref="Fallback"/>.</summary>
public sealed class FallbackNode : TimesNode
{
    public TimesNode Primary { get; }
    public TimesNode Fallback { get; }

    public FallbackNode(TimesNode primary, TimesNode fallback)
    {
        Primary = primary ?? throw new ArgumentNullException(nameof(primary));
        Fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }
}
