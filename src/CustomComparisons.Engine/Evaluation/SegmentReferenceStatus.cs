using System;
using System.Collections.Generic;
using CustomComparisons.Engine.Ast;

namespace CustomComparisons.Engine.Evaluation;

/// <summary>
/// The status sentence when a program names a segment the current splits don't
/// have. "for segments X to Y" and "from runs that reached" store positions, not
/// names, so they go stale when splits are added or removed. Evaluation still
/// succeeds: a range that starts past the end never matches (the otherwise arm
/// is used) and a reached segment past the end keeps nothing.
/// </summary>
/// <remarks>
/// A range that still overlaps the splits (4–9 on a 6-segment run) reports only
/// the end it names, "Uses segment 9, but your splits have 6". A range that
/// starts past the end reports the whole range, "Uses segments 9 to 12, but
/// your splits have 6". Adjacent numbers are joined.
/// </remarks>
public static class SegmentReferenceStatus
{
    /// <summary>
    /// Null when every named segment exists, when <paramref name="root"/> is
    /// missing, or when <paramref name="segmentCount"/> is negative (the splits
    /// aren't known). Otherwise a sentence for the settings list and the editor.
    /// </summary>
    public static string Describe(Node root, int segmentCount)
    {
        if (root == null || segmentCount < 0)
        {
            return null;
        }

        var spans = new List<(int From, int To)>();
        Collect(root, segmentCount, spans);
        return Format(spans, segmentCount);
    }

    private static void Collect(Node node, int segmentCount, List<(int From, int To)> spans)
    {
        switch (node)
        {
            case null:
            case SegmentHistoryNode _:
            case ComparisonSourceNode _:
            case AttemptNode _:
            case BestRecentRunNode _:
                return;

            case LastNNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case CompletedRunsOnlyNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case RecentDaysNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case TrimOutliersNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case FinishedUnderNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case AtLeastNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case ReachedSegmentNode n:
                if (n.Segment > segmentCount)
                {
                    spans.Add((n.Segment, n.Segment));
                }

                Collect(n.Source, segmentCount, spans);
                return;

            case AggregateNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case PercentileNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case WeightedAverageNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case AddPerSegmentNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case ScaleNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case StretchToTotalNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case RoundNode n:
                Collect(n.Source, segmentCount, spans);
                return;
            case ClampNode n:
                Collect(n.Source, segmentCount, spans);
                Collect(n.Low, segmentCount, spans);
                Collect(n.High, segmentCount, spans);
                return;
            case BlendNode n:
                Collect(n.From, segmentCount, spans);
                Collect(n.To, segmentCount, spans);
                return;
            case PickNode n:
                Collect(n.First, segmentCount, spans);
                Collect(n.Second, segmentCount, spans);
                return;
            case FallbackNode n:
                Collect(n.Primary, segmentCount, spans);
                Collect(n.Fallback, segmentCount, spans);
                return;
            case SegmentRangeSwitchNode n:
                foreach (SegmentRangeCase c in n.Cases)
                {
                    // An end past the splits still names that segment, even when the
                    // start overlaps segments that exist. A start past the splits
                    // means the whole range never matches.
                    if (c.ToSegment > segmentCount)
                    {
                        int from = c.FromSegment > segmentCount ? c.FromSegment : c.ToSegment;
                        spans.Add((from, c.ToSegment));
                    }

                    Collect(c.Times, segmentCount, spans);
                }

                Collect(n.Otherwise, segmentCount, spans);
                return;

            default:
                throw new InvalidOperationException("No segment check for " + node.GetType().Name + ".");
        }
    }

    private static string Format(List<(int From, int To)> spans, int segmentCount)
    {
        if (spans.Count == 0)
        {
            return null;
        }

        spans.Sort((a, b) =>
        {
            int byFrom = a.From.CompareTo(b.From);
            return byFrom != 0 ? byFrom : a.To.CompareTo(b.To);
        });

        var merged = new List<(int From, int To)>();
        foreach ((int From, int To) span in spans)
        {
            if (merged.Count == 0 || span.From > merged[merged.Count - 1].To + 1)
            {
                merged.Add(span);
                continue;
            }

            (int From, int To) last = merged[merged.Count - 1];
            merged[merged.Count - 1] = (last.From, Math.Max(last.To, span.To));
        }

        bool oneNumber = merged.Count == 1 && merged[0].From == merged[0].To;
        return $"Uses {(oneNumber ? "segment" : "segments")} {Join(merged)}, but your splits have {segmentCount}";
    }

    private static string Join(List<(int From, int To)> spans)
    {
        var parts = new string[spans.Count];
        for (int i = 0; i < spans.Count; i++)
        {
            (int From, int To) span = spans[i];
            parts[i] = span.From == span.To ? span.From.ToString() : span.From + " to " + span.To;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        if (parts.Length == 2)
        {
            return parts[0] + " and " + parts[1];
        }

        return string.Join(", ", parts, 0, parts.Length - 1) + " and " + parts[parts.Length - 1];
    }
}
