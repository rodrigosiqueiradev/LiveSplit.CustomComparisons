using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Library;
using CustomComparisons.Engine.Model;
using CustomComparisons.Engine.Serialization;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;

namespace LiveSplit.CustomComparisons.Editor;

/// <summary>
/// Answers the editor page's messages (see editor/src/protocol.ts). Everything the page
/// sends is validated again here with the same code that loads layouts, so the page can
/// never save something the component wouldn't accept.
/// </summary>
internal sealed class EditorBridge
{
    private readonly CustomComparisonsComponent component;
    private readonly LiveSplitState state;
    private readonly string initialProgram;

    public EditorBridge(CustomComparisonsComponent component, LiveSplitState state, string initialProgram)
    {
        this.component = component;
        this.state = state;
        this.initialProgram = initialProgram;
    }

    public event EventHandler CloseRequested;

    public bool HasUnsavedChanges { get; private set; }

    /// <summary>True once the editor has saved changes into the component.</summary>
    public bool Saved { get; private set; }

    /// <summary>Handles one message from the page. Returns the reply as JSON, or null for none.</summary>
    public string Handle(string messageJson)
    {
        JsonObject message;
        try
        {
            message = JsonReader.Parse(messageJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        switch ((message?["type"] as JsonString)?.Value)
        {
            case "ready":
                return Write(Init());
            case "preview":
                return Write(Preview(message));
            case "save":
                return Write(Save(message));
            case "dirty":
                HasUnsavedChanges = message["dirty"] is JsonBool { Value: true };
                return null;
            case "close":
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return null;
            default:
                return null;
        }
    }

    private static string Write(JsonObject reply) => JsonWriter.Write(reply, indented: false);

    // ------------------------------------------------------------ init

    private JsonObject Init()
    {
        IRun run = state.Run;
        ComparisonLibrary library = component.Library;

        var programs = new JsonArray(library.Entries
            .Where(e => e.IsReadable)
            .Select(e => (JsonValue)new JsonObject()
                .Add("name", e.Name)
                .Add("doc", JsonReader.Parse(ProgramSerializer.Write(e.Document, indented: false)))));

        List<string> others = component.OtherComparisons().ToList();
        IEnumerable<string> builtIns = run == null
            ? Enumerable.Empty<string>()
            : new StandardComparisonGeneratorsFactory().GetAllGenerators(run).Select(g => g.Name).Where(n => n != "None");
        List<string> choices = new[] { Run.PersonalBestComparisonName }
            .Concat(builtIns)
            .Concat(others)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> taken = others
            .Concat(library.Entries.Where(e => !e.IsReadable).Select(e => e.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new JsonObject()
            .Add("type", "init")
            .Add("programs", programs)
            .Add("initialProgram", initialProgram == null ? (JsonValue)JsonNull.Instance : new JsonString(initialProgram))
            .Add("segments", Strings(run?.Select(s => s.Name) ?? Enumerable.Empty<string>()))
            .Add("comparisons", Strings(choices))
            .Add("takenNames", Strings(taken))
            .Add("reservedNames", Strings(ComparisonNames.Reserved))
            .Add("timingMethod", state.CurrentTimingMethod == TimingMethod.GameTime ? "GameTime" : "RealTime");
    }

    // ------------------------------------------------------------ preview

    private JsonObject Preview(JsonObject message)
    {
        double requestId = (message["requestId"] as JsonNumber)?.Value ?? 0;
        TimingMethod method = (message["timingMethod"] as JsonString)?.Value == "GameTime" ? TimingMethod.GameTime : TimingMethod.RealTime;
        var reply = new JsonObject().Add("type", "previewResult").Add("requestId", requestId);

        IRun run = state.Run;
        if (run == null || run.Count == 0)
        {
            return reply.Add("rows", new JsonArray()).Add("error", JsonNull.Instance);
        }

        ProgramDocument program;
        try
        {
            program = ReadDoc(message["program"]);
        }
        catch (ProgramFormatException e)
        {
            return reply.Add("rows", new JsonArray()).Add("error", e.Message);
        }

        // The other open programs, which the previewed one may use by name. Broken ones are skipped.
        var programs = new List<ComparisonProgram> { program.Program };
        if (message["others"] is JsonArray othersJson)
        {
            foreach (JsonValue other in othersJson.Items)
            {
                try
                {
                    ComparisonProgram p = ReadDoc(other).Program;
                    if (programs.All(x => !string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        programs.Add(p);
                    }
                }
                catch (ProgramFormatException)
                {
                }
            }
        }

        // Never read our own (possibly stale) saved results back from the run.
        var own = new HashSet<string>(programs.Select(p => p.Name).Concat(component.Library.Entries.Select(e => e.Name)), StringComparer.Ordinal);
        var source = new ComparisonSource(run, state.Settings, own);
        // What the coloured differences are measured against: PB unless the editor picked another.
        string compareTo = (message["compareTo"] as JsonString)?.Value;
        if (string.IsNullOrWhiteSpace(compareTo))
        {
            compareTo = Run.PersonalBestComparisonName;
        }

        ComparisonResult result;
        IReadOnlyList<TimeSpan?> reference;
        try
        {
            var evaluator = new ComparisonEvaluator(programs);
            RunData data = RunAdapter.ToRunData(run, method, source);
            result = evaluator.EvaluateWithOrigins(program.Program.Name, data);
            reference = programs.Any(p => p.Name == compareTo)
                ? evaluator.Evaluate(compareTo, data).SplitTimes
                : source.Get(compareTo, method);
        }
        catch (Exception e)
        {
            return reply.Add("rows", new JsonArray()).Add("error", "Unexpected error: " + e.Message);
        }

        var rows = new JsonArray();
        for (int i = 0; i < run.Count; i++)
        {
            rows.Items.Add(new JsonObject()
                .Add("segment", run[i].Name ?? "")
                .Add("segmentMs", Milliseconds(i < result.SegmentTimes.Count ? result.SegmentTimes[i] : null))
                .Add("splitMs", Milliseconds(i < result.SplitTimes.Count ? result.SplitTimes[i] : null))
                .Add("referenceMs", Milliseconds(reference != null && i < reference.Count ? reference[i] : null))
                .Add("bestSegmentMs", Milliseconds(run[i].BestSegmentTime[method]))
                .Add("origin", result.Origins != null && i < result.Origins.Count ? result.Origins[i] : ""));
        }

        reply.Add("compareTo", compareTo);
        return reply.Add("rows", rows).Add("error", result.Error == null ? JsonNull.Instance : new JsonString(result.Error));
    }

    // ------------------------------------------------------------ save

    private JsonObject Save(JsonObject message)
    {
        var reply = new JsonObject().Add("type", "saveResult");
        if (!(message["programs"] is JsonArray items))
        {
            return reply.Add("ok", JsonBool.False).Add("error", "Nothing to save.");
        }

        var documents = new List<ProgramDocument>();
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonValue item in items.Items)
        {
            JsonObject entry = item as JsonObject;
            ProgramDocument document;
            try
            {
                document = ReadDoc(entry?["doc"]);
            }
            catch (ProgramFormatException e)
            {
                return reply.Add("ok", JsonBool.False).Add("error", e.Message);
            }

            documents.Add(document);
            if (entry["originalName"] is JsonString original && original.Value != document.Program.Name)
            {
                renames[original.Value] = document.Program.Name;
            }
        }

        string problem = component.ApplyEditorSave(documents, renames);
        if (problem != null)
        {
            return reply.Add("ok", JsonBool.False).Add("error", problem);
        }

        HasUnsavedChanges = false;
        Saved = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return reply.Add("ok", JsonBool.True).Add("error", JsonNull.Instance);
    }

    // ------------------------------------------------------------ helpers

    private static ProgramDocument ReadDoc(JsonValue value)
    {
        if (value == null)
        {
            throw new ProgramFormatException("A program is missing.");
        }

        return ProgramSerializer.Read(JsonWriter.Write(value, indented: false));
    }

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(v => (JsonValue)new JsonString(v)));

    private static JsonValue Milliseconds(TimeSpan? time) =>
        time.HasValue ? new JsonNumber(Math.Round(time.Value.TotalMilliseconds, 3)) : JsonNull.Instance;
}
