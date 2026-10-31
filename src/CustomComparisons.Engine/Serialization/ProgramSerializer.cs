using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CustomComparisons.Engine.Ast;

namespace CustomComparisons.Engine.Serialization;

/// <summary>A program plus the editor state that produced it.</summary>
public sealed class ProgramDocument
{
    public ProgramDocument(ComparisonProgram program, JsonValue workspace = null)
    {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        Workspace = workspace;
    }

    public ComparisonProgram Program { get; }

    /// <summary>
    /// The block editor's own layout (block positions and so on), kept only so the
    /// program can be re-opened as it was. Never evaluated. Null for hand-written files.
    /// </summary>
    public JsonValue Workspace { get; }
}

/// <summary>
/// Reads and writes the versioned program format. This is what lives in the layout
/// file and in shared .json files.
///
/// <code>
/// {
///   "format": "livesplit-custom-comparison",
///   "version": 1,
///   "name": "Safe Pace",
///   "root": { "type": "fallback", "primary": { ... }, "fallback": { ... } },
///   "workspace": { ... }            // optional, editor-only
/// }
/// </code>
///
/// A file is pure data: it can only describe blocks from the fixed set below.
/// </summary>
public static class ProgramSerializer
{
    public const string FormatId = "livesplit-custom-comparison";
    public const int CurrentVersion = ComparisonProgram.CurrentAstVersion;

    public const int MaxCount = 10_000;
    public const int MaxDays = 36_500;
    public const double MaxTotalSeconds = 360_000; // 100 hours
    public const int MaxSegment = 10_000;
    public const int MaxCases = 100;
    public const double MaxSeconds = 3600;
    public const double MinPercent = 1;
    public const double MaxPercent = 1000;

    // ------------------------------------------------------------------ Writing

    public static string Write(ProgramDocument document, bool indented = true)
    {
        var root = new JsonObject()
            .Add("format", FormatId)
            .Add("version", RequiredVersion(document.Program.Root))
            .Add("name", document.Program.Name)
            .Add("root", WriteNode(document.Program.Root));

        if (document.Workspace != null)
        {
            root.Add("workspace", document.Workspace);
        }

        return JsonWriter.Write(root, indented);
    }

    private static JsonObject WriteNode(Node node)
    {
        switch (node)
        {
            case SegmentHistoryNode:
                return Typed("segmentHistory");
            case ComparisonSourceNode n:
                return Typed("comparison").Add("name", n.ComparisonName);
            case LastNNode n:
                return Typed("lastN").Add("count", n.Count).Add("source", WriteNode(n.Source));
            case CompletedRunsOnlyNode n:
                return Typed("completedRunsOnly").Add("source", WriteNode(n.Source));
            case AggregateNode n:
                return Typed("aggregate").Add("kind", KindName(n.Kind)).Add("source", WriteNode(n.Source));
            case AddPerSegmentNode n:
                return Typed("addTime").Add("seconds", Math.Round(n.Amount.TotalSeconds, 3)).Add("source", WriteNode(n.Source));
            case ScaleNode n:
                return Typed("scale").Add("percent", n.Percent).Add("source", WriteNode(n.Source));
            case FallbackNode n:
                return Typed("fallback").Add("primary", WriteNode(n.Primary)).Add("fallback", WriteNode(n.Fallback));
            case TrimOutliersNode n:
                return Typed("trimOutliers").Add("percent", n.Percent).Add("source", WriteNode(n.Source));
            case FinishedUnderNode n:
                return Typed("finishedUnder").Add("totalSeconds", Math.Round(n.Limit.TotalSeconds, 3)).Add("source", WriteNode(n.Source));
            case AtLeastNode n:
                return Typed("atLeast").Add("count", n.Count).Add("source", WriteNode(n.Source));
            case AttemptNode n:
                return Typed("attempt").Add("number", n.Number);
            case PercentileNode n:
                return Typed("percentile").Add("percent", n.Percent).Add("source", WriteNode(n.Source));
            case RecentDaysNode n:
                return Typed("recentDays").Add("days", n.Days).Add("source", WriteNode(n.Source));
            case StretchToTotalNode n:
                return Typed("stretchToTotal").Add("totalSeconds", Math.Round(n.Total.TotalSeconds, 3)).Add("source", WriteNode(n.Source));
            case BlendNode n:
                return Typed("blend").Add("percent", n.Percent).Add("from", WriteNode(n.From)).Add("to", WriteNode(n.To));
            case PickNode n:
                return Typed("pick").Add("kind", n.Kind == PickKind.Faster ? "faster" : "slower").Add("first", WriteNode(n.First)).Add("second", WriteNode(n.Second));
            case SegmentRangeSwitchNode n:
                return Typed("segmentRange")
                    .Add("cases", new JsonArray(n.Cases.Select(c => (JsonValue)new JsonObject()
                        .Add("from", c.FromSegment)
                        .Add("to", c.ToSegment)
                        .Add("times", WriteNode(c.Times)))))
                    .Add("otherwise", WriteNode(n.Otherwise));
            default:
                throw new ArgumentException($"Cannot save block {node?.GetType().Name ?? "(missing)"}.", nameof(node));
        }
    }

    private static JsonObject Typed(string type) => new JsonObject().Add("type", type);

    /// <summary>The lowest format version that can hold every block in the tree.</summary>
    private static int RequiredVersion(Node node)
    {
        switch (node)
        {
            case AttemptNode:
                return 3;
            case TrimOutliersNode n:
                return Math.Max(3, RequiredVersion(n.Source));
            case FinishedUnderNode n:
                return Math.Max(3, RequiredVersion(n.Source));
            case AtLeastNode n:
                return Math.Max(3, RequiredVersion(n.Source));
            case PercentileNode n:
                return Math.Max(2, RequiredVersion(n.Source));
            case RecentDaysNode n:
                return Math.Max(2, RequiredVersion(n.Source));
            case StretchToTotalNode n:
                return Math.Max(2, RequiredVersion(n.Source));
            case BlendNode n:
                return Math.Max(2, Math.Max(RequiredVersion(n.From), RequiredVersion(n.To)));
            case PickNode n:
                return Math.Max(2, Math.Max(RequiredVersion(n.First), RequiredVersion(n.Second)));
            case LastNNode n:
                return RequiredVersion(n.Source);
            case CompletedRunsOnlyNode n:
                return RequiredVersion(n.Source);
            case AggregateNode n:
                return RequiredVersion(n.Source);
            case AddPerSegmentNode n:
                return RequiredVersion(n.Source);
            case ScaleNode n:
                return RequiredVersion(n.Source);
            case FallbackNode n:
                return Math.Max(RequiredVersion(n.Primary), RequiredVersion(n.Fallback));
            case SegmentRangeSwitchNode n:
                return n.Cases.Select(c => RequiredVersion(c.Times)).Concat(new[] { RequiredVersion(n.Otherwise) }).Max();
            default:
                return 1;
        }
    }

    private static string KindName(AggregateKind kind) => kind switch
    {
        AggregateKind.Min => "min",
        AggregateKind.Max => "max",
        AggregateKind.Average => "average",
        AggregateKind.Median => "median",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // ------------------------------------------------------------------ Reading

    /// <summary>Parses a program file. Throws <see cref="ProgramFormatException"/> with a readable message.</summary>
    public static ProgramDocument Read(string text)
    {
        JsonValue json;
        try
        {
            json = JsonReader.Parse(text);
        }
        catch (JsonException e)
        {
            throw new ProgramFormatException("This isn't valid JSON: " + e.Message);
        }

        JsonObject root = json as JsonObject
            ?? throw new ProgramFormatException("This isn't a Custom Comparisons file.");

        if (!(root["format"] is JsonString format) || format.Value != FormatId)
        {
            throw new ProgramFormatException("This isn't a Custom Comparisons file.");
        }

        int version = ReadInt(root, "version", "", 1, int.MaxValue);
        if (version > CurrentVersion)
        {
            throw new ProgramFormatException("This comparison was made with a newer version of Custom Comparisons. Update the component to use it.");
        }

        // Older versions get migrated here once there are any.

        string name = ReadString(root, "name", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProgramFormatException("The comparison has no name.");
        }

        if (name.Trim().Length > ComparisonNames.MaxLength)
        {
            throw new ProgramFormatException($"The comparison name is longer than {ComparisonNames.MaxLength} characters.");
        }

        TimesNode program = ReadTimes(Required(root, "root", ""), "root");
        JsonValue workspace = root["workspace"];
        return new ProgramDocument(new ComparisonProgram(name, program, version), workspace is JsonNull ? null : workspace);
    }

    private static TimesNode ReadTimes(JsonValue value, string path)
    {
        JsonObject obj = AsBlock(value, path, out string type);
        switch (type)
        {
            case "comparison":
            {
                string name = ReadString(obj, "name", path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw Error(path, "name", "pick a comparison");
                }

                return new ComparisonSourceNode(name);
            }

            case "aggregate":
                return new AggregateNode(ReadKind(obj, path), ReadSamples(Required(obj, "source", path), Join(path, "source")));

            case "addTime":
            {
                double seconds = ReadNumber(obj, "seconds", path, -MaxSeconds, MaxSeconds);
                return new AddPerSegmentNode(ReadTimes(Required(obj, "source", path), Join(path, "source")), TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)));
            }

            case "scale":
                return new ScaleNode(
                    ReadTimes(Required(obj, "source", path), Join(path, "source")),
                    ReadNumber(obj, "percent", path, MinPercent, MaxPercent));

            case "fallback":
                return new FallbackNode(
                    ReadTimes(Required(obj, "primary", path), Join(path, "primary")),
                    ReadTimes(Required(obj, "fallback", path), Join(path, "fallback")));

            case "segmentRange":
            {
                if (!(Required(obj, "cases", path) is JsonArray cases))
                {
                    throw Error(path, "cases", "must be a list");
                }

                if (cases.Items.Count > MaxCases)
                {
                    throw Error(path, "cases", $"can have at most {MaxCases} ranges");
                }

                var parsed = new List<SegmentRangeCase>();
                for (int i = 0; i < cases.Items.Count; i++)
                {
                    string casePath = $"{Join(path, "cases")}[{i}]";
                    if (!(cases.Items[i] is JsonObject c))
                    {
                        throw new ProgramFormatException($"{casePath}: must be an object.");
                    }

                    int from = ReadInt(c, "from", casePath, 1, MaxSegment);
                    int to = ReadInt(c, "to", casePath, 1, MaxSegment);
                    if (to < from)
                    {
                        throw Error(casePath, "to", "must not be before \"from\"");
                    }

                    parsed.Add(new SegmentRangeCase(from, to, ReadTimes(Required(c, "times", casePath), Join(casePath, "times"))));
                }

                return new SegmentRangeSwitchNode(parsed, ReadTimes(Required(obj, "otherwise", path), Join(path, "otherwise")));
            }

            case "attempt":
                return new AttemptNode(ReadInt(obj, "number", path, 1, int.MaxValue));

            case "percentile":
                return new PercentileNode(
                    ReadSamples(Required(obj, "source", path), Join(path, "source")),
                    ReadNumber(obj, "percent", path, 0, 100));

            case "stretchToTotal":
            {
                double total = ReadNumber(obj, "totalSeconds", path, 0.001, MaxTotalSeconds);
                return new StretchToTotalNode(
                    ReadTimes(Required(obj, "source", path), Join(path, "source")),
                    TimeSpan.FromTicks((long)Math.Round(total * TimeSpan.TicksPerSecond)));
            }

            case "blend":
                return new BlendNode(
                    ReadTimes(Required(obj, "from", path), Join(path, "from")),
                    ReadTimes(Required(obj, "to", path), Join(path, "to")),
                    ReadNumber(obj, "percent", path, 0, 100));

            case "pick":
            {
                string kind = ReadString(obj, "kind", path);
                PickKind pick = kind == "faster" ? PickKind.Faster
                    : kind == "slower" ? PickKind.Slower
                    : throw Error(path, "kind", "must be faster or slower");
                return new PickNode(
                    pick,
                    ReadTimes(Required(obj, "first", path), Join(path, "first")),
                    ReadTimes(Required(obj, "second", path), Join(path, "second")));
            }

            case "segmentHistory":
            case "lastN":
            case "completedRunsOnly":
            case "recentDays":
            case "trimOutliers":
            case "finishedUnder":
            case "atLeast":
                throw new ProgramFormatException($"{path}: \"{type}\" gives a list of times per segment. Put it inside a min, max, average, median or percentile block.");

            default:
                throw new ProgramFormatException($"{path}: unknown block \"{type}\".");
        }
    }

    private static SamplesNode ReadSamples(JsonValue value, string path)
    {
        JsonObject obj = AsBlock(value, path, out string type);
        switch (type)
        {
            case "segmentHistory":
                return new SegmentHistoryNode();
            case "lastN":
                return new LastNNode(ReadSamples(Required(obj, "source", path), Join(path, "source")), ReadInt(obj, "count", path, 1, MaxCount));
            case "completedRunsOnly":
                return new CompletedRunsOnlyNode(ReadSamples(Required(obj, "source", path), Join(path, "source")));
            case "recentDays":
                return new RecentDaysNode(ReadSamples(Required(obj, "source", path), Join(path, "source")), ReadInt(obj, "days", path, 1, MaxDays));
            case "trimOutliers":
                return new TrimOutliersNode(
                    ReadSamples(Required(obj, "source", path), Join(path, "source")),
                    ReadNumber(obj, "percent", path, 0, TrimOutliersNode.MaxPercent));
            case "finishedUnder":
            {
                SamplesNode source = ReadSamples(Required(obj, "source", path), Join(path, "source"));
                double limit = ReadNumber(obj, "totalSeconds", path, 0.001, MaxTotalSeconds);
                return new FinishedUnderNode(source, TimeSpan.FromTicks((long)Math.Round(limit * TimeSpan.TicksPerSecond)));
            }
            case "atLeast":
                return new AtLeastNode(ReadSamples(Required(obj, "source", path), Join(path, "source")), ReadInt(obj, "count", path, 1, MaxCount));
            case "attempt":
            case "comparison":
            case "aggregate":
            case "percentile":
            case "addTime":
            case "scale":
            case "stretchToTotal":
            case "blend":
            case "pick":
            case "fallback":
            case "segmentRange":
                throw new ProgramFormatException($"{path}: \"{type}\" gives one time per segment, but this slot needs a list of times (segment history, last N, or completed runs only).");
            default:
                throw new ProgramFormatException($"{path}: unknown block \"{type}\".");
        }
    }

    private static AggregateKind ReadKind(JsonObject obj, string path)
    {
        string kind = ReadString(obj, "kind", path);
        return kind switch
        {
            "min" => AggregateKind.Min,
            "max" => AggregateKind.Max,
            "average" => AggregateKind.Average,
            "median" => AggregateKind.Median,
            _ => throw Error(path, "kind", "must be min, max, average or median"),
        };
    }

    // ------------------------------------------------------------------ Helpers

    private static JsonObject AsBlock(JsonValue value, string path, out string type)
    {
        if (!(value is JsonObject obj))
        {
            throw new ProgramFormatException($"{path}: expected a block.");
        }

        if (!(obj["type"] is JsonString t))
        {
            throw new ProgramFormatException($"{path}: the block has no \"type\".");
        }

        type = t.Value;
        return obj;
    }

    private static JsonValue Required(JsonObject obj, string key, string path)
    {
        JsonValue value = obj[key];
        if (value == null || value is JsonNull)
        {
            throw Error(path, key, "is missing");
        }

        return value;
    }

    private static string ReadString(JsonObject obj, string key, string path)
    {
        if (!(Required(obj, key, path) is JsonString s))
        {
            throw Error(path, key, "must be text");
        }

        return s.Value;
    }

    private static double ReadNumber(JsonObject obj, string key, string path, double min, double max)
    {
        if (!(Required(obj, key, path) is JsonNumber n) || n.Value < min || n.Value > max)
        {
            throw Error(path, key, $"must be a number from {Format(min)} to {Format(max)}");
        }

        return n.Value;
    }

    private static int ReadInt(JsonObject obj, string key, string path, int min, int max)
    {
        if (!(Required(obj, key, path) is JsonNumber n) || n.Value != Math.Floor(n.Value) || n.Value < min || n.Value > max)
        {
            throw Error(path, key, max == int.MaxValue ? $"must be a whole number of at least {min}" : $"must be a whole number from {min} to {max}");
        }

        return (int)n.Value;
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : path + "." + key;

    private static ProgramFormatException Error(string path, string key, string problem) =>
        new($"{Join(path, key)}: {problem}.");

    private static string Format(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>A program file that can't be read. The message is meant for the user.</summary>
public sealed class ProgramFormatException : Exception
{
    public ProgramFormatException(string message) : base(message)
    {
    }
}
