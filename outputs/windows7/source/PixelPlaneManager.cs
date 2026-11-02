using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class ChannelChoice
    {
        public string Id { get; set; } public string Label { get; set; }
        public override string ToString() { return Label; }
    }
    public sealed class PixelPlaneManager : ProjectDialog
    {
        public readonly ListBox pixelPlanes = new ListBox();
        public readonly NumericUpDown pixelWidth = new NumericUpDown(), pixelHeight = new NumericUpDown();
        public readonly ComboBox pixelFirst = new ComboBox(), pixelAxis = new ComboBox();
        public readonly CheckBox pixelSerpentine = new CheckBox();
        public readonly TextBox pixelName = new TextBox();
        public readonly DataGridView pixelCells = new DataGridView();
        public readonly Button pixelWizard;
        private PixelPlane displayed;
        private bool loading;
        public PixelPlaneManager(Project project) : base(project)
        {
            Text = "Manage PixelPlanes - XY to raw RGB channel mapping";
            pixelPlanes.Dock = DockStyle.Left; pixelPlanes.Width = 170; Fmt(pixelPlanes, delegate(object p) { return ((PixelPlane)p).Name; });
            pixelPlanes.SelectedIndexChanged += SelectPlane;
            Panel right = new Panel { Dock = DockStyle.Fill };
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 110, RowCount = 3, ColumnCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) header.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            FlowLayoutPanel dimensions = Row(); pixelName.Width = 150; pixelWidth.Minimum = pixelHeight.Minimum = 1; pixelWidth.Maximum = pixelHeight.Maximum = 32; pixelWidth.Width = pixelHeight.Width = 48;
            dimensions.Controls.Add(new Label { Text = "Name", AutoSize = true }); dimensions.Controls.Add(pixelName);
            dimensions.Controls.Add(new Label { Text = "Width", AutoSize = true }); dimensions.Controls.Add(pixelWidth); dimensions.Controls.Add(new Label { Text = "Height", AutoSize = true }); dimensions.Controls.Add(pixelHeight);
            dimensions.Controls.Add(B("Apply", delegate { Run(delegate { ApplyDisplayed(); Bind(displayed); }); })); header.Controls.Add(dimensions, 0, 0);
            FlowLayoutPanel wizard = Row(); pixelFirst.Width = 185; pixelFirst.DropDownStyle = ComboBoxStyle.DropDownList; pixelAxis.Width = 90; pixelAxis.DropDownStyle = ComboBoxStyle.DropDownList;
            pixelAxis.Items.AddRange(new object[] { "Horizontal", "Vertical" }); pixelAxis.SelectedIndex = 0; pixelSerpentine.Text = "Serpentine"; pixelSerpentine.AutoSize = true;
            pixelWizard = B("Assign mapping", delegate { Run(BuildMapping); }); wizard.Controls.Add(pixelFirst); wizard.Controls.Add(pixelAxis); wizard.Controls.Add(pixelSerpentine); wizard.Controls.Add(pixelWizard); header.Controls.Add(wizard, 0, 1);
            FlowLayoutPanel commands = Row(); commands.Controls.Add(B("New plane", delegate { Run(CreatePlane); })); commands.Controls.Add(B("Delete plane", delegate { Run(DeletePlane); }));
            commands.Controls.Add(new Label { Text = "Blank cell = hole. RGB cells must be unique within the plane.", AutoSize = true, Margin = new Padding(8, 7, 0, 0) }); header.Controls.Add(commands, 0, 2);
            pixelCells.Dock = DockStyle.Fill; pixelCells.AllowUserToAddRows = pixelCells.AllowUserToDeleteRows = false; pixelCells.AutoGenerateColumns = false;
            pixelCells.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "X", Width = 45, ReadOnly = true });
            pixelCells.Columns.Add(new DataGridViewTextBoxColumn { Name = "Y", HeaderText = "Y", Width = 45, ReadOnly = true });
            List<ChannelChoice> choices = Choices(true); DataGridViewComboBoxColumn channel = new DataGridViewComboBoxColumn { Name = "Channel", HeaderText = "Mapped RGB raw channel", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DataSource = choices, ValueMember = "Id", DisplayMember = "Label" }; pixelCells.Columns.Add(channel);
            pixelFirst.DataSource = Choices(false); pixelFirst.DisplayMember = "Label"; pixelFirst.ValueMember = "Id";
            right.Controls.Add(pixelCells); right.Controls.Add(header); Controls.Add(right); Controls.Add(pixelPlanes); Controls.Add(Buttons()); Bind(null);
        }
        private static FlowLayoutPanel Row() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(3) }; }
        private List<ChannelChoice> Choices(bool hole)
        {
            List<ChannelChoice> choices = new List<ChannelChoice>(); if (hole) choices.Add(new ChannelChoice { Id = "", Label = "(unmapped hole)" });
            foreach (RawChannel c in working.Channels) if (c.Kind == "RGB") choices.Add(new ChannelChoice { Id = c.Id, Label = c.Name + "  [U" + c.Universe + ":" + c.Address + "]" }); return choices;
        }
        private void Bind(PixelPlane selected)
        {
            loading = true; pixelPlanes.DataSource = null; pixelPlanes.Items.Clear(); foreach (PixelPlane plane in working.PixelPlanes) pixelPlanes.Items.Add(plane);
            if (working.PixelPlanes.Count > 0) pixelPlanes.SelectedItem = selected ?? working.PixelPlanes[0]; loading = false;
            displayed = pixelPlanes.SelectedItem as PixelPlane; LoadDisplayed();
        }
        private void LoadDisplayed()
        {
            loading = true; pixelCells.Rows.Clear();
            if (displayed != null)
            {
                pixelName.Text = displayed.Name; pixelWidth.Value = displayed.Width; pixelHeight.Value = displayed.Height;
                for (int y = 0; y < displayed.Height; y++) for (int x = 0; x < displayed.Width; x++) pixelCells.Rows.Add(x, y, displayed.Cells[y * displayed.Width + x] ?? "");
            }
            else pixelName.Text = ""; loading = false;
        }
        private void SelectPlane(object sender, EventArgs e)
        {
            if (loading) return; PixelPlane previous = displayed, next = pixelPlanes.SelectedItem as PixelPlane;
            try { ApplyDisplayed(); displayed = next; LoadDisplayed(); }
            catch (Exception error) { loading = true; pixelPlanes.SelectedItem = previous; loading = false; MessageBox.Show(this, error.Message, Text); }
        }
        public void ApplyDisplayed()
        {
            if (displayed == null) return;
            if (!pixelCells.EndEdit()) throw new ArgumentException("Finish the active mapping cell first.");
            int width = (int)pixelWidth.Value, height = (int)pixelHeight.Value;
            if ((width != displayed.Width || height != displayed.Height) && working.Effects.Exists(delegate(Effect f) { return f.TargetId == displayed.Id; })) throw new ArgumentException("Cannot resize a PixelPlane while spatial clips reference its dimensions.");
            List<string> cells = new List<string>(); foreach (DataGridViewRow row in pixelCells.Rows) cells.Add(Convert.ToString(row.Cells["Channel"].Value) ?? "");
            while (cells.Count < width * height) cells.Add(""); if (cells.Count > width * height) cells.RemoveRange(width * height, cells.Count - width * height);
            displayed.Name = pixelName.Text.Trim(); displayed.Width = width; displayed.Height = height; displayed.Cells = cells;
        }
        public void BuildMapping()
        {
            ApplyDisplayed(); if (displayed == null || pixelFirst.SelectedItem == null) return;
            displayed.Cells = Model.BuildPlaneMap(working, displayed.Width, displayed.Height, ((ChannelChoice)pixelFirst.SelectedItem).Id, Convert.ToString(pixelAxis.SelectedItem), pixelSerpentine.Checked); LoadDisplayed();
        }
        public void CreatePlane()
        {
            ApplyDisplayed(); int n = 1; while (working.PixelPlanes.Exists(delegate(PixelPlane p) { return p.Name == "PixelPlane " + n; })) n++;
            PixelPlane created = new PixelPlane { Id = Model.NewId(), Name = "PixelPlane " + n }; created.Cells.Add(""); working.PixelPlanes.Add(created); Bind(created);
        }
        public void DeletePlane() { ApplyDisplayed(); if (displayed == null) return; Model.RemoveTarget(working, displayed.Id); displayed = null; Bind(null); }
        private void Run(Action action) { try { action(); } catch (Exception error) { MessageBox.Show(this, error.Message, Text); } }
        protected override void Accept() { ApplyDisplayed(); base.Accept(); }
    }
}
