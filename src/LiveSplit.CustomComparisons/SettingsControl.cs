using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Library;
using CustomComparisons.Engine.Serialization;

namespace LiveSplit.CustomComparisons;

/// <summary>
/// Settings page in LiveSplit's Layout Editor: the list of comparisons, whether each one
/// works, and import/export so runners can share them. The block editor's "Open editor"
/// button joins this page in the next milestone.
/// </summary>
internal sealed class SettingsControl : UserControl
{
    private const string FileFilter = "Custom comparison (*.json)|*.json|All files (*.*)|*.*";

    private readonly CustomComparisonsComponent component;
    private readonly ListView list;
    private readonly Button exportButton;
    private readonly Button renameButton;
    private readonly Button deleteButton;
    private readonly Button upButton;
    private readonly Button downButton;
    private readonly Button copyButton;
    private readonly Label notice;

    public SettingsControl(CustomComparisonsComponent component)
    {
        this.component = component;

        Padding = new Padding(7);
        Size = new Size(476, 300);

        var heading = new Label
        {
            Text = "These comparisons appear in LiveSplit's \"Compare Against\" menu. " +
                   "Build them in the block editor, or import a .json file someone shared.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
        };

        // LiveSplit's Layout Settings window puts every component back as it was when you
        // press Cancel, and only keeps changes on OK. Easy to miss, so say so after a change.
        notice = new Label
        {
            Text = "Changes applied. Click OK to close Layout Settings, then save your layout " +
                   "(right-click → Save Layout) to keep them. Cancel would undo them.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 48,
            Padding = new Padding(8, 6, 8, 6),
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Color.FromArgb(0xFF, 0xF4, 0xCE),
            ForeColor = Color.FromArgb(0x3D, 0x30, 0x00),
            Visible = false,
        };

        list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        list.Columns.Add("Comparison", 160);
        list.Columns.Add("Status", 200);
        list.SelectedIndexChanged += (s, e) => UpdateButtons();
        list.DoubleClick += (s, e) => OpenEditor(Selected?.IsReadable == true ? Selected.Name : null);
        // The status column takes whatever width is left, so nothing scrolls sideways.
        list.Resize += (s, e) => list.Columns[1].Width = Math.Max(120, list.ClientSize.Width - list.Columns[0].Width - 4);

        // The main action gets its own row above the list.
        var editorRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 6),
            WrapContents = true,
        };
        Button editorButton = MakeButton("Open block editor…", (s, e) => OpenEditor(Selected?.IsReadable == true ? Selected.Name : null));
        editorButton.Font = new Font(editorButton.Font, FontStyle.Bold);
        var editorHint = new Label
        {
            Text = "or double-click a comparison",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, 8, 0, 0),
        };
        editorRow.Controls.AddRange(new Control[] { editorButton, editorHint });

        // The rest wrap onto more rows if the settings page is narrow.
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 6, 0, 0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        upButton = MakeButton("Move up", (s, e) => MoveSelected(-1));
        downButton = MakeButton("Move down", (s, e) => MoveSelected(1));
        renameButton = MakeButton("Rename…", (s, e) => RenameSelected());
        deleteButton = MakeButton("Delete", (s, e) => DeleteSelected());
        Button importButton = MakeButton("Import…", (s, e) => ImportFile());
        exportButton = MakeButton("Export…", (s, e) => ExportSelected());
        copyButton = MakeButton("Copy as text", (s, e) => CopySelected());
        Button pasteButton = MakeButton("Paste", (s, e) => PasteFromClipboard());
        buttons.Controls.AddRange(new Control[] { upButton, downButton, renameButton, deleteButton });
        buttons.SetFlowBreak(deleteButton, true);
        buttons.Controls.AddRange(new Control[] { importButton, exportButton, copyButton, pasteButton });

        // Docking order: the last added docks first, so the heading ends up on top.
        Controls.Add(list);
        Controls.Add(buttons);
        Controls.Add(editorRow);
        Controls.Add(notice);
        Controls.Add(heading);
        UpdateButtons();
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 28),
            Padding = new Padding(6, 0, 6, 0),
            Margin = new Padding(0, 0, 6, 6),
        };
        button.Click += onClick;
        return button;
    }

    private StoredComparison Selected => list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as StoredComparison : null;

    public void RefreshList()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(new Action(RefreshList));
            return;
        }

        string selectedName = Selected?.Name;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (StoredComparison entry in component.Library.Entries)
        {
            string problem = component.StatusOf(entry);
            var item = new ListViewItem(entry.Name) { Tag = entry };
            item.SubItems.Add(problem ?? "Working");
            if (problem != null)
            {
                item.ForeColor = Color.Firebrick;
            }

            list.Items.Add(item);
            if (entry.Name == selectedName)
            {
                item.Selected = true;
            }
        }

        if (list.Items.Count == 0)
        {
            list.Items.Add(new ListViewItem("(none yet)") { ForeColor = SystemColors.GrayText });
        }

        list.EndUpdate();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        StoredComparison selected = Selected;
        int index = selected == null ? -1 : list.SelectedIndices[0];
        exportButton.Enabled = selected != null;
        copyButton.Enabled = selected != null;
        renameButton.Enabled = selected?.IsReadable == true;
        deleteButton.Enabled = selected != null;
        upButton.Enabled = selected != null && index > 0;
        downButton.Enabled = selected != null && index < component.Library.Entries.Count - 1;
    }

    // ------------------------------------------------------------ Actions

    private void OpenEditor(string initialProgram)
    {
        if (component.OpenEditor(FindForm(), initialProgram))
        {
            ShowChangedNotice();
        }
    }

    /// <summary>Reminds the user that Layout Settings must be closed with OK and the layout saved.</summary>
    public void ShowChangedNotice() => notice.Visible = true;

    /// <summary>Called each time Layout Settings opens, since LiveSplit reuses this control.</summary>
    public void ResetNotice() => notice.Visible = false;

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog { Filter = FileFilter, Title = "Import a custom comparison" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            ShowError("Couldn't import that file", e.Message);
            return;
        }

        ImportText(text, "Couldn't import that file");
    }

    /// <summary>Pastes a comparison someone shared as text, e.g. in Discord.</summary>
    private void PasteFromClipboard()
    {
        string text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowError("Nothing to paste", "Copy a comparison's text first (it starts with {\"format\": \"livesplit-custom-comparison\" …).");
            return;
        }

        ImportText(text.Trim(), "Couldn't paste that");
    }

    /// <summary>Copies the selected comparison as text, ready to paste into a chat.</summary>
    private void CopySelected()
    {
        StoredComparison entry = Selected;
        if (entry == null)
        {
            return;
        }

        // Compact JSON: one line, so chat apps don't mangle it. Unreadable entries go out as stored.
        Clipboard.SetText(entry.IsReadable ? ProgramSerializer.Write(entry.Document, indented: false) : entry.Json);
    }

    private void MoveSelected(int offset)
    {
        StoredComparison entry = Selected;
        if (entry != null && component.Move(entry, offset))
        {
            ShowChangedNotice();
        }
    }

    private void ImportText(string text, string errorTitle)
    {
        ProgramDocument document;
        try
        {
            document = ProgramSerializer.Read(text);
        }
        catch (ProgramFormatException e)
        {
            ShowError(errorTitle, e.Message);
            return;
        }

        string problem = component.Import(document);
        if (problem == null)
        {
            ShowChangedNotice();
            return;
        }

        // Most likely a name clash: offer a free name instead of failing.
        string suggestion = component.Library.SuggestFreeName(document.Program.Name, component.OtherComparisons());
        string newName = NamePrompt.Ask(this, "Import comparison", problem + "\n\nImport it under this name instead:", suggestion);
        if (newName == null)
        {
            return;
        }

        var renamed = new ProgramDocument(
            new ComparisonProgram(newName, document.Program.Root),
            document.Workspace);
        problem = component.Import(renamed);
        if (problem != null)
        {
            ShowError(errorTitle, problem);
        }
        else
        {
            ShowChangedNotice();
        }
    }

    private void ExportSelected()
    {
        StoredComparison entry = Selected;
        if (entry == null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = FileFilter,
            Title = "Export a custom comparison",
            FileName = SafeFileName(entry.Name) + ".json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Unreadable entries are exported exactly as stored, so nothing is lost.
        string text = entry.IsReadable ? ProgramSerializer.Write(entry.Document) : entry.Json;
        try
        {
            File.WriteAllText(dialog.FileName, text + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            ShowError("Couldn't save the file", e.Message);
        }
    }

    private void RenameSelected()
    {
        StoredComparison entry = Selected;
        if (entry?.IsReadable != true)
        {
            return;
        }

        string newName = entry.Name;
        while (true)
        {
            newName = NamePrompt.Ask(this, "Rename comparison", "New name:", newName);
            if (newName == null || newName.Trim() == entry.Name)
            {
                return;
            }

            string problem = component.Rename(entry, newName);
            if (problem == null)
            {
                ShowChangedNotice();
                return;
            }

            ShowError("Can't use that name", problem);
        }
    }

    private void DeleteSelected()
    {
        StoredComparison entry = Selected;
        if (entry == null)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"Delete \"{entry.Name}\"? Export it first if you might want it back.",
            "Delete comparison",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK)
        {
            component.Delete(entry);
            ShowChangedNotice();
        }
    }

    private void ShowError(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "comparison" : cleaned;
    }
}

/// <summary>A minimal text prompt; WinForms has none built in.</summary>
internal static class NamePrompt
{
    public static string Ask(IWin32Window owner, string title, string message, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(380, 150),
            Padding = new Padding(12),
        };

        var label = new Label { Text = message, Dock = DockStyle.Top, AutoSize = false, Height = 60 };
        var box = new TextBox { Text = initial ?? "", Dock = DockStyle.Top, MaxLength = 64 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 36 };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        form.Controls.Add(box);
        form.Controls.Add(label);
        form.Controls.Add(buttons);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (s, e) => box.SelectAll();

        return form.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
