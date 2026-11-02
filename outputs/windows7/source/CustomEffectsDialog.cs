using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace HlsRecreation
{
    // This is deliberately an unbound editor.  CustomPart has public fields, and
    // DataGridView's automatic binding silently loses edits to those fields.
    public sealed class SerialDefinitionEditor : UserControl
    {
        public readonly TextBox definitionName = new TextBox();
        public readonly DataGridView partGrid = new DataGridView();
        public readonly Label totalCells = new Label();
        public readonly Button addLevel = new Button(), addRampUp = new Button(), addRampDown = new Button();
        public readonly Button removePart = new Button(), duplicatePart = new Button(), movePartUp = new Button(), movePartDown = new Button();
        public readonly Button choosePartColor = new Button();
        public readonly SerialRecipePreview preview = new SerialRecipePreview();
        private CustomDefinition definition;
        private bool loading;

        public CustomDefinition Definition { get { return definition; } }

        public SerialDefinitionEditor()
        {
            Dock = DockStyle.Fill;
            TableLayoutPanel table = new TableLayoutPanel(); table.Dock = DockStyle.Fill; table.Padding = new Padding(6); table.ColumnCount = 2; table.RowCount = 4;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            table.Controls.Add(new Label { Text = "Definition name", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            definitionName.Dock = DockStyle.Fill; table.Controls.Add(definitionName, 1, 0);

            partGrid.Dock = DockStyle.Fill; partGrid.AllowUserToAddRows = false; partGrid.AllowUserToDeleteRows = false; partGrid.AutoGenerateColumns = false;
            partGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; partGrid.MultiSelect = false; partGrid.RowHeadersVisible = false;
            DataGridViewComboBoxColumn kind = new DataGridViewComboBoxColumn(); kind.Name = "Kind"; kind.HeaderText = "Type"; kind.Items.AddRange(new object[] { "Level", "RampUp", "RampDown" }); kind.Width = 88;
            partGrid.Columns.Add(kind);
            partGrid.Columns.Add(Column("LengthCells", "Length (cells)", 88));
            partGrid.Columns.Add(Column("StartPercent", "Start %", 78));
            partGrid.Columns.Add(Column("EndPercent", "End %", 78));
            partGrid.Columns.Add(Column("ColorHtml", "Color #RRGGBB", 110));
            table.Controls.Add(partGrid, 0, 1); table.SetColumnSpan(partGrid, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel(); actions.Dock = DockStyle.Fill; actions.WrapContents = true;
            Configure(addLevel, "Add Level", delegate { AddPart("Level"); }); Configure(addRampUp, "Add Ramp Up", delegate { AddPart("RampUp"); }); Configure(addRampDown, "Add Ramp Down", delegate { AddPart("RampDown"); });
            Configure(removePart, "Remove", delegate { Remove(); }); Configure(duplicatePart, "Duplicate", delegate { Duplicate(); }); Configure(movePartUp, "Up", delegate { MovePart(-1); }); Configure(movePartDown, "Down", delegate { MovePart(1); });
            Configure(choosePartColor, "Color…", delegate { PickColor(); });
            actions.Controls.Add(addLevel); actions.Controls.Add(addRampUp); actions.Controls.Add(addRampDown); actions.Controls.Add(removePart); actions.Controls.Add(duplicatePart); actions.Controls.Add(movePartUp); actions.Controls.Add(movePartDown); actions.Controls.Add(choosePartColor);
            totalCells.AutoSize = true; totalCells.Margin = new Padding(12, 8, 0, 0); actions.Controls.Add(totalCells);
            table.Controls.Add(actions, 0, 2); table.SetColumnSpan(actions, 2);
            preview.Dock = DockStyle.Fill; preview.MinimumSize = new Size(300, 84); table.Controls.Add(preview, 0, 3); table.SetColumnSpan(preview, 2);
            Controls.Add(table);
            partGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == partGrid.Columns["ColorHtml"].Index) PickColor(); };
            partGrid.CellValueChanged += delegate { if (!loading) RefreshPreviewSafe(); };
            partGrid.RowsRemoved += delegate { if (!loading) RefreshPreviewSafe(); };
        }

        private static DataGridViewTextBoxColumn Column(string name, string title, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn(); column.Name = name; column.HeaderText = title; column.Width = width; return column;
        }
        private void Configure(Button button, string text, EventHandler handler) { button.Text = text; button.AutoSize = true; button.Click += delegate(object sender, EventArgs e) { try { handler(sender, e); } catch (Exception error) { ShowError(error); } }; }
        private void ShowError(Exception error) { if (ProjectDialog.SuppressErrorDialogs) throw error; MessageBox.Show(FindForm(), error.Message, "Custom effect"); }

        public void LoadDefinition(CustomDefinition value)
        {
            definition = value; loading = true; partGrid.Rows.Clear(); definitionName.Text = value == null ? "" : value.Name;
            if (value != null && value.Parts != null)
                for (int i = 0; i < value.Parts.Count; i++)
                {
                    CustomPart part = value.Parts[i];
                    if (part != null) partGrid.Rows.Add(part.Kind, part.LengthCells.ToString(CultureInfo.InvariantCulture), Percent(part.StartLevel), Percent(part.EndLevel), part.ColorHtml);
                }
            loading = false; RefreshPreviewSafe();
        }
        private static string Percent(double value) { return (value * 100.0).ToString("0.##", CultureInfo.InvariantCulture); }
        public void Flush()
        {
            if (definition == null) return;
            partGrid.EndEdit();
            string name = definitionName.Text.Trim();
            if (name.Length == 0) throw new ArgumentException("A custom definition name is required.");
            if (name.Length > 256) throw new ArgumentException("Definition name is too long.");
            List<CustomPart> parsed = new List<CustomPart>();
            for (int i = 0; i < partGrid.Rows.Count; i++)
            {
                DataGridViewRow row = partGrid.Rows[i];
                string kind = CellText(row, "Kind"); int length = Integer(row, "LengthCells", 1, 12000, "Length (cells)");
                double start = Percentage(row, "StartPercent"), end = Percentage(row, "EndPercent"); string color = CellText(row, "ColorHtml").Trim();
                if (kind != "Level" && kind != "RampUp" && kind != "RampDown") throw new ArgumentException("Part " + (i + 1) + " has an invalid Type.");
                Model.ParseColor(color);
                if (kind == "Level") end = start;
                parsed.Add(new CustomPart { Kind = kind, LengthCells = length, StartLevel = start, EndLevel = end, ColorHtml = Model.HtmlColor(Model.ParseColor(color)) });
            }
            if (parsed.Count == 0) throw new ArgumentException("A custom definition needs at least one serial part.");
            definition.Name = name; definition.Parts = parsed;
            List<string> errors = CustomEffects.Validate(definition); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()));
            RefreshPreview();
        }
        private static string CellText(DataGridViewRow row, string column) { object value = row.Cells[column].Value; return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static int Integer(DataGridViewRow row, string column, int minimum, int maximum, string label)
        {
            int value; if (!Int32.TryParse(CellText(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum || value > maximum) throw new ArgumentException(label + " in row " + (row.Index + 1) + " must be " + minimum + ".." + maximum + "."); return value;
        }
        private static double Percentage(DataGridViewRow row, string column)
        {
            double value; if (!Double.TryParse(CellText(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || Double.IsNaN(value) || Double.IsInfinity(value) || value < 0 || value > 100) throw new ArgumentException(column.Replace("Percent", " %") + " in row " + (row.Index + 1) + " must be 0..100."); return value / 100.0;
        }
        private void AddPart(string kind)
        {
            if (definition == null) return; double start = 1, end = 1;
            if (kind == "RampUp") { start = 0; end = 1; } else if (kind == "RampDown") { start = 1; end = 0; }
            int row = partGrid.Rows.Add(kind, "2", Percent(start), Percent(end), "#FFFFFF"); partGrid.CurrentCell = partGrid.Rows[row].Cells[0]; RefreshPreviewSafe();
        }
        private void Remove() { if (partGrid.CurrentRow == null) return; partGrid.Rows.RemoveAt(partGrid.CurrentRow.Index); RefreshPreviewSafe(); }
        private void Duplicate()
        {
            if (partGrid.CurrentRow == null) return; int index = partGrid.CurrentRow.Index; DataGridViewRow source = partGrid.Rows[index]; int row = partGrid.Rows.Add(CellText(source, "Kind"), CellText(source, "LengthCells"), CellText(source, "StartPercent"), CellText(source, "EndPercent"), CellText(source, "ColorHtml")); MoveRow(row, index + 1); partGrid.CurrentCell = partGrid.Rows[index + 1].Cells[0]; RefreshPreviewSafe();
        }
        private void MovePart(int delta) { if (partGrid.CurrentRow == null) return; int old = partGrid.CurrentRow.Index, next = old + delta; if (next < 0 || next >= partGrid.Rows.Count) return; MoveRow(old, next); partGrid.CurrentCell = partGrid.Rows[next].Cells[0]; RefreshPreviewSafe(); }
        private void MoveRow(int from, int to)
        {
            object[] values = new object[partGrid.Columns.Count]; for (int i = 0; i < values.Length; i++) values[i] = partGrid.Rows[from].Cells[i].Value;
            partGrid.Rows.RemoveAt(from); partGrid.Rows.Insert(to, values);
        }
        private void PickColor()
        {
            if (partGrid.CurrentRow == null) return; string current = CellText(partGrid.CurrentRow, "ColorHtml"); Color color;
            try { color = Model.ParseColor(current); } catch { color = Color.White; }
            using (ColorDialog dialog = new ColorDialog()) { dialog.Color = color; if (dialog.ShowDialog(FindForm()) == DialogResult.OK) partGrid.CurrentRow.Cells["ColorHtml"].Value = Model.HtmlColor(dialog.Color); }
        }
        public void RefreshPreview()
        {
            if (definition == null) { preview.Definition = null; totalCells.Text = "0 cells"; return; }
            preview.Definition = definition; totalCells.Text = CustomEffects.TotalCells(definition).ToString(CultureInfo.InvariantCulture) + " cells"; preview.Invalidate();
        }
        private void RefreshPreviewSafe()
        {
            if (!loading)
            {
                loading = true;
                foreach (DataGridViewRow row in partGrid.Rows)
                {
                    bool flat = CellText(row, "Kind") == "Level"; row.Cells["EndPercent"].ReadOnly = flat;
                    if (flat) row.Cells["EndPercent"].Value = row.Cells["StartPercent"].Value;
                }
                loading = false;
            }
            try { FlushPreviewOnly(); } catch { totalCells.Text = "Correct the highlighted values"; preview.Definition = null; preview.Invalidate(); }
        }
        private void FlushPreviewOnly()
        {
            if (definition == null) return; List<CustomPart> values = new List<CustomPart>();
            for (int i = 0; i < partGrid.Rows.Count; i++)
            {
                DataGridViewRow r = partGrid.Rows[i]; int length; double start, end; string kind = CellText(r, "Kind"), color = CellText(r, "ColorHtml");
                if (!Int32.TryParse(CellText(r, "LengthCells"), NumberStyles.Integer, CultureInfo.InvariantCulture, out length) || length < 1 || !Double.TryParse(CellText(r, "StartPercent"), NumberStyles.Float, CultureInfo.InvariantCulture, out start) || !Double.TryParse(CellText(r, "EndPercent"), NumberStyles.Float, CultureInfo.InvariantCulture, out end)) throw new ArgumentException();
                values.Add(new CustomPart { Kind = kind, LengthCells = length, StartLevel = start / 100, EndLevel = end / 100, ColorHtml = color });
            }
            preview.Definition = new CustomDefinition { Id = definition.Id, Name = definitionName.Text, Parts = values }; int total = 0; foreach (CustomPart part in values) total += part.LengthCells; totalCells.Text = total.ToString(CultureInfo.InvariantCulture) + " cells"; preview.Invalidate();
        }
    }

    public sealed class SerialRecipePreview : Control
    {
        public CustomDefinition Definition;
        public SerialRecipePreview() { DoubleBuffered = true; BackColor = Color.FromArgb(36, 36, 36); ForeColor = Color.White; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(BackColor); if (Definition == null || Definition.Parts == null || Definition.Parts.Count == 0) { e.Graphics.DrawString("Recipe preview: add a serial part.", Font, Brushes.Gainsboro, 8, 8); return; }
            int total = 0; foreach (CustomPart part in Definition.Parts) if (part != null) total += Math.Max(1, part.LengthCells); int x = 8, width = Math.Max(1, ClientSize.Width - 16), y = 22, height = Math.Max(24, ClientSize.Height - 42);
            e.Graphics.DrawString("Serial recipe - sampled cell intensities", Font, Brushes.Gainsboro, 8, 4);
            foreach (CustomPart part in Definition.Parts)
            {
                if (part == null) continue; int piece = Math.Max(2, (int)Math.Round(width * (double)Math.Max(1, part.LengthCells) / total)); Color source;
                try { source = Model.ParseColor(part.ColorHtml); } catch { source = Color.Black; }
                for (int pixel = 0; pixel < piece; pixel++)
                {
                    int cell = piece <= 1 ? 0 : (int)Math.Round(pixel * (double)(part.LengthCells - 1) / (piece - 1));
                    double progress = part.LengthCells <= 1 ? 0 : cell / (double)(part.LengthCells - 1);
                    double intensity = part.Kind == "Level" ? part.StartLevel : part.StartLevel + (part.EndLevel - part.StartLevel) * progress;
                    intensity = Math.Max(0, Math.Min(1, intensity)); Color sample = Color.FromArgb((int)Math.Round(source.R * intensity), (int)Math.Round(source.G * intensity), (int)Math.Round(source.B * intensity));
                    using (Pen samplePen = new Pen(sample)) e.Graphics.DrawLine(samplePen, x + pixel, y, x + pixel, y + height);
                }
                e.Graphics.DrawRectangle(Pens.Silver, x, y, piece, height);
                if (piece > 48) e.Graphics.DrawString(part.Kind + " " + part.LengthCells + "c", Font, Brushes.White, x + 3, y + 3); x += piece;
            }
            e.Graphics.DrawString("First/last cells use the authored endpoints; blocks retain serial order.", Font, Brushes.Gainsboro, 8, ClientSize.Height - 18);
        }
    }

    public sealed class CustomEffectsDialog : ProjectDialog
    {
        public readonly ListBox definitionList = new ListBox();
        public readonly SerialDefinitionEditor definitionEditor = new SerialDefinitionEditor();
        public readonly DataGridView partGrid;
        public readonly TextBox definitionName;
        public readonly ComboBox placementMode = new ComboBox(), colorMode = new ComboBox();
        public readonly NumericUpDown repeatCount = Number(1, 1, 10000), periodCells = Number(0, 0, 12000), staggerCells = Number(1, 0, 12000), coveragePercent = Number(100, 0, 100), seed = Number(1, -2147483648M, 2147483647M), minIntensityPercent = Number(0, 0, 100), maxIntensityPercent = Number(100, 0, 100);
        public readonly CheckBox randomizePartOrder = new CheckBox { Text = "Place multiple line effects random", AutoSize = true }, randomIntensity = new CheckBox { Text = "Random intensity", AutoSize = true };
        public readonly TextBox selectedColor = new TextBox();
        public readonly Button selectedColorButton = new Button { Text = "Color…", AutoSize = true }, previewPlacementButton = new Button { Text = "Preview placement", AutoSize = true }, placeButton = new Button { Text = "Place", AutoSize = true };
        public readonly Label placementStatus = new Label { AutoSize = true };
        public readonly SerialRecipePreview placementPreview = new SerialRecipePreview();
        private readonly Selection selection;
        private bool changing;
        private string selectedId;
        private bool placementRequested;

        public string SelectedDefinitionId { get { return selectedId; } }
        public bool PlacementRequested { get { return placementRequested; } }
        public CustomPlacement Placement { get { return ReadPlacement(); } }

        public CustomEffectsDialog(Project project, Selection chosen, Color initialColor) : base(project)
        {
            if (chosen == null) throw new ArgumentNullException("chosen"); selection = chosen; Text = "Custom Effects"; Width = 1020; Height = 700; MinimumSize = new Size(900, 600);
            partGrid = definitionEditor.partGrid; definitionName = definitionEditor.definitionName;
            definitionList.Dock = DockStyle.Fill; definitionList.FormattingEnabled = true; definitionList.Format += delegate(object sender, ListControlConvertEventArgs e) { CustomDefinition d = e.ListItem as CustomDefinition; e.Value = d == null ? "" : d.Name; };
            definitionList.SelectedIndexChanged += DefinitionChanged;
            FlowLayoutPanel leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64 };
            leftButtons.Controls.Add(MakeButton("New", delegate { NewDefinition(); })); leftButtons.Controls.Add(MakeButton("Duplicate", delegate { DuplicateDefinition(); })); leftButtons.Controls.Add(MakeButton("Delete", delegate { DeleteDefinition(); }));
            Panel left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) }; left.Controls.Add(definitionList); left.Controls.Add(leftButtons);
            TableLayoutPanel split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); split.Controls.Add(left, 0, 0); split.Controls.Add(definitionEditor, 1, 0);
            Panel placement = BuildPlacement();
            Controls.Add(split); Controls.Add(placement); Controls.Add(BuildBottom());
            selectedColor.Text = Model.HtmlColor(initialColor);
            placementMode.DropDownStyle = colorMode.DropDownStyle = ComboBoxStyle.DropDownList; placementMode.Items.AddRange(new object[] { "Chase", "Random" }); placementMode.SelectedIndex = 0; colorMode.Items.AddRange(new object[] { "Selected", "Embedded", "Random" }); colorMode.SelectedIndex = 0;
            BindDefinitions(working.CustomEffects.Count == 0 ? null : working.CustomEffects[0].Id);
        }
        private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum) { NumericUpDown n = new NumericUpDown(); n.Minimum = minimum; n.Maximum = maximum; n.Value = value; n.Width = 66; return n; }
        private Button MakeButton(string text, EventHandler action) { Button b = new Button { Text = text, AutoSize = true }; b.Click += delegate(object sender, EventArgs e) { try { action(sender, e); } catch (Exception error) { Error(error); } }; return b; }
        private Panel BuildPlacement()
        {
            Panel panel = new Panel { Dock = DockStyle.Bottom, Height = 220, Padding = new Padding(7) };
            GroupBox box = new GroupBox { Text = "Place selected serial recipe", Dock = DockStyle.Fill };
            TableLayoutPanel rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel first = PlacementRow(); placementMode.Width = colorMode.Width = 100;
            Labeled(first, "Mode", placementMode); Labeled(first, "Repeat", repeatCount); Labeled(first, "Period cells", periodCells); Labeled(first, "Stagger cells", staggerCells); Labeled(first, "Coverage %", coveragePercent);
            FlowLayoutPanel second = PlacementRow(); selectedColor.Width = 74;
            Labeled(second, "Color", colorMode); second.Controls.Add(selectedColor); second.Controls.Add(selectedColorButton); Labeled(second, "Seed", seed); second.Controls.Add(randomizePartOrder); second.Controls.Add(randomIntensity);
            FlowLayoutPanel third = PlacementRow(); Labeled(third, "Min intensity %", minIntensityPercent); Labeled(third, "Max intensity %", maxIntensityPercent);
            third.Controls.Add(new Label { Text = "Period 0 uses recipe width for Chase; any cell for Random.", AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
            TableLayoutPanel lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); placementStatus.AutoSize = false; placementStatus.Dock = DockStyle.Fill; actions.Controls.Add(previewPlacementButton, 0, 0); actions.Controls.Add(placementStatus, 0, 1); placementPreview.Dock = DockStyle.Fill;
            lower.Controls.Add(actions, 0, 0); lower.Controls.Add(placementPreview, 1, 0);
            rows.Controls.Add(first, 0, 0); rows.Controls.Add(second, 0, 1); rows.Controls.Add(third, 0, 2); rows.Controls.Add(lower, 0, 3);
            box.Controls.Add(rows); panel.Controls.Add(box);
            randomIntensity.CheckedChanged += delegate { minIntensityPercent.Enabled = maxIntensityPercent.Enabled = randomIntensity.Checked; };
            minIntensityPercent.Enabled = maxIntensityPercent.Enabled = randomIntensity.Checked;
            selectedColorButton.Click += delegate { PickPlacementColor(); };
            previewPlacementButton.Click += delegate { try { PreviewPlacement(); } catch (Exception error) { Error(error); } };
            placeButton.Click += delegate { try { PlaceSelected(); } catch (Exception error) { Error(error); } };
            return panel;
        }
        private static FlowLayoutPanel PlacementRow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true }; }
        private static void Labeled(FlowLayoutPanel row, string label, Control control) { row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 3, 0) }); row.Controls.Add(control); }
        private static void Add(TableLayoutPanel grid, string label, Control control, int column, int row) { FlowLayoutPanel flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; flow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(2, 6, 2, 0) }); flow.Controls.Add(control); grid.Controls.Add(flow, column, row); }
        private Panel BuildBottom()
        {
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 42 }; FlowLayoutPanel right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 270, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true }; Button save = new Button { Text = "Save definition", AutoSize = true }; save.Click += delegate { try { FlushDefinition(); placementRequested = false; base.Accept(); } catch (Exception error) { Error(error); } };
            right.Controls.Add(cancel); right.Controls.Add(placeButton); right.Controls.Add(save); bottom.Controls.Add(right); CancelButton = cancel; return bottom;
        }
        private void DefinitionChanged(object sender, EventArgs e)
        {
            if (changing) return;
            try { if (selectedId != null) FlushDefinition(); }
            catch (Exception error)
            {
                changing = true; for (int i = 0; i < definitionList.Items.Count; i++) if (((CustomDefinition)definitionList.Items[i]).Id == selectedId) { definitionList.SelectedIndex = i; break; } changing = false; Error(error); return;
            }
            CustomDefinition selected = definitionList.SelectedItem as CustomDefinition; selectedId = selected == null ? null : selected.Id; definitionEditor.LoadDefinition(selected); definitionEditor.Enabled = selected != null; placementPreview.Definition = selected; placementPreview.Invalidate();
        }
        private void BindDefinitions(string wanted)
        {
            changing = true; definitionList.Items.Clear(); foreach (CustomDefinition definition in working.CustomEffects) definitionList.Items.Add(definition); int index = 0; for (int i = 0; i < definitionList.Items.Count; i++) if (((CustomDefinition)definitionList.Items[i]).Id == wanted) { index = i; break; } if (definitionList.Items.Count > 0) definitionList.SelectedIndex = index; changing = false; DefinitionChanged(this, EventArgs.Empty);
        }
        private CustomDefinition Current() { return definitionList.SelectedItem as CustomDefinition; }
        public void FlushDefinition()
        {
            definitionEditor.Flush(); List<string> errors = CustomEffects.ValidateDefinitions(working.CustomEffects); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray())); placementPreview.Definition = Current(); placementPreview.Invalidate();
        }
        private void NewDefinition() { FlushDefinition(); CustomDefinition add = CustomEffects.Example(); add.Id = Model.NewId(); add.Name = NextDefinitionName("Custom Effect"); working.CustomEffects.Add(add); BindDefinitions(add.Id); }
        private void DuplicateDefinition() { FlushDefinition(); CustomDefinition source = Current(); if (source == null) throw new ArgumentException("Select a custom definition first."); CustomDefinition add = CustomEffects.Clone(source); add.Id = Model.NewId(); add.Name = NextDefinitionName(source.Name + " Copy"); working.CustomEffects.Add(add); BindDefinitions(add.Id); }
        private void DeleteDefinition() { FlushDefinition(); CustomDefinition source = Current(); if (source == null) return; working.CustomEffects.Remove(source); selectedId = null; BindDefinitions(working.CustomEffects.Count == 0 ? null : working.CustomEffects[0].Id); }
        private string NextDefinitionName(string prefix) { string candidate = prefix; int suffix = 2; while (working.CustomEffects.Exists(delegate(CustomDefinition d) { return String.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase); })) candidate = prefix + " " + suffix++; return candidate; }
        private CustomPlacement ReadPlacement()
        {
            string color = selectedColor.Text.Trim(); Model.ParseColor(color); int min = Decimal.ToInt32(minIntensityPercent.Value), max = Decimal.ToInt32(maxIntensityPercent.Value); if (min > max) throw new ArgumentException("Minimum intensity cannot exceed maximum intensity.");
            return new CustomPlacement { Mode = Convert.ToString(placementMode.SelectedItem), RepeatCount = Decimal.ToInt32(repeatCount.Value), PeriodCells = Decimal.ToInt32(periodCells.Value), StaggerCells = Decimal.ToInt32(staggerCells.Value), CoveragePercent = Decimal.ToInt32(coveragePercent.Value), Seed = Decimal.ToInt32(seed.Value), RandomizePartOrder = randomizePartOrder.Checked, RandomIntensity = randomIntensity.Checked, MinIntensityPercent = min, MaxIntensityPercent = max, ColorMode = Convert.ToString(colorMode.SelectedItem), SelectedColor = Model.HtmlColor(Model.ParseColor(color)) };
        }
        public void PlaceSelected()
        {
            FlushDefinition(); EnsurePlacementSelection(); CustomDefinition selected = Current(); if (selected == null) throw new ArgumentException("Select a custom definition first."); working = CustomEffects.Place(working, selected, selection, ReadPlacement()); Model.RequireValid(working); placementRequested = true; DialogResult = DialogResult.OK; Close();
        }
        private void PreviewPlacement()
        {
            FlushDefinition(); EnsurePlacementSelection(); CustomDefinition selected = Current(); if (selected == null) throw new ArgumentException("Select a custom definition first."); Project materialized = CustomEffects.Place(working, selected, selection, ReadPlacement()); int originalCount = working.Effects.Count, made = materialized.Effects.Count - originalCount; placementPreview.Definition = MaterializedRecipe(materialized, originalCount, selected); placementPreview.Invalidate(); placementStatus.Text = "Preview materialized " + made.ToString(CultureInfo.InvariantCulture) + " primitive effect(s); displayed recipe is the first committed stamp, including its realized color and intensity.";
        }
        private void PickPlacementColor()
        {
            Color initial; try { initial = Model.ParseColor(selectedColor.Text.Trim()); } catch { initial = Color.White; }
            using (ColorDialog dialog = new ColorDialog()) { dialog.Color = initial; if (dialog.ShowDialog(this) == DialogResult.OK) selectedColor.Text = Model.HtmlColor(dialog.Color); }
        }
        private static CustomDefinition MaterializedRecipe(Project materialized, int originalCount, CustomDefinition fallback)
        {
            if (materialized.Effects.Count <= originalCount) return fallback;
            Effect first = materialized.Effects[originalCount]; CustomDefinition result = new CustomDefinition { Id = fallback.Id, Name = fallback.Name + " preview" };
            for (int i = originalCount; i < materialized.Effects.Count && i - originalCount < fallback.Parts.Count; i++)
            {
                Effect effect = materialized.Effects[i]; if (effect.TargetId != first.TargetId) break;
                double start = effect.UseEndpoints ? effect.StartLevel : effect.Level, end = effect.UseEndpoints ? effect.EndLevel : effect.Level;
                result.Parts.Add(new CustomPart { Kind = effect.Kind, LengthCells = Math.Max(1, effect.DurationMs / materialized.FrameMs), StartLevel = start, EndLevel = end, ColorHtml = effect.ColorHtml });
            }
            return result.Parts.Count == 0 ? fallback : result;
        }
        private void EnsurePlacementSelection() { if (selection.Rows == null || selection.Rows.Count == 0 || selection.DurationMs <= 0) throw new ArgumentException("Select a rectangular sequence area before placing this Custom effect. You can still save definitions now."); }
        private void Error(Exception error) { if (ProjectDialog.SuppressErrorDialogs) throw error; placementStatus.Text = error.Message; MessageBox.Show(this, error.Message, Text); }
    }
}
