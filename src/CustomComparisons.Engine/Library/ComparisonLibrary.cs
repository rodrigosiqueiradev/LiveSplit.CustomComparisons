using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Serialization;

namespace CustomComparisons.Engine.Library;

/// <summary>
/// One saved comparison. Keeps the exact text it was loaded from, so an entry
/// that can't be read (corrupt, or from a newer version) is saved back untouched
/// instead of being lost.
/// </summary>
public sealed class StoredComparison
{
    private StoredComparison(string json, ProgramDocument document, string loadError)
    {
        Json = json;
        Document = document;
        LoadError = loadError;
    }

    public string Json { get; }

    /// <summary>Null when the entry couldn't be read.</summary>
    public ProgramDocument Document { get; }

    /// <summary>Why the entry couldn't be read, or null.</summary>
    public string LoadError { get; }

    public bool IsReadable => Document != null;

    /// <summary>The program's name, or a best guess for unreadable entries.</summary>
    public string Name => Document?.Program.Name ?? GuessName(Json) ?? "(unreadable comparison)";

    public static StoredComparison Load(string json)
    {
        try
        {
            return new StoredComparison(json, ProgramSerializer.Read(json), null);
        }
        catch (ProgramFormatException e)
        {
            return new StoredComparison(json, null, e.Message);
        }
    }

    public static StoredComparison FromDocument(ProgramDocument document) =>
        new(ProgramSerializer.Write(document, indented: false), document, null);

    internal StoredComparison WithError(string error) => new(Json, null, error);

    private static string GuessName(string json)
    {
        try
        {
            return JsonReader.Parse(json) is JsonObject o && o["name"] is JsonString s && !string.IsNullOrWhiteSpace(s.Value)
                ? s.Value.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The list of comparisons a component owns, with all the rules about names.
/// Pure logic: the LiveSplit component wraps it with file dialogs and registration.
/// </summary>
public sealed class ComparisonLibrary
{
    private readonly List<StoredComparison> entries = new();

    public IReadOnlyList<StoredComparison> Entries => entries;

    /// <summary>Readable programs with unique names, ready to evaluate.</summary>
    public IEnumerable<ComparisonProgram> Programs => entries.Where(e => e.IsReadable).Select(e => e.Document.Program);

    /// <summary>Replaces everything, e.g. when a layout is loaded. Later duplicates are flagged, not dropped.</summary>
    public void Load(IEnumerable<string> jsonEntries)
    {
        entries.Clear();
        foreach (string json in jsonEntries)
        {
            StoredComparison entry = StoredComparison.Load(json);
            if (entry.IsReadable && entries.Any(e => e.IsReadable && SameName(e.Name, entry.Name)))
            {
                entry = entry.WithError($"Another comparison is already called '{entry.Name}'. Rename one of them.");
            }

            entries.Add(entry);
        }
    }

    public IEnumerable<string> SaveAll() => entries.Select(e => e.Json);

    /// <summary>Returns null if <paramref name="name"/> is free, otherwise why not.</summary>
    /// <param name="otherComparisons">Comparisons in the run that this library doesn't own.</param>
    /// <param name="ignoring">The entry being renamed, whose current name doesn't count as taken.</param>
    public string ValidateName(string name, IEnumerable<string> otherComparisons, StoredComparison ignoring = null)
    {
        IEnumerable<string> taken = entries.Where(e => !ReferenceEquals(e, ignoring)).Select(e => e.Name)
            .Concat(otherComparisons ?? Enumerable.Empty<string>());
        return ComparisonNames.Validate(name, taken);
    }

    /// <summary>Adds a program read from a file. Returns null on success, otherwise why not.</summary>
    public string Add(ProgramDocument document, IEnumerable<string> otherComparisons)
    {
        string problem = ValidateName(document.Program.Name, otherComparisons);
        if (problem != null)
        {
            return problem;
        }

        entries.Add(StoredComparison.FromDocument(document));
        return null;
    }

    public string Rename(StoredComparison entry, string newName, IEnumerable<string> otherComparisons)
    {
        int index = IndexOf(entry);
        if (!entry.IsReadable)
        {
            return "This comparison can't be read, so it can't be renamed.";
        }

        string problem = ValidateName(newName, otherComparisons, entry);
        if (problem != null)
        {
            return problem;
        }

        ComparisonProgram old = entry.Document.Program;
        var renamed = new ProgramDocument(new ComparisonProgram(newName.Trim(), old.Root), entry.Document.Workspace);
        entries[index] = StoredComparison.FromDocument(renamed);
        RecheckDuplicates();
        return null;
    }

    /// <summary>
    /// Replaces every readable comparison with <paramref name="documents"/>, as the block editor
    /// saves them. Unreadable entries are kept, so the editor can never destroy something it
    /// couldn't open. All-or-nothing: on any problem nothing changes and the reason is returned.
    /// </summary>
    public string ReplaceReadable(IReadOnlyList<ProgramDocument> documents, IEnumerable<string> otherComparisons)
    {
        List<StoredComparison> unreadable = entries.Where(e => !e.IsReadable).ToList();
        List<string> others = (otherComparisons ?? Enumerable.Empty<string>()).ToList();
        var accepted = new List<string>();

        foreach (ProgramDocument document in documents)
        {
            string name = document.Program.Name;
            IEnumerable<string> taken = accepted
                .Concat(unreadable.Select(e => e.Name))
                .Concat(others);
            string problem = ComparisonNames.Validate(name, taken);
            if (problem != null)
            {
                return problem;
            }

            accepted.Add(name);
        }

        List<string> saved = documents.Select(d => ProgramSerializer.Write(d, indented: false))
            .Concat(unreadable.Select(e => e.Json))
            .ToList();
        Load(saved);
        return null;
    }

    /// <summary>
    /// Moves an entry up (negative) or down (positive) in the list, which is also the order
    /// in LiveSplit's Compare Against menu. Returns false if it's already at that end.
    /// </summary>
    public bool Move(StoredComparison entry, int offset)
    {
        int from = IndexOf(entry);
        int to = Math.Max(0, Math.Min(entries.Count - 1, from + offset));
        if (to == from)
        {
            return false;
        }

        entries.RemoveAt(from);
        entries.Insert(to, entry);
        return true;
    }

    public void Remove(StoredComparison entry)
    {
        entries.RemoveAt(IndexOf(entry));
        RecheckDuplicates();
    }

    /// <summary>A name that isn't taken, based on <paramref name="name"/>: "Pace", "Pace (2)", ...</summary>
    public string SuggestFreeName(string name, IEnumerable<string> otherComparisons)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Custom Comparison" : name.Trim();
        List<string> others = (otherComparisons ?? Enumerable.Empty<string>()).ToList();
        if (ValidateName(baseName, others) == null)
        {
            return baseName;
        }

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (ValidateName(candidate, others) == null)
            {
                return candidate;
            }
        }

        return baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private int IndexOf(StoredComparison entry)
    {
        int index = entries.IndexOf(entry);
        if (index < 0)
        {
            throw new ArgumentException("That comparison isn't in this list.", nameof(entry));
        }

        return index;
    }

    /// <summary>After a rename or delete, a previously-duplicate entry may now be fine.</summary>
    private void RecheckDuplicates()
    {
        List<string> reloaded = entries.Select(e => e.Json).ToList();
        Load(reloaded);
    }

    private static bool SameName(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
