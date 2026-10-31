using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using CustomComparisons.Engine;
using CustomComparisons.Engine.Evaluation;
using CustomComparisons.Engine.Library;
using CustomComparisons.Engine.Serialization;
using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.CustomComparisons;

/// <summary>
/// Invisible layout component that keeps one comparison generator per saved program
/// registered on whatever run is currently loaded.
/// </summary>
public sealed class CustomComparisonsComponent : LogicComponent
{
    // 1 = milestone 1 (nothing saved). 2 = programs saved as JSON.
    private const int SettingsVersion = 2;

    private readonly LiveSplitState state;
    private readonly ComparisonLibrary library = new();
    private readonly List<ProgramComparisonGenerator> generators = new();
    private readonly Dictionary<string, string> blockedReasons = new(StringComparer.Ordinal);
    private readonly SettingsControl settingsControl;
    private IRun attachedRun;

    public CustomComparisonsComponent(LiveSplitState state)
    {
        this.state = state;
        ContextMenuControls = new Dictionary<string, Action>();

        // A freshly added component starts with one example so it does something
        // right away. Loading a layout (SetSettings) replaces this.
        library.Load(new[] { ProgramSerializer.Write(new ProgramDocument(SamplePrograms.MedianOfLast10Plus1s()), indented: false) });

        settingsControl = new SettingsControl(this);
        Rebuild();
    }

    public override string ComponentName => CustomComparisonsFactory.Name;

    internal ComparisonLibrary Library => library;

    public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        Attach(state);
    }

    // ------------------------------------------------------------ Registration

    /// <summary>
    /// Called every frame, so it must be cheap when nothing changed. Handles three cases:
    /// a different splits file was loaded (new run object, our generators missing), the
    /// run was replaced by an edited clone (LiveSplit copies the generator list, so our
    /// generators are present but point at the old run), and the first frame.
    /// </summary>
    private void Attach(LiveSplitState state)
    {
        IRun run = state?.Run;
        if (run == null)
        {
            return;
        }

        bool changed = false;
        if (!ReferenceEquals(run, attachedRun))
        {
            Detach(attachedRun);
            attachedRun = run;
            RefreshBlockedNames(run);
            changed = true;
        }

        foreach (ProgramComparisonGenerator generator in ActiveGenerators())
        {
            if (!ReferenceEquals(generator.Run, run))
            {
                generator.Run = run;
                changed = true;
            }

            if (!run.ComparisonGenerators.Contains(generator))
            {
                run.ComparisonGenerators.Add(generator);
                changed = true;
            }
        }

        if (changed)
        {
            // LiveSplit only regenerates comparisons on its own events (like a reset),
            // so a freshly added generator has to be run once by hand.
            foreach (ProgramComparisonGenerator generator in ActiveGenerators())
            {
                generator.Generate(state.Settings);
            }

            settingsControl.RefreshList();
        }
    }

    private IEnumerable<ProgramComparisonGenerator> ActiveGenerators() =>
        generators.Where(g => !blockedReasons.ContainsKey(g.Name));

    private void Detach(IRun run)
    {
        if (run == null)
        {
            return;
        }

        foreach (ProgramComparisonGenerator generator in generators)
        {
            run.ComparisonGenerators.Remove(generator);
        }
    }

    /// <summary>Throws away all generators and registers the library's current programs.</summary>
    /// <param name="fixSelection">
    /// After a user action, switch away from a comparison that no longer exists. Not done
    /// while a layout loads, when our comparisons may simply not be registered yet.
    /// </param>
    private void Rebuild(bool fixSelection = false)
    {
        Detach(attachedRun);
        Detach(state?.Run);
        attachedRun = null;

        var programs = library.Programs.ToList();
        var evaluator = new ComparisonEvaluator(programs);
        var ownNames = new HashSet<string>(programs.Select(p => p.Name), StringComparer.Ordinal);
        generators.Clear();
        generators.AddRange(programs.Select(p => new ProgramComparisonGenerator(state?.Run, p.Name, evaluator, ownNames)));

        Attach(state);

        // If the selected comparison just disappeared, fall back like LiveSplit does.
        if (fixSelection && state?.Run != null && !state.Run.Comparisons.Contains(state.CurrentComparison))
        {
            state.CurrentComparison = Run.PersonalBestComparisonName;
        }

        settingsControl.RefreshList();
    }

    /// <summary>A program whose name clashes with a comparison already in this run is not registered.</summary>
    private void RefreshBlockedNames(IRun run)
    {
        blockedReasons.Clear();
        List<string> others = OtherComparisons(run).ToList();
        foreach (ProgramComparisonGenerator generator in generators)
        {
            string problem = ComparisonNames.Validate(generator.Name, others);
            if (problem != null)
            {
                blockedReasons[generator.Name] = problem;
            }
        }
    }

    /// <summary>Comparisons in the run that aren't ours: PB, enabled built-ins, hand-made ones.</summary>
    internal IEnumerable<string> OtherComparisons(IRun run = null)
    {
        run ??= state?.Run;
        if (run == null)
        {
            return Enumerable.Empty<string>();
        }

        var ours = new HashSet<string>(generators.Select(g => g.Name), StringComparer.Ordinal);
        return run.Comparisons.Where(name => !ours.Contains(name)).ToList();
    }

    // ------------------------------------------------------------ Used by the settings page

    internal string StatusOf(StoredComparison entry)
    {
        if (!entry.IsReadable)
        {
            return entry.LoadError;
        }

        if (blockedReasons.TryGetValue(entry.Name, out string reason))
        {
            return reason;
        }

        return generators.FirstOrDefault(g => g.Name == entry.Name)?.LastError;
    }

    internal string Import(ProgramDocument document)
    {
        string problem = library.Add(document, OtherComparisons());
        if (problem == null)
        {
            Rebuild(fixSelection: true);
        }

        return problem;
    }

    internal string Rename(StoredComparison entry, string newName)
    {
        bool wasSelected = state != null && state.CurrentComparison == entry.Name;
        string problem = library.Rename(entry, newName, OtherComparisons());
        if (problem == null)
        {
            Rebuild(fixSelection: !wasSelected);
            if (wasSelected)
            {
                state.CurrentComparison = newName.Trim();
            }
        }

        return problem;
    }

    /// <summary>Moves a comparison in the list, which is also its place in Compare Against.</summary>
    internal bool Move(StoredComparison entry, int offset)
    {
        if (!library.Move(entry, offset))
        {
            return false;
        }

        // Re-adding the generators in the new order is what reorders LiveSplit's menu.
        Rebuild();
        return true;
    }

    internal void Delete(StoredComparison entry)
    {
        library.Remove(entry);
        Rebuild(fixSelection: true);
    }

    /// <summary>Opens the block editor as a dialog over <paramref name="owner"/>. Returns true if it saved.</summary>
    internal bool OpenEditor(IWin32Window owner, string initialProgram = null)
    {
        var bridge = new Editor.EditorBridge(this, state, initialProgram);
        using var form = new Editor.EditorForm(bridge);
        form.ShowDialog(owner);
        settingsControl.RefreshList();
        return bridge.Saved;
    }

    /// <summary>
    /// Applies everything the editor saved. <paramref name="renames"/> maps old names to new
    /// ones, so a renamed comparison that's currently selected stays selected.
    /// </summary>
    internal string ApplyEditorSave(IReadOnlyList<ProgramDocument> documents, IReadOnlyDictionary<string, string> renames)
    {
        string selected = state?.CurrentComparison;
        string problem = library.ReplaceReadable(documents, OtherComparisons());
        if (problem != null)
        {
            return problem;
        }

        bool followRename = selected != null && renames.ContainsKey(selected);
        Rebuild(fixSelection: !followRename);
        if (followRename && renames.TryGetValue(selected, out string newName))
        {
            state.CurrentComparison = newName;
        }

        return null;
    }

    // ------------------------------------------------------------ Settings

    public override Control GetSettingsControl(LayoutMode mode)
    {
        settingsControl.ResetNotice();
        settingsControl.RefreshList();
        return settingsControl;
    }

    public override XmlNode GetSettings(XmlDocument document)
    {
        XmlElement root = document.CreateElement("Settings");

        XmlElement version = document.CreateElement("Version");
        version.InnerText = SettingsVersion.ToString();
        root.AppendChild(version);

        XmlElement comparisons = document.CreateElement("Comparisons");
        foreach (string json in library.SaveAll())
        {
            XmlElement comparison = document.CreateElement("Comparison");
            comparison.InnerText = json;
            comparisons.AppendChild(comparison);
        }

        root.AppendChild(comparisons);
        return root;
    }

    public override void SetSettings(XmlNode settings)
    {
        // Milestone-1 layouts have no <Comparisons>: keep the example they were using.
        XmlNode comparisons = settings?["Comparisons"];
        if (comparisons == null)
        {
            return;
        }

        library.Load(comparisons.ChildNodes
            .OfType<XmlElement>()
            .Where(e => e.Name == "Comparison")
            .Select(e => e.InnerText));
        Rebuild();
    }

    public override void Dispose()
    {
        Detach(state?.Run);
        Detach(attachedRun);

        if (state != null && generators.Any(g => g.Name == state.CurrentComparison))
        {
            state.CurrentComparison = Run.PersonalBestComparisonName;
        }

        settingsControl.Dispose();
    }
}
