using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class PreviewLayoutDialog : ProjectDialog
    {
        public LayoutCanvas Canvas { get; private set; }
        public readonly ListBox ElementList = new ListBox(), RawList = new ListBox();
        public readonly DataGridView MappingGrid = new DataGridView();
        public readonly TextBox ElementName = new TextBox();
        public readonly NumericUpDown Group = N(1, 32768), Number = N(1, 32768), Nodes = N(1, 32768), PixelSize = N(0, 1024), Rows = N(1, 32), Columns = N(1, 32), BackgroundOpacity = N(0, 100);
        public readonly CheckBox Serpentine = new CheckBox { Text = "Serpentine", AutoSize = true }, PolylineClosed = new CheckBox { Text = "Closed", AutoSize = true };
        public readonly TextBox BackgroundPath = new TextBox();
        public readonly Label Diagnostics = new Label();
        private bool binding;
        private string editingId;

        public PreviewLayoutDialog(Project project) : base(project)
        {
            Text = "Create / Edit Preview Layout"; Width = 1180; Height = 720; MinimumSize = new Size(1000, 650);
            Canvas = new LayoutCanvas { Project = working, EditMode = true, Dock = DockStyle.Fill, Name = "previewLayoutCanvas" };
            Canvas.BeforeEdit = delegate { return TryFlushElement(); };
            Canvas.LayoutChanged += delegate { if (!binding) { BindElements(); UpdateDiagnostics(); } };
            Canvas.NodeSelected += delegate(object sender, PreviewNodeEventArgs e) { SelectRaw(e.ChannelIds); };
            ElementList.FormattingEnabled = true; ElementList.Width = 165; ElementList.Format += delegate(object s, ListControlConvertEventArgs e) { PreviewElement x = e.ListItem as PreviewElement; e.Value = x == null ? "" : x.Name + "  (G" + x.Group + " N" + x.Number + ")"; };
            ElementList.SelectedIndexChanged += delegate { if (!binding) { if (!TryFlushElement()) { RestoreEditing(); return; } LoadElement(ElementList.SelectedItem as PreviewElement); } };
            RawList.SelectionMode = SelectionMode.MultiExtended; RawList.FormattingEnabled = true; RawList.Format += delegate(object s, ListControlConvertEventArgs e) { RawChannel x = e.ListItem as RawChannel; e.Value = x == null ? "" : x.OutputOrder.ToString("0000", CultureInfo.InvariantCulture) + "  " + x.Name + "  U" + x.Universe + ":" + x.Address; };
            MappingGrid.Dock = DockStyle.Fill; MappingGrid.ReadOnly = true; MappingGrid.AllowUserToAddRows = MappingGrid.AllowUserToDeleteRows = false; MappingGrid.AutoGenerateColumns = false; MappingGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Node", HeaderText = "Node", Width = 45 }); MappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mapping", HeaderText = "G / N / Pixel", Width = 110 }); MappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Channel", HeaderText = "Assigned raw channel(s)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            MappingGrid.SelectionChanged += delegate { if (binding || MappingGrid.CurrentRow == null) return; int node = Int(MappingGrid.CurrentRow.Cells["Node"].Value, 1) - 1; PreviewElement element = Current(); if (element != null) SelectRaw(PreviewLayouts.ChannelsForNode(working, element, node)); };

            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 68, WrapContents = true, AutoScroll = false, Name = "previewTools" };
            foreach (string tool in new string[] { "Select", "Point", "Line", "Arch", "Rectangle", "Matrix", "Polyline" }) { string captured = tool; Button b = B(tool, delegate { if (!TryFlushElement()) return; Canvas.Tool = captured; Canvas.Focus(); }); b.Height = 25; tools.Controls.Add(b); }
            tools.Controls.Add(B("Delete", delegate { if (!TryFlushElement()) return; PreviewElement x = Current(); if (x != null) { working.Layout.Elements.Remove(x); Canvas.SelectedElementId = null; BindElements(); } }));
            tools.Controls.Add(B("Up", delegate { MoveElement(-1); })); tools.Controls.Add(B("Down", delegate { MoveElement(1); }));
            tools.Controls.Add(new Label { Text = "Background", AutoSize = true, Margin = new Padding(8, 7, 2, 0) }); BackgroundPath.Width = 160; BackgroundPath.TextChanged += delegate { if (!binding) { working.Layout.BackgroundImage = BackgroundPath.Text.Trim(); Canvas.Invalidate(); UpdateDiagnostics(); } }; tools.Controls.Add(BackgroundPath);
            tools.Controls.Add(B("Browse...", BrowseBackground)); tools.Controls.Add(new Label { Text = "Opacity", AutoSize = true, Margin = new Padding(5, 7, 1, 0) }); BackgroundOpacity.Width = 48; BackgroundOpacity.ValueChanged += delegate { if (!binding) { working.Layout.BackgroundOpacity = (double)BackgroundOpacity.Value / 100; Canvas.Invalidate(); UpdateDiagnostics(); } }; tools.Controls.Add(BackgroundOpacity);

            Panel left = new Panel { Dock = DockStyle.Fill }; ElementList.Dock = DockStyle.Fill; left.Controls.Add(ElementList); left.Controls.Add(new Label { Text = "Preview objects", Dock = DockStyle.Top, Height = 20 });
            TableLayoutPanel editor = new TableLayoutPanel { Dock = DockStyle.Top, Height = 232, ColumnCount = 2, Padding = new Padding(5) }; editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            PixelSize.DecimalPlaces = 4; PixelSize.Increment = .1M;
            Add(editor, "Name", ElementName); Add(editor, "Group", Group); Add(editor, "Number", Number); Add(editor, "Nodes", Nodes); Add(editor, "Pixel size", PixelSize); Add(editor, "Matrix rows", Rows); Add(editor, "Matrix cols", Columns); Add(editor, "Matrix order", Serpentine); Add(editor, "Polyline", PolylineClosed);
            foreach (Control c in new Control[] { ElementName, Group, Number, Nodes, PixelSize, Rows, Columns, Serpentine, PolylineClosed }) c.Validated += delegate { TryFlushElement(); };
            Panel properties = new Panel { Dock = DockStyle.Top, Height = 258 }; properties.Controls.Add(editor); properties.Controls.Add(new Label { Text = "Selected object", Dock = DockStyle.Top, Height = 20 });
            SplitContainer middle = new SplitContainer { Size = new Size(850, 560), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 220, Panel2MinSize = 250, SplitterDistance = 220 }; middle.Panel1.Controls.Add(left); middle.Panel1.Controls.Add(properties); middle.Panel2.Controls.Add(Canvas);

            Panel right = new Panel { Dock = DockStyle.Fill }; RawList.Dock = DockStyle.Top; RawList.Height = 130; Button assign = B("Assign selected raw → object", delegate { List<string> ids = SelectedRaw(); AssignSelected(ids, false); }); assign.Dock = DockStyle.Top; Button reverse = B("Assign reverse", delegate { AssignSelected(SelectedRaw(), true); }); reverse.Dock = DockStyle.Top;
            Diagnostics.Dock = DockStyle.Bottom; Diagnostics.Height = 54; Diagnostics.ForeColor = Color.Maroon; Diagnostics.AutoEllipsis = true; right.Controls.Add(MappingGrid); right.Controls.Add(reverse); right.Controls.Add(assign); right.Controls.Add(RawList); right.Controls.Add(new Label { Text = "Raw channels — physical output order", Dock = DockStyle.Top, Height = 20 }); right.Controls.Add(Diagnostics);
            SplitContainer outer = new SplitContainer { Size = new Size(1160, 560), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Panel1MinSize = 500, Panel2MinSize = 280, SplitterDistance = 856 }; outer.Panel1.Controls.Add(middle); outer.Panel2.Controls.Add(right);
            Controls.Add(outer); Controls.Add(tools); Controls.Add(Buttons());
            BindAll();
        }
        private static NumericUpDown N(decimal minimum, decimal maximum) { return new NumericUpDown { Minimum = minimum, Maximum = maximum, DecimalPlaces = 0, Dock = DockStyle.Fill }; }
        private static void Add(TableLayoutPanel p, string text, Control control) { int row = p.RowCount++; p.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); p.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(1, 4, 1, 0) }, 0, row); p.Controls.Add(control, 1, row); }
        private void BrowseBackground(object sender, EventArgs e)
        {
            using (OpenFileDialog open = new OpenFileDialog { Filter = "Image files|*.bmp;*.jpg;*.jpeg;*.png;*.gif", CheckFileExists = true }) if (open.ShowDialog(this) == DialogResult.OK) BackgroundPath.Text = Path.GetFullPath(open.FileName);
        }
        private PreviewElement Current() { PreviewElement selected = ElementList.SelectedItem as PreviewElement; return selected == null ? null : working.Layout.Elements.Find(delegate(PreviewElement e) { return e.Id == selected.Id; }); }
        private PreviewElement Editing()
        {
            if (String.IsNullOrEmpty(editingId) || working.Layout == null || working.Layout.Elements == null) return null;
            return working.Layout.Elements.Find(delegate(PreviewElement e) { return e != null && e.Id == editingId; });
        }
        public void FlushElement() { TryFlushElement(); }
        private bool TryFlushElement()
        {
            if (binding) return true; PreviewElement element = Editing(); if (element == null) return true;
            Project candidate = Model.Clone(working); PreviewElement next = candidate.Layout.Elements.Find(delegate(PreviewElement e) { return e != null && e.Id == editingId; }); if (next == null) return false;
            next.Name = ElementName.Text.Trim(); next.Group = (int)Group.Value; next.Number = (int)Number.Value; next.NodeCount = (int)Nodes.Value; next.PixelSize = (double)PixelSize.Value; next.Rows = (int)Rows.Value; next.Columns = (int)Columns.Value; next.Serpentine = Serpentine.Checked; next.Closed = PolylineClosed.Checked;
            if (next.Kind == "Matrix") next.NodeCount = next.Rows * next.Columns;
            List<string> errors = PreviewLayouts.Validate(candidate); if (errors.Count != 0) { Diagnostics.Text = "Correct the selected object before leaving it: " + String.Join(" ", errors.ToArray()); return false; }
            string selectedId = Current() == null ? null : Current().Id;
            working = candidate; Canvas.Project = working; Canvas.SelectedElementId = next.Id;
            binding = true; ElementList.Items.Clear(); foreach(PreviewElement value in working.Layout.Elements) ElementList.Items.Add(value);
            for(int i=0;i<ElementList.Items.Count;i++) if(((PreviewElement)ElementList.Items[i]).Id == selectedId) ElementList.SelectedIndex = i;
            binding = false; Canvas.Invalidate(); BindMapping(); UpdateDiagnostics(); return true;
        }
        public void AssignSelected(IList<string> rawIds, bool reverse)
        {
            if (!TryFlushElement()) throw new ArgumentException("Correct the selected preview object before assigning raw channels."); PreviewElement element = Current(); if (element == null) throw new ArgumentException("Select or draw a preview object first.");
            working = PreviewLayouts.Assign(working, element.Id, rawIds, reverse); Canvas.Project = working; BindAll(); SelectElement(element.Id);
        }
        private void MoveElement(int delta)
        {
            if (!TryFlushElement()) return; PreviewElement item = Current(); if (item == null) return; int i = working.Layout.Elements.IndexOf(item), target = i + delta; if (target < 0 || target >= working.Layout.Elements.Count) return;
            working.Layout.Elements.RemoveAt(i); working.Layout.Elements.Insert(target, item); BindElements(); SelectElement(item.Id);
        }
        private void BindAll()
        {
            binding = true; BackgroundPath.Text = working.Layout.BackgroundImage ?? ""; BackgroundOpacity.Value = (decimal)Math.Max(0, Math.Min(100, working.Layout.BackgroundOpacity * 100));
            RawList.Items.Clear(); List<RawChannel> raws = new List<RawChannel>(working.Channels); raws.Sort(delegate(RawChannel a, RawChannel b) { return a.OutputOrder.CompareTo(b.OutputOrder); }); foreach (RawChannel raw in raws) RawList.Items.Add(raw);
            BindElements(); binding = false; UpdateDiagnostics();
        }
        private void BindElements()
        {
            string id=Canvas.SelectedElementId; binding=true; ElementList.Items.Clear();
            int selected=-1;foreach(PreviewElement element in working.Layout.Elements){int index=ElementList.Items.Add(element);if(element.Id==id)selected=index;}
            if(selected<0&&ElementList.Items.Count>0)selected=0;ElementList.SelectedIndex=selected;binding=false;
            LoadElement(Current());Canvas.Invalidate();
        }
        private void SelectElement(string id)
        {
            for (int i = 0; i < ElementList.Items.Count; i++) if (((PreviewElement)ElementList.Items[i]).Id == id) { ElementList.SelectedIndex = i; return; }
        }
        private void RestoreEditing()
        {
            if (String.IsNullOrEmpty(editingId)) return; binding = true; SelectElement(editingId); binding = false;
        }
        private void LoadElement(PreviewElement element)
        {
            binding = true; Canvas.SelectedElementId = element == null ? null : element.Id; editingId = Canvas.SelectedElementId;
            ElementName.Text = element == null ? "" : element.Name; Group.Value = element == null ? 1 : Math.Max(Group.Minimum, Math.Min(Group.Maximum, element.Group)); Number.Value = element == null ? 1 : Math.Max(Number.Minimum, Math.Min(Number.Maximum, element.Number));
            Nodes.Value = element == null ? 1 : Math.Max(Nodes.Minimum, Math.Min(Nodes.Maximum, element.NodeCount)); PixelSize.Value = element == null ? 5 : Math.Max(PixelSize.Minimum, Math.Min(PixelSize.Maximum, (decimal)element.PixelSize)); Rows.Value = element == null ? 1 : Math.Max(Rows.Minimum, Math.Min(Rows.Maximum, element.Rows)); Columns.Value = element == null ? 1 : Math.Max(Columns.Minimum, Math.Min(Columns.Maximum, element.Columns)); Serpentine.Checked = element != null && element.Serpentine; PolylineClosed.Checked = element != null && element.Closed;
            binding = false; BindMapping(); Canvas.Invalidate();
        }
        private void BindMapping()
        {
            MappingGrid.Rows.Clear(); PreviewElement element = Current(); if (element == null) return; int count = Math.Max(1, element.NodeCount);
            for (int i = 0; i < count; i++) { List<string> ids = PreviewLayouts.ChannelsForNode(working, element, i); MappingGrid.Rows.Add(i + 1, "G" + element.Group + " / N" + element.Number + " / " + (i + 1), ChannelNames(ids)); }
        }
        private string ChannelNames(List<string> ids)
        {
            List<string> names = new List<string>(); foreach (string id in ids) { RawChannel raw = working.Channels.Find(delegate(RawChannel c) { return c.Id == id; }); names.Add(raw == null ? "(missing)" : raw.Name); } return String.Join(", ", names.ToArray());
        }
        private List<string> SelectedRaw() { List<string> ids = new List<string>(); foreach (object value in RawList.SelectedItems) ids.Add(((RawChannel)value).Id); return ids; }
        private void SelectRaw(List<string> ids)
        {
            RawList.ClearSelected(); if (ids == null) return; foreach (string id in ids) for (int i = 0; i < RawList.Items.Count; i++) if (((RawChannel)RawList.Items[i]).Id == id) RawList.SetSelected(i, true);
        }
        private void UpdateDiagnostics()
        {
            Canvas.RefreshBackgroundStatus(); List<string> warnings = PreviewLayouts.Diagnostics(working); if (!String.IsNullOrEmpty(Canvas.BackgroundWarning)) warnings.Add(Canvas.BackgroundWarning); Diagnostics.Text = warnings.Count == 0 ? "Preview mapping is ready. Save/compile, then identify the preview control file for playback." : String.Join("  ", warnings.ToArray());
        }
        protected override void Accept()
        {
            if(!TryFlushElement())throw new ArgumentException("Correct the selected preview object before saving.");Model.RequireValid(working);
            string path=working.Layout.ControlFile;
            if(String.IsNullOrEmpty(path))using(SaveFileDialog save=new SaveFileDialog{Filter="Preview Control File (*.hlsrpreview)|*.hlsrpreview"}){if(save.ShowDialog(this)!=DialogResult.OK)return;path=save.FileName;}
            working=PreviewLayouts.Compile(working,path);base.Accept();
        }
    }
}
