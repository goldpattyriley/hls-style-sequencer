using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class RawChannelManager : ProjectDialog
    {
        public readonly DataGridView rawGrid = new DataGridView();
        public readonly NumericUpDown rawAddCount = new NumericUpDown(), rawUniverse = new NumericUpDown(), rawAddress = new NumericUpDown();
        public readonly TextBox rawPrefix = new TextBox();
        public readonly CheckBox rawRgb = new CheckBox();
        public readonly Button rawAddButton;
        public RawChannelManager(Project project) : base(project)
        {
            Text = "Manage Raw Channels"; Width = 1120;
            FlowLayoutPanel add = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, Padding = new Padding(5) };
            rawAddCount.Minimum = 1; rawAddCount.Maximum = 256; rawAddCount.Value = 1; rawAddCount.Width = 55;
            rawUniverse.Maximum = 64; rawUniverse.Value = 1; rawUniverse.Width = 55;
            rawAddress.Minimum = 1; rawAddress.Maximum = 512; rawAddress.Value = 1; rawAddress.Width = 60;
            rawPrefix.Text = "Channel"; rawPrefix.Width = 135; rawRgb.Text = "RGB (3 values)"; rawRgb.AutoSize = true;
            AddLabeled(add, "Number", rawAddCount); AddLabeled(add, "Universe", rawUniverse); AddLabeled(add, "Start", rawAddress); AddLabeled(add, "Prefix", rawPrefix);
            add.Controls.Add(rawRgb); rawAddButton = B("Add", delegate { Run(delegate { AddChannelsFromControls(); }); }); add.Controls.Add(rawAddButton);
            rawGrid.Dock = DockStyle.Fill; rawGrid.AllowUserToAddRows = rawGrid.AllowUserToDeleteRows = false;
            rawGrid.AutoGenerateColumns = false; rawGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            Column("Id", "Identity", 80, true); Column("Name", "Channel name", 160, false);
            DataGridViewComboBoxColumn kind = new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Type", Width = 75 };
            kind.Items.AddRange(new object[] { "Dimmer", "RGB" }); rawGrid.Columns.Add(kind);
            Column("Universe", "Universe", 65, false); Column("Address", "Address", 65, false); Column("OutputOrder", "Output order", 80, false); Column("ColorHtml", "Color #RRGGBB", 105, false); Column("PreviewGroup", "Preview group", 85, false); Column("PreviewNumber", "Preview number", 90, false); Column("PreviewPixel", "Preview pixel", 80, false);
            rawGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != rawGrid.Columns["ColorHtml"].Index) return;
                using (ColorDialog dialog = new ColorDialog()) { if (dialog.ShowDialog(this) == DialogResult.OK) rawGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = Model.HtmlColor(dialog.Color); }
            };
            Button delete = B("Delete selected channel (referenced channels are kept)", delegate { Run(DeleteSelected); }); delete.Dock = DockStyle.Top;
            Controls.Add(rawGrid); Controls.Add(delete); Controls.Add(add); Controls.Add(Buttons()); Bind();
        }
        private static void AddLabeled(FlowLayoutPanel parent, string label, Control control) { parent.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 3, 0) }); parent.Controls.Add(control); }
        private void Column(string name, string label, int width, bool readOnly) { rawGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = label, Width = width, ReadOnly = readOnly }); }
        private void Bind() { rawGrid.Rows.Clear(); foreach (RawChannel channel in working.Channels) rawGrid.Rows.Add(channel.Id, channel.Name, channel.Kind, channel.Universe, channel.Address, channel.OutputOrder, channel.ColorHtml, channel.PreviewGroup, channel.PreviewNumber, channel.PreviewPixel); }
        private static int Integer(object value, string label) { int number; if (!Int32.TryParse(Convert.ToString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) throw new ArgumentException(label + " must be a whole number."); return number; }
        public void ApplyGridEdits()
        {
            if (!rawGrid.EndEdit()) throw new ArgumentException("Complete the current cell edit first.");
            Project candidate = Model.Clone(working);
            foreach (DataGridViewRow row in rawGrid.Rows)
            {
                string id = Convert.ToString(row.Cells["Id"].Value);
                RawChannel channel = candidate.Channels.Find(delegate(RawChannel c) { return c.Id == id; });
                if (channel == null) throw new ArgumentException("The channel identity is invalid.");
                channel.Name = Convert.ToString(row.Cells["Name"].Value).Trim(); channel.Kind = Convert.ToString(row.Cells["Kind"].Value);
                channel.Universe = Integer(row.Cells["Universe"].Value, "Universe"); channel.Address = Integer(row.Cells["Address"].Value, "Address");
                channel.OutputOrder = Integer(row.Cells["OutputOrder"].Value, "Output order"); channel.ColorHtml = Convert.ToString(row.Cells["ColorHtml"].Value).Trim();
                channel.PreviewGroup = Integer(row.Cells["PreviewGroup"].Value,"Preview group"); channel.PreviewNumber = Integer(row.Cells["PreviewNumber"].Value,"Preview number"); channel.PreviewPixel = Integer(row.Cells["PreviewPixel"].Value,"Preview pixel");
            }
            Model.RequireValid(candidate); working = candidate;
        }
        public void AddChannelsFromControls()
        {
            ApplyGridEdits(); Model.AddRawChannels(working, (int)rawAddCount.Value, rawPrefix.Text.Trim(), rawRgb.Checked ? "RGB" : "Dimmer", (int)rawUniverse.Value, (int)rawAddress.Value); Bind();
        }
        public void DeleteSelected()
        {
            if (rawGrid.CurrentRow == null) return; string id = Convert.ToString(rawGrid.CurrentRow.Cells["Id"].Value);
            ApplyGridEdits(); Model.RemoveTarget(working, id); Bind();
        }
        private void Run(Action action) { try { action(); } catch (Exception error) { MessageBox.Show(this, error.Message, Text); } }
        protected override void Accept() { ApplyGridEdits(); base.Accept(); }
    }
}
