using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class SpatialEffectEditor : ProjectDialog
    {
        private readonly Effect effect;
        private readonly PixelPlane plane;
        public readonly NumericUpDown spatialAddPosition = new NumericUpDown(), spatialKeyPosition = new NumericUpDown(), spatialSpeed = new NumericUpDown(), spatialFadeIn = new NumericUpDown(), spatialFadeOut = new NumericUpDown();
        public readonly Button spatialAddButton;
        public readonly ListBox spatialKeys = new ListBox();
        private readonly Panel matrix = new Panel();
        private Color paint = Color.White;
        public SpatialEffectEditor(Project p, string id) : base(p)
        {
            effect = working.Effects.Find(delegate(Effect f) { return f.Id == id; }); if (effect == null || effect.Kind != "PixelPlane") throw new ArgumentException("Select a spatial clip.");
            plane = working.PixelPlanes.Find(delegate(PixelPlane pp) { return pp.Id == effect.TargetId; }); Text = "PixelPlane - manually painted spatial frames";
            spatialAddPosition.DecimalPlaces = spatialKeyPosition.DecimalPlaces = 3; spatialAddPosition.Increment = spatialKeyPosition.Increment = .05M;
            spatialAddPosition.Maximum = spatialKeyPosition.Maximum = 1; spatialAddPosition.Value = .5M; spatialAddPosition.Width = spatialKeyPosition.Width = 75;
            spatialSpeed.Minimum = 1; spatialSpeed.Maximum = 4; spatialSpeed.Width = 45; spatialSpeed.Value = effect.SpeedDivisor;
            spatialFadeIn.Maximum = spatialFadeOut.Maximum = effect.DurationMs; spatialFadeIn.Width = spatialFadeOut.Width = 70;
            spatialFadeIn.Value = effect.FadeInMs; spatialFadeOut.Value = effect.FadeOutMs;
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 108, RowCount = 3, ColumnCount = 1 };
            for (int i = 0; i < 3; i++) header.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            FlowLayoutPanel keys = Row(); keys.Controls.Add(L("New key (0..1)")); keys.Controls.Add(spatialAddPosition);
            spatialAddButton = B("Add key", delegate { Run(delegate { AddKey((double)spatialAddPosition.Value); }); }); keys.Controls.Add(spatialAddButton);
            keys.Controls.Add(B("Duplicate", delegate { Run(DuplicateKey); })); keys.Controls.Add(B("Delete key", delegate { Run(DeleteKey); })); header.Controls.Add(keys, 0, 0);
            FlowLayoutPanel selected = Row(); selected.Controls.Add(L("Selected key time")); selected.Controls.Add(spatialKeyPosition); selected.Controls.Add(B("Set time", delegate { Run(ApplySelectedTime); }));
            selected.Controls.Add(B("Palette...", delegate { using (ColorDialog dialog = new ColorDialog()) { dialog.Color = paint; if (dialog.ShowDialog(this) == DialogResult.OK) paint = dialog.Color; } }));
            selected.Controls.Add(B("Black / erase", delegate { paint = Color.Black; })); header.Controls.Add(selected, 0, 1);
            FlowLayoutPanel playback = Row(); playback.Controls.Add(L("Speed divisor")); playback.Controls.Add(spatialSpeed); playback.Controls.Add(L("Fade in ms")); playback.Controls.Add(spatialFadeIn); playback.Controls.Add(L("Fade out ms")); playback.Controls.Add(spatialFadeOut);
            playback.Controls.Add(L("Click paints; right-click erases.")); header.Controls.Add(playback, 0, 2);
            spatialKeys.Dock = DockStyle.Left; spatialKeys.Width = 135; Fmt(spatialKeys, delegate(object f) { return "Key " + ((PlaneFrame)f).Position.ToString("0.000", CultureInfo.InvariantCulture); });
            spatialKeys.SelectedIndexChanged += delegate { PlaneFrame key = Current(); if (key != null) spatialKeyPosition.Value = (decimal)key.Position; matrix.Invalidate(); };
            matrix.Dock = DockStyle.Fill; matrix.BackColor = Color.Black; matrix.Paint += DrawMatrix; matrix.MouseDown += PaintCell;
            Controls.Add(matrix); Controls.Add(spatialKeys); Controls.Add(header); Controls.Add(Buttons()); Bind(effect.Frames[0]);
        }
        private static Label L(string value) { return new Label { Text = value, AutoSize = true, Margin = new Padding(4, 7, 4, 0) }; }
        private static FlowLayoutPanel Row() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true }; }
        private PlaneFrame Current() { return spatialKeys.SelectedItem as PlaneFrame; }
        private void Bind(PlaneFrame selected)
        {
            effect.Frames.Sort(delegate(PlaneFrame a, PlaneFrame b) { return a.Position.CompareTo(b.Position); }); spatialKeys.DataSource = null; spatialKeys.Items.Clear(); foreach (PlaneFrame key in effect.Frames) spatialKeys.Items.Add(key);
            spatialKeys.SelectedItem = selected; if (spatialKeys.SelectedIndex < 0 && spatialKeys.Items.Count > 0) spatialKeys.SelectedIndex = 0; matrix.Invalidate();
        }
        public void AddKey(double position)
        {
            if (effect.Frames.Exists(delegate(PlaneFrame f) { return Math.Abs(f.Position - position) < .0000001; })) throw new ArgumentException("That key time is already occupied.");
            PlaneFrame key = new PlaneFrame { Position = position }; for (int i = 0; i < plane.Width * plane.Height; i++) key.Colors.Add("#000000"); effect.Frames.Add(key); Bind(key);
        }
        public void DuplicateKey()
        {
            PlaneFrame source = Current(); if (source == null) return; double at = (double)spatialAddPosition.Value;
            if (effect.Frames.Exists(delegate(PlaneFrame f) { return Math.Abs(f.Position - at) < .0000001; })) throw new ArgumentException("Choose an unused New key time for the duplicate.");
            PlaneFrame copy = new PlaneFrame { Position = at, Colors = new List<string>(source.Colors) }; effect.Frames.Add(copy); Bind(copy);
        }
        public void DeleteKey() { if (effect.Frames.Count < 2) throw new ArgumentException("Keep at least one painted frame."); PlaneFrame key = Current(); if (key == null) return; effect.Frames.Remove(key); Bind(effect.Frames[0]); }
        public void ApplySelectedTime()
        {
            PlaneFrame key = Current(); if (key == null) return; double at = (double)spatialKeyPosition.Value;
            if (effect.Frames.Exists(delegate(PlaneFrame f) { return f != key && Math.Abs(f.Position - at) < .0000001; })) throw new ArgumentException("Another key already uses that time."); key.Position = at; Bind(key);
        }
        private void DrawMatrix(object sender, PaintEventArgs e)
        {
            PlaneFrame key = Current(); if (key == null) return; int cellWidth = Math.Max(1, matrix.ClientSize.Width / plane.Width), cellHeight = Math.Max(1, matrix.ClientSize.Height / plane.Height);
            for (int y = 0; y < plane.Height; y++) for (int x = 0; x < plane.Width; x++)
            {
                int i = y * plane.Width + x; Rectangle cell = new Rectangle(x * cellWidth, y * cellHeight, cellWidth, cellHeight);
                using (Brush fill = new SolidBrush(Model.ParseColor(key.Colors[i]))) e.Graphics.FillRectangle(fill, cell); e.Graphics.DrawRectangle(Pens.DimGray, cell);
                e.Graphics.DrawString(x + "," + y, Font, Brushes.Gray, cell.X + 3, cell.Y + 3);
                if (String.IsNullOrEmpty(plane.Cells[i])) e.Graphics.DrawString("unmapped", Font, Brushes.DarkGray, cell.X + 3, cell.Y + 18);
            }
        }
        private void PaintCell(object sender, MouseEventArgs e)
        {
            PlaneFrame key = Current(); if (key == null) return;
            int x = e.X / Math.Max(1, matrix.ClientSize.Width / plane.Width), y = e.Y / Math.Max(1, matrix.ClientSize.Height / plane.Height);
            if (x < 0 || x >= plane.Width || y < 0 || y >= plane.Height) return;
            key.Colors[y * plane.Width + x] = Model.HtmlColor(e.Button == MouseButtons.Right ? Color.Black : paint); matrix.Invalidate();
        }
        public void PaintAt(int x, int y, Color color) { PlaneFrame key = Current(); if (key == null || x < 0 || x >= plane.Width || y < 0 || y >= plane.Height) throw new ArgumentOutOfRangeException("x"); key.Colors[y * plane.Width + x] = Model.HtmlColor(color); matrix.Invalidate(); }
        private void Run(Action action) { try { action(); } catch (Exception error) { MessageBox.Show(this, error.Message, Text); } }
        protected override void Accept() { ApplySelectedTime(); effect.SpeedDivisor = (int)spatialSpeed.Value; effect.FadeInMs = (int)spatialFadeIn.Value; effect.FadeOutMs = (int)spatialFadeOut.Value; base.Accept(); }
    }
}
