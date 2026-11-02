using System;
using System.Collections.Generic;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Serialization;

namespace CustomComparisons.Engine.Library;

/// <summary>
/// Comparison blocks name another comparison in plain text. Renaming has to
/// follow those names or the dependent goes blank with "there is no comparison
/// named …". Matching is case-insensitive, same as LiveSplit's menus; the
/// stored name keeps the casing of the rename.
/// </summary>
internal static class ComparisonReferences
{
    /// <summary>The block editor's comparison block, inside a saved workspace.</summary>
    private const string ComparisonBlockType = "cc_comparison";

    public static bool Uses(Node node, string name)
    {
        if (node == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        switch (node)
        {
            case ComparisonSourceNode n:
                return Same(n.ComparisonName, name);
            case AttemptNode:
            case BestRecentRunNode:
            case SegmentHistoryNode:
                return false;
            case AggregateNode n:
                return Uses(n.Source, name);
            case PercentileNode n:
                return Uses(n.Source, name);
            case WeightedAverageNode n:
                return Uses(n.Source, name);
            case AddPerSegmentNode n:
                return Uses(n.Source, name);
            case ScaleNode n:
                return Uses(n.Source, name);
            case RoundNode n:
                return Uses(n.Source, name);
            case StretchToTotalNode n:
                return Uses(n.Source, name);
            case LastNNode n:
                return Uses(n.Source, name);
            case CompletedRunsOnlyNode n:
                return Uses(n.Source, name);
            case RecentDaysNode n:
                return Uses(n.Source, name);
            case TrimOutliersNode n:
                return Uses(n.Source, name);
            case FinishedUnderNode n:
                return Uses(n.Source, name);
            case AtLeastNode n:
                return Uses(n.Source, name);
            case ReachedSegmentNode n:
                return Uses(n.Source, name);
            case FallbackNode n:
                return Uses(n.Primary, name) || Uses(n.Fallback, name);
            case BlendNode n:
                return Uses(n.From, name) || Uses(n.To, name);
            case PickNode n:
                return Uses(n.First, name) || Uses(n.Second, name);
            case ClampNode n:
                return Uses(n.Source, name) || Uses(n.Low, name) || Uses(n.High, name);
            case SegmentRangeSwitchNode n:
                if (Uses(n.Otherwise, name))
                {
                    return true;
                }

                foreach (SegmentRangeCase c in n.Cases)
                {
                    if (Uses(c.Times, name))
                    {
                        return true;
                    }
                }

                return false;
            default:
                throw new ArgumentException($"Can't look for comparison names inside a {node.GetType().Name} block.");
        }
    }

    public static TimesNode Rewrite(TimesNode node, string oldName, string newName) =>
        RewriteTimes(node, oldName, newName, out _);

    /// <summary>
    /// Updates comparison-block names inside the editor layout. Anything else
    /// (positions, notes, unrelated fields) stays. Returns the same instance
    /// when nothing matched, so a workspace with no comparison blocks is kept
    /// as it was.
    /// </summary>
    public static JsonValue RewriteWorkspace(JsonValue workspace, string oldName, string newName)
    {
        if (workspace == null || workspace is JsonNull)
        {
            return workspace;
        }

        return RewriteValue(workspace, oldName, newName, out _);
    }

    private static TimesNode RewriteTimes(TimesNode node, string oldName, string newName, out bool changed)
    {
        switch (node)
        {
            case ComparisonSourceNode n when Same(n.ComparisonName, oldName):
                changed = true;
                return new ComparisonSourceNode(newName);
            case ComparisonSourceNode:
            case AttemptNode:
            case BestRecentRunNode:
                changed = false;
                return node;
            case AggregateNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new AggregateNode(n.Kind, s));
            case PercentileNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new PercentileNode(s, n.Percent));
            case WeightedAverageNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new WeightedAverageNode(s, n.HalfLife));
            case AddPerSegmentNode n:
                return Rebuild(n, RewriteTimes(n.Source, oldName, newName, out changed), changed, s => new AddPerSegmentNode(s, n.Amount));
            case ScaleNode n:
                return Rebuild(n, RewriteTimes(n.Source, oldName, newName, out changed), changed, s => new ScaleNode(s, n.Percent));
            case RoundNode n:
                return Rebuild(n, RewriteTimes(n.Source, oldName, newName, out changed), changed, s => new RoundNode(s, n.Step));
            case StretchToTotalNode n:
                return Rebuild(n, RewriteTimes(n.Source, oldName, newName, out changed), changed, s => new StretchToTotalNode(s, n.Total));
            case FallbackNode n:
            {
                TimesNode primary = RewriteTimes(n.Primary, oldName, newName, out bool primaryChanged);
                TimesNode fallback = RewriteTimes(n.Fallback, oldName, newName, out bool fallbackChanged);
                changed = primaryChanged || fallbackChanged;
                return changed ? new FallbackNode(primary, fallback) : n;
            }

            case BlendNode n:
            {
                TimesNode from = RewriteTimes(n.From, oldName, newName, out bool fromChanged);
                TimesNode to = RewriteTimes(n.To, oldName, newName, out bool toChanged);
                changed = fromChanged || toChanged;
                return changed ? new BlendNode(from, to, n.Percent) : n;
            }

            case PickNode n:
            {
                TimesNode first = RewriteTimes(n.First, oldName, newName, out bool firstChanged);
                TimesNode second = RewriteTimes(n.Second, oldName, newName, out bool secondChanged);
                changed = firstChanged || secondChanged;
                return changed ? new PickNode(n.Kind, first, second) : n;
            }

            case ClampNode n:
            {
                TimesNode source = RewriteTimes(n.Source, oldName, newName, out bool sourceChanged);
                TimesNode low = RewriteTimes(n.Low, oldName, newName, out bool lowChanged);
                TimesNode high = RewriteTimes(n.High, oldName, newName, out bool highChanged);
                changed = sourceChanged || lowChanged || highChanged;
                return changed ? new ClampNode(source, low, high) : n;
            }

            case SegmentRangeSwitchNode n:
            {
                bool any = false;
                var cases = new List<SegmentRangeCase>(n.Cases.Count);
                foreach (SegmentRangeCase c in n.Cases)
                {
                    TimesNode times = RewriteTimes(c.Times, oldName, newName, out bool timesChanged);
                    any |= timesChanged;
                    cases.Add(timesChanged ? new SegmentRangeCase(c.FromSegment, c.ToSegment, times) : c);
                }

                TimesNode otherwise = RewriteTimes(n.Otherwise, oldName, newName, out bool otherwiseChanged);
                changed = any || otherwiseChanged;
                return changed ? new SegmentRangeSwitchNode(cases, otherwise) : n;
            }

            default:
                throw new ArgumentException($"Can't update comparison names inside a {node?.GetType().Name ?? "missing"} block.");
        }
    }

    private static SamplesNode RewriteSamples(SamplesNode node, string oldName, string newName, out bool changed)
    {
        switch (node)
        {
            case SegmentHistoryNode:
                changed = false;
                return node;
            case LastNNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new LastNNode(s, n.Count));
            case CompletedRunsOnlyNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new CompletedRunsOnlyNode(s));
            case RecentDaysNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new RecentDaysNode(s, n.Days));
            case TrimOutliersNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new TrimOutliersNode(s, n.Percent));
            case FinishedUnderNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new FinishedUnderNode(s, n.Limit));
            case AtLeastNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new AtLeastNode(s, n.Count));
            case ReachedSegmentNode n:
                return Rebuild(n, RewriteSamples(n.Source, oldName, newName, out changed), changed, s => new ReachedSegmentNode(s, n.Segment));
            default:
                throw new ArgumentException($"Can't update comparison names inside a {node?.GetType().Name ?? "missing"} block.");
        }
    }

    private static TNode Rebuild<TNode, TChild>(TNode original, TChild child, bool changed, Func<TChild, TNode> create)
        where TNode : Node
    {
        return changed ? create(child) : original;
    }

    private static JsonValue RewriteValue(JsonValue value, string oldName, string newName, out bool changed)
    {
        switch (value)
        {
            case JsonObject obj:
                return RewriteObject(obj, oldName, newName, out changed);
            case JsonArray array:
            {
                bool any = false;
                var items = new List<JsonValue>(array.Items.Count);
                foreach (JsonValue item in array.Items)
                {
                    items.Add(RewriteValue(item, oldName, newName, out bool itemChanged));
                    any |= itemChanged;
                }

                changed = any;
                return any ? new JsonArray(items) : array;
            }

            default:
                changed = false;
                return value;
        }
    }

    private static JsonValue RewriteObject(JsonObject obj, string oldName, string newName, out bool changed)
    {
        bool isComparison = obj["type"] is JsonString type && type.Value == ComparisonBlockType;
        bool any = false;
        var copy = new JsonObject();
        foreach (KeyValuePair<string, JsonValue> member in obj.Members)
        {
            JsonValue next;
            bool memberChanged;
            if (isComparison && member.Key == "fields" && member.Value is JsonObject fields)
            {
                next = RewriteFields(fields, oldName, newName, out memberChanged);
            }
            else
            {
                next = RewriteValue(member.Value, oldName, newName, out memberChanged);
            }

            any |= memberChanged;
            copy.Add(member.Key, next);
        }

        changed = any;
        return any ? copy : obj;
    }

    private static JsonValue RewriteFields(JsonObject fields, string oldName, string newName, out bool changed)
    {
        bool any = false;
        var copy = new JsonObject();
        foreach (KeyValuePair<string, JsonValue> member in fields.Members)
        {
            if (member.Key == "NAME" && member.Value is JsonString name && Same(name.Value, oldName))
            {
                copy.Add("NAME", newName);
                any = true;
                continue;
            }

            copy.Add(member.Key, RewriteValue(member.Value, oldName, newName, out bool childChanged));
            any |= childChanged;
        }

        changed = any;
        return any ? copy : fields;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
