using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HlsRecreation
{
    // Library state intentionally has a separate lifetime from a project.  The only
    // project mutation this form can return is an explicit Custom import.
    public sealed class EffectLibraryDialog : Form
    {
        public readonly ListBox libraryItems = new ListBox();
        public readonly TextBox libraryName = new TextBox();
        public readonly TextBox itemName = new TextBox();
        public readonly Label libraryPath = new Label { AutoSize = true }, status = new Label { AutoSize = true };
        public readonly Button makeNewLibrary = new Button { Text = "Make New Library", AutoSize = true }, openLibrary = new Button { Text = "Open…", AutoSize = true }, saveLibrary = new Button { Text = "Save", AutoSize = true }, saveLibraryAs = new Button { Text = "Save As…", AutoSize = true };
        public readonly Button renameItem = new Button { Text = "Rename", AutoSize = true }, deleteItem = new Button { Text = "Delete", AutoSize = true }, captureSelection = new Button { Text = "Capture selection", AutoSize = true }, addProjectCustom = new Button { Text = "Add project Custom", AutoSize = true }, importCustom = new Button { Text = "Import Custom", AutoSize = true }, insertGroup = new Button { Text = "Insert group", AutoSize = true };
        public readonly ComboBox projectCustomDefinitions = new ComboBox();
        public readonly SerialDefinitionEditor customEditor = new SerialDefinitionEditor();
        public readonly DataGridView partGrid;
        public readonly TextBox definitionName;
        private Project candidate;
        private readonly Selection selection;
        private EffectLibrary library;
        private string path;
        private bool dirty, changing;
        private LibraryItem editingItem;
        private string editingFingerprint;
        private bool projectChanged;
        private LibraryItem pendingInsert;

        public Project Result { get { return candidate; } }
        public bool ProjectChanged { get { return projectChanged; } }
        public LibraryItem PendingInsert { get { return pendingInsert; } }
        public EffectLibrary Library { get { return library; } }
        public string CurrentPath { get { return path; } }

        public EffectLibraryDialog(Project project, Selection chosen)
        {
            if (project == null) throw new ArgumentNullException("project"); if (chosen == null) throw new ArgumentNullException("chosen");
            candidate = Model.Clone(project); selection = chosen; library = EffectLibraries.Empty("Untitled Library");
            Text = "Effect Library"; Width = 1020; Height = 680; MinimumSize = new Size(900, 580); StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; ShowInTaskbar = false;
            partGrid = customEditor.partGrid; definitionName = customEditor.definitionName;
            Build(); BindItems(null); UpdateCaption();
        }

        private void Build()
        {
            FlowLayoutPanel commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(5), WrapContents = false };
            commands.Controls.Add(makeNewLibrary); commands.Controls.Add(openLibrary); commands.Controls.Add(saveLibrary); commands.Controls.Add(saveLibraryAs); commands.Controls.Add(new Label { Text = " Library name", AutoSize = true, Margin = new Padding(12, 7, 2, 0) }); libraryName.Width = 165; commands.Controls.Add(libraryName); commands.Controls.Add(libraryPath);
            makeNewLibrary.Click += delegate { Guard(delegate { NewLibrary(); }); }; openLibrary.Click += delegate { ChooseOpen(); }; saveLibrary.Click += delegate { ChooseSave(false); }; saveLibraryAs.Click += delegate { ChooseSave(true); };
            libraryName.TextChanged += delegate { if (!changing && library != null && library.Name != libraryName.Text) { library.Name = libraryName.Text; MarkDirty(); } };

            libraryItems.Dock = DockStyle.Fill; libraryItems.FormattingEnabled = true; libraryItems.Format += delegate(object sender, ListControlConvertEventArgs e) { LibraryItem item = e.ListItem as LibraryItem; e.Value = item == null ? "" : item.Kind + "  —  " + item.Name; }; libraryItems.SelectedIndexChanged += ItemChanged;
            FlowLayoutPanel itemButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 145, AutoScroll = true };
            itemButtons.Controls.Add(new Label { Text = "Item name", AutoSize = true, Margin = new Padding(2, 7, 0, 0) }); itemName.Width = 130; itemButtons.Controls.Add(itemName);
            itemButtons.Controls.Add(renameItem); itemButtons.Controls.Add(deleteItem); itemButtons.Controls.Add(captureSelection);
            projectCustomDefinitions.DropDownStyle = ComboBoxStyle.DropDownList; projectCustomDefinitions.Width = 135; itemButtons.Controls.Add(projectCustomDefinitions); itemButtons.Controls.Add(addProjectCustom); itemButtons.Controls.Add(importCustom); itemButtons.Controls.Add(insertGroup);
            renameItem.Click += delegate { Guard(delegate { RenameSelected(); }); }; deleteItem.Click += delegate { Guard(delegate { DeleteSelected(); }); }; captureSelection.Click += delegate { Guard(delegate { CaptureFromUi(); }); }; addProjectCustom.Click += delegate { Guard(delegate { AddFirstProjectCustom(); }); }; importCustom.Click += delegate { Guard(delegate { ImportSelectedCustom(); }); }; insertGroup.Click += delegate { Guard(delegate { ChooseInsert(); }); };
            Panel left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) }; left.Controls.Add(libraryItems); left.Controls.Add(itemButtons);
            GroupBox editorBox = new GroupBox { Text = "Stored custom definition", Dock = DockStyle.Fill, Padding = new Padding(3) }; editorBox.Controls.Add(customEditor);
            TableLayoutPanel split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); split.Controls.Add(left, 0, 0); split.Controls.Add(editorBox, 1, 0);
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 42 }; status.Margin = new Padding(9, 11, 0, 0); bottom.Controls.Add(status); FlowLayoutPanel right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 180, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) }; Button cancel = new Button { Text = "Cancel", AutoSize = true }; Button close = new Button { Text = "Close", AutoSize = true };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); }; close.Click += delegate { DialogResult = DialogResult.OK; Close(); }; right.Controls.Add(cancel); right.Controls.Add(close); bottom.Controls.Add(right); CancelButton = cancel;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            commands.Dock = DockStyle.Fill; split.Dock = DockStyle.Fill; bottom.Dock = DockStyle.Fill;
            root.Controls.Add(commands, 0, 0); root.Controls.Add(split, 0, 1); root.Controls.Add(bottom, 0, 2); Controls.Add(root);
            FormClosing += OnLibraryClosing; BindProjectDefinitions();
        }

        private void ItemChanged(object sender, EventArgs e)
        {
            if (changing) return;
            try { FlushStoredCustom(); }
            catch (Exception error)
            {
                changing = true; if (editingItem != null) libraryItems.SelectedItem = editingItem; changing = false;
                Error(error); return;
            }
            LibraryItem item = Current(); bool custom = item != null && item.Kind == "Custom";
            customEditor.Visible = custom; editingItem = custom ? item : null; if (custom) customEditor.LoadDefinition(item.Definition); else customEditor.LoadDefinition(null); editingFingerprint = custom ? Fingerprint(item.Definition) : null; itemName.Text = item == null ? "" : item.Name;
            importCustom.Enabled = custom; insertGroup.Enabled = item != null && item.Kind == "Group"; renameItem.Enabled = deleteItem.Enabled = item != null;
        }
        private LibraryItem Current() { return libraryItems.SelectedItem as LibraryItem; }
        private void BindItems(string wantedId)
        {
            changing = true; libraryItems.Items.Clear(); if (library.Items != null) foreach (LibraryItem item in library.Items) libraryItems.Items.Add(item);
            int selected = -1; for (int i = 0; i < libraryItems.Items.Count; i++) if (((LibraryItem)libraryItems.Items[i]).Id == wantedId) { selected = i; break; }
            if (selected >= 0) libraryItems.SelectedIndex = selected; else if (libraryItems.Items.Count > 0) libraryItems.SelectedIndex = 0;
            changing = false; ItemChanged(this, EventArgs.Empty);
        }
        private void FlushStoredCustom()
        {
            LibraryItem item = editingItem; if (item == null || item.Kind != "Custom" || changing) return;
            customEditor.Flush(); item.Definition = CustomEffects.Clone(customEditor.Definition); string after = Fingerprint(item.Definition); if (after != editingFingerprint) MarkDirty(); editingFingerprint = after;
        }
        private void MarkDirty() { dirty = true; UpdateCaption(); }
        private void UpdateCaption()
        {
            libraryName.Text = library == null ? "" : library.Name; libraryPath.Text = String.IsNullOrEmpty(path) ? "(not saved)" : Path.GetFileName(path); Text = "Effect Library" + (dirty ? " *" : "");
        }
        private void NewLibrary()
        {
            FlushStoredCustom(); if (!ConfirmReplace()) return; library = EffectLibraries.Empty("Untitled Library"); path = null; dirty = false; pendingInsert = null; BindItems(null); UpdateCaption(); status.Text = "New empty library.";
        }
        public void OpenLibrary(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentException("Library path is required.", "filePath"); FlushStoredCustom(); if (!ConfirmReplace()) return;
            EffectLibrary loaded = EffectLibraries.Load(filePath); library = loaded; path = Path.GetFullPath(filePath); dirty = false; pendingInsert = null; BindItems(null); UpdateCaption(); status.Text = "Opened " + Path.GetFileName(path) + ".";
        }
        public void SaveLibrary(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentException("Library path is required.", "filePath"); FlushStoredCustom(); library.Name = libraryName.Text.Trim(); EffectLibraries.RequireValid(library);
            string full = Path.GetFullPath(filePath); EffectLibraries.Save(library, full); path = full; dirty = false; UpdateCaption(); status.Text = "Saved " + Path.GetFileName(path) + ".";
        }
        private void ChooseOpen()
        {
            using (OpenFileDialog dialog = new OpenFileDialog()) { dialog.Filter = "HLS-style effect libraries (*.hlsrlib)|*.hlsrlib"; if (dialog.ShowDialog(this) == DialogResult.OK) Guard(delegate { OpenLibrary(dialog.FileName); }); }
        }
        private bool ChooseSave(bool forceAs)
        {
            try
            {
                if (!forceAs && !String.IsNullOrEmpty(path)) { SaveLibrary(path); return true; }
                using (SaveFileDialog dialog = new SaveFileDialog()) { dialog.Filter = "HLS-style effect libraries (*.hlsrlib)|*.hlsrlib"; dialog.FileName = String.IsNullOrEmpty(path) ? library.Name + ".hlsrlib" : Path.GetFileName(path); if (dialog.ShowDialog(this) != DialogResult.OK) return false; SaveLibrary(dialog.FileName); return true; }
            }
            catch (Exception error) { status.Text = "Save failed: " + error.Message; if (ProjectDialog.SuppressErrorDialogs) throw; MessageBox.Show(this, error.Message, Text); return false; }
        }
        private bool ConfirmReplace()
        {
            if (!dirty) return true; if (ProjectDialog.SuppressErrorDialogs) return false;
            DialogResult response = MessageBox.Show(this, "Save changes to this effect library before replacing it?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (response == DialogResult.Cancel) return false; if (response == DialogResult.Yes) return ChooseSave(false); return true;
        }
        private void OnLibraryClosing(object sender, FormClosingEventArgs e)
        {
            try { FlushStoredCustom(); } catch (Exception error) { e.Cancel = true; Error(error); return; }
            if (!dirty) return; if (ProjectDialog.SuppressErrorDialogs) { e.Cancel = true; return; }
            DialogResult response = MessageBox.Show(this, "Save changes to this effect library before closing?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (response == DialogResult.Cancel) { e.Cancel = true; return; } if (response == DialogResult.Yes && !ChooseSave(false)) e.Cancel = true;
        }
        public void CaptureSelection(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("A library item name is required.", "name"); FlushStoredCustom(); LibraryItem item = EffectLibraries.Capture(candidate, selection, name.Trim()); library.Items.Add(item); MarkDirty(); BindItems(item.Id); status.Text = "Captured the selected rectangle as an independent group.";
        }
        private void CaptureFromUi() { string name = String.IsNullOrWhiteSpace(itemName.Text) ? NextItemName("Effect group") : itemName.Text.Trim(); CaptureSelection(name); }
        public void SaveDefinition(string definitionId, string name)
        {
            if (String.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("A custom definition is required.", "definitionId"); if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("A library item name is required.", "name"); FlushStoredCustom();
            CustomDefinition definition = candidate.CustomEffects.Find(delegate(CustomDefinition d) { return d != null && d.Id == definitionId; }); if (definition == null) throw new ArgumentException("The selected project custom definition no longer exists.");
            LibraryItem item = EffectLibraries.FromCustom(definition, name.Trim()); library.Items.Add(item); MarkDirty(); BindItems(item.Id); status.Text = "Saved an independent Custom definition to this library.";
        }
        private void AddFirstProjectCustom()
        {
            CustomDefinition selected = projectCustomDefinitions.SelectedItem as CustomDefinition;
            if (selected == null) throw new ArgumentException("This project has no saved Custom definitions.");
            string name = String.IsNullOrWhiteSpace(itemName.Text) ? NextItemName(selected.Name) : itemName.Text.Trim(); SaveDefinition(selected.Id, name);
        }
        private void RenameSelected()
        {
            FlushStoredCustom(); LibraryItem item = Current(); if (item == null) throw new ArgumentException("Select a library item first.");
            // The name box is an explicit editable surface, avoiding a hidden modal prompt.
            if (String.IsNullOrWhiteSpace(itemName.Text)) itemName.Text = item.Name; else { item.Name = itemName.Text.Trim(); MarkDirty(); BindItems(item.Id); status.Text = "Renamed library item."; }
        }
        private void DeleteSelected()
        {
            FlushStoredCustom(); LibraryItem item = Current(); if (item == null) return; library.Items.Remove(item); MarkDirty(); BindItems(null); status.Text = "Deleted library item.";
        }
        private void ImportSelectedCustom()
        {
            FlushStoredCustom(); LibraryItem item = Current(); if (item == null || item.Kind != "Custom" || item.Definition == null) throw new ArgumentException("Select a stored Custom definition first.");
            CustomDefinition copy = CustomEffects.Clone(item.Definition); copy.Id = Model.NewId(); copy.Name = UniqueProjectDefinitionName(copy.Name); candidate.CustomEffects.Add(copy); Model.RequireValid(candidate); projectChanged = true; status.Text = "Imported Custom definition as " + copy.Name + "."; BindProjectDefinitions();
        }
        private string UniqueProjectDefinitionName(string basis)
        {
            string candidateName = String.IsNullOrWhiteSpace(basis) ? "Custom Effect" : basis; string prefix = candidateName; int number = 2;
            while (candidate.CustomEffects.Exists(delegate(CustomDefinition d) { return d != null && String.Equals(d.Name, candidateName, StringComparison.OrdinalIgnoreCase); })) candidateName = prefix + " " + number++;
            return candidateName;
        }
        private void ChooseInsert()
        {
            FlushStoredCustom(); LibraryItem item = Current(); if (item == null || item.Kind != "Group") throw new ArgumentException("Select a Group library item to insert.");
            // Clone via the library clone path so the pending stamp cannot become a live link.
            EffectLibrary copy = EffectLibraries.Clone(library); LibraryItem independent = copy.Items.Find(delegate(LibraryItem x) { return x != null && x.Id == item.Id; }); if (independent == null) throw new InvalidOperationException("Unable to copy the selected library item.");
            pendingInsert = independent; DialogResult = DialogResult.OK; Close();
        }
        private string NextItemName(string prefix)
        {
            string baseName = String.IsNullOrWhiteSpace(prefix) ? "Library item" : prefix.Trim(); string answer = baseName; int number = 2;
            while (library.Items.Exists(delegate(LibraryItem x) { return x != null && String.Equals(x.Name, answer, StringComparison.OrdinalIgnoreCase); })) answer = baseName + " " + number++;
            return answer;
        }
        private void BindProjectDefinitions()
        {
            projectCustomDefinitions.Items.Clear(); foreach (CustomDefinition definition in candidate.CustomEffects) projectCustomDefinitions.Items.Add(definition);
            projectCustomDefinitions.FormattingEnabled = true; projectCustomDefinitions.Format -= ProjectDefinitionFormat; projectCustomDefinitions.Format += ProjectDefinitionFormat;
            if (projectCustomDefinitions.Items.Count > 0) projectCustomDefinitions.SelectedIndex = 0;
        }
        private void ProjectDefinitionFormat(object sender, ListControlConvertEventArgs e) { CustomDefinition definition = e.ListItem as CustomDefinition; e.Value = definition == null ? "" : definition.Name; }
        private static string Fingerprint(CustomDefinition value)
        {
            if (value == null) return ""; StringBuilder text = new StringBuilder(); text.Append(value.Id).Append('\n').Append(value.Name).Append('\n');
            if (value.Parts == null) return text.Append("null").ToString();
            for (int i = 0; i < value.Parts.Count; i++) { CustomPart part = value.Parts[i]; if (part == null) text.Append("null"); else text.Append(part.Kind).Append('|').Append(part.LengthCells).Append('|').Append(part.StartLevel.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(part.EndLevel.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(part.ColorHtml); text.Append('\n'); }
            return text.ToString();
        }
        private void Guard(EventHandler action) { try { action(this, EventArgs.Empty); } catch (Exception error) { Error(error); } }
        private void Error(Exception error) { status.Text = error.Message; if (ProjectDialog.SuppressErrorDialogs) throw error; MessageBox.Show(this, error.Message, Text); }
    }
}
