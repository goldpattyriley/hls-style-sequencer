using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class EffectTimingEventArgs : EventArgs
    {
        public string Id; public int StartMs, DurationMs;
    }
    public sealed class LibraryAnchorEventArgs : EventArgs
    {
        public int Row, TimeMs;
    }
    public sealed class TimelineView : ScrollableControl
    {
        public const int LabelWidth = 205, WaveHeight = 54, HeaderHeight = 80, RowHeight = 28;
        public Project Project;
        public string GroupId, SelectedEffectId, HighlightTargetId;
        public int PlayheadMs, FirstClickMs = -1, FirstClickRow = -1, SecondClickMs = -1, SecondClickRow = -1;
        public bool Playing;
        public float PixelsPerSecond = 80;
        public float[] Waveform = new float[0];
        public event EventHandler SelectionFinished;
        public event EventHandler<EffectTimingEventArgs> TimingRequested;
        public event EventHandler<LibraryAnchorEventArgs> LibraryAnchorPicked;
        public event EventHandler LibraryPickCancelled;
        public bool PickingLibraryAnchor;
        private string dragId;
        private int dragStart, dragDuration, dragX, previewStart, previewDuration;
        private bool resizing, moved;

        public TimelineView() { DoubleBuffered = true; BackColor = Color.Black; AutoScroll = true; TabStop = true; }
        public List<string> VisibleRows()
        {
            if (Project == null || String.IsNullOrEmpty(GroupId)) return new List<string>();
            DisplayGroup group = Project.Groups.Find(delegate(DisplayGroup g) { return g.Id == GroupId; });
            return group == null ? new List<string>() : new List<string>(group.Members);
        }
        public float TimeX(int ms) { return LabelWidth + ms * PixelsPerSecond / 1000f; }
        public RectangleF EffectRectangle(Effect effect)
        {
            int row = VisibleRows().IndexOf(effect.TargetId);
            int start = effect.Id == dragId ? previewStart : effect.StartMs;
            int duration = effect.Id == dragId ? previewDuration : effect.DurationMs;
            return new RectangleF(TimeX(start), HeaderHeight + row * RowHeight + 4, Math.Max(3f, duration * PixelsPerSecond / 1000f), RowHeight - 8);
        }
        public bool IsResizeHandle(Effect effect, int logicalX)
        {
            RectangleF box = EffectRectangle(effect);
            return box.Right - logicalX <= 6 && Math.Abs(box.Right - logicalX) <= Math.Abs(logicalX - box.Left);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.Clear(Color.Black);
            if (Project == null) return;
            List<string> rows = VisibleRows();
            int contentWidth = (int)Math.Ceiling(TimeX(Project.DurationMs)) + 12;
            int contentHeight = HeaderHeight + rows.Count * RowHeight + 12;
            Size content = new Size(contentWidth, contentHeight);
            if (AutoScrollMinSize != content) AutoScrollMinSize = content;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            int bottom = Math.Max(ClientSize.Height - AutoScrollPosition.Y, contentHeight);
            using (Pen minor = new Pen(Color.FromArgb(48, 48, 48)))
            using (Pen major = new Pen(Color.FromArgb(115, 115, 115)))
            using (Pen cursor = new Pen(Color.Red, 2))
            {
                g.FillRectangle(Brushes.WhiteSmoke, 0, 0, LabelWidth, HeaderHeight);
                g.DrawString("Audio waveform", Font, Brushes.Black, 6, 12);
                g.DrawString("Work group rows", Font, Brushes.Black, 6, WaveHeight + 8);
                for (int x = 0; x < Waveform.Length; x++)
                {
                    float px = LabelWidth + (contentWidth - LabelWidth - 12) * (float)x / Math.Max(1, Waveform.Length - 1);
                    float amplitude = Waveform[x] * (WaveHeight - 6);
                    g.DrawLine(Pens.White, px, WaveHeight / 2f - amplitude, px, WaveHeight / 2f + amplitude);
                }
                for (int ms = 0; ms <= Project.DurationMs; ms += Project.FrameMs)
                {
                    float px = TimeX(ms);
                    if (px + AutoScrollPosition.X < LabelWidth - 2 || px + AutoScrollPosition.X > ClientSize.Width + 2) continue;
                    if (Project.FrameMs * PixelsPerSecond / 1000f >= 3 || ms % 100 == 0)
                        g.DrawLine(ms % 1000 == 0 ? major : minor, px, WaveHeight, px, bottom);
                    if (ms % 1000 == 0) g.DrawString((ms / 1000) + "s", Font, Brushes.Gainsboro, px + 2, WaveHeight + 6);
                }
                for (int r = 0; r < rows.Count; r++)
                {
                    int y = HeaderHeight + r * RowHeight;
                    g.FillRectangle(rows[r] == HighlightTargetId ? Brushes.LightBlue : Brushes.Gainsboro, 0, y, LabelWidth, RowHeight);
                    string kind = Model.TargetKind(Project, rows[r]);
                    g.DrawString(Model.TargetName(Project, rows[r]) + "  [" + kind + "]", Font, Brushes.Black, new RectangleF(4, y + 5, LabelWidth - 8, RowHeight - 6));
                    g.DrawLine(minor, 0, y, contentWidth, y);
                }
                foreach (Effect effect in Project.Effects)
                {
                    if (!rows.Contains(effect.TargetId)) continue;
                    RectangleF box = EffectRectangle(effect);
                    Color color = effect.Kind == "PixelPlane" ? Color.MediumPurple : Model.ParseColor(effect.ColorHtml);
                    using (Brush fill = new SolidBrush(color))
                    {
                        if (effect.Kind == "RampUp" || effect.Kind == "RampDown")
                        {
                            double from = (effect.UseEndpoints ? effect.StartLevel : effect.Kind == "RampUp" ? 0 : 1) * effect.Level;
                            double to = (effect.UseEndpoints ? effect.EndLevel : effect.Kind == "RampUp" ? 1 : 0) * effect.Level;
                            g.FillPolygon(fill, new PointF[] { new PointF(box.Left, box.Bottom), new PointF(box.Left, box.Bottom - (float)from * box.Height), new PointF(box.Right, box.Bottom - (float)to * box.Height), new PointF(box.Right, box.Bottom) });
                        }
                        else g.FillRectangle(fill, box);
                    }
                    g.DrawRectangle(effect.Id == SelectedEffectId ? Pens.White : Pens.Gray, box.X, box.Y, box.Width, box.Height);
                    if (box.Width > 42) g.DrawString(effect.Kind, Font, Brushes.Black, box.X + 3, box.Y + 2);
                }
                if (FirstClickMs >= 0 && FirstClickRow >= 0)
                {
                    int lastMs = SecondClickMs >= 0 ? SecondClickMs : FirstClickMs;
                    int lastRow = SecondClickRow >= 0 ? SecondClickRow : FirstClickRow;
                    RectangleF selection = new RectangleF(TimeX(Math.Min(FirstClickMs, lastMs)), HeaderHeight + Math.Min(FirstClickRow, lastRow) * RowHeight,
                        Math.Max(2, (Math.Abs(lastMs - FirstClickMs) + Project.FrameMs) * PixelsPerSecond / 1000f), (Math.Abs(lastRow - FirstClickRow) + 1) * RowHeight);
                    g.DrawRectangle(Pens.DodgerBlue, selection.X, selection.Y, selection.Width, selection.Height);
                }
                if (Playing) g.DrawLine(cursor, TimeX(PlayheadMs), 0, TimeX(PlayheadMs), bottom);
                if (rows.Count == 0) g.DrawString("Select a work group, or assign channels to this group.", Font, Brushes.White, LabelWidth + 12, HeaderHeight + 12);
            }
        }
        private Point Logical(Point point) { return new Point(point.X - AutoScrollPosition.X, point.Y - AutoScrollPosition.Y); }
        public int SnapTime(int logicalX)
        {
            if (Project == null) return 0;
            int raw = (int)Math.Floor((logicalX - LabelWidth) * 1000.0 / PixelsPerSecond);
            return Math.Max(0, Math.Min(Project.DurationMs - Project.FrameMs, raw / Project.FrameMs * Project.FrameMs));
        }
        public string HitEffect(Point point)
        {
            if (Project == null) return null; Point logical = Logical(point);
            if (logical.X < LabelWidth || logical.Y < HeaderHeight) return null;
            List<string> rows = VisibleRows();
            for (int i = Project.Effects.Count - 1; i >= 0; i--)
            {
                Effect effect = Project.Effects[i];
                if (rows.Contains(effect.TargetId) && EffectRectangle(effect).Contains(logical)) return effect.Id;
            }
            return null;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (Project == null) return; Focus();
            Point point = Logical(e.Location);
            if (point.Y < HeaderHeight || point.X < LabelWidth) return;
            if (PickingLibraryAnchor && e.Button == MouseButtons.Left)
            {
                int anchorRow = (point.Y - HeaderHeight) / RowHeight;
                if (anchorRow < VisibleRows().Count)
                {
                    EventHandler<LibraryAnchorEventArgs> callback = LibraryAnchorPicked;
                    if (callback != null) callback(this, new LibraryAnchorEventArgs { Row = anchorRow, TimeMs = SnapTime(point.X) });
                }
                return;
            }
            string hit = HitEffect(e.Location);
            if (hit != null)
            {
                SelectedEffectId = hit; ClearRectangle(); Invalidate();
                if (e.Button != MouseButtons.Left) return;
                Effect effect = Project.Effects.Find(delegate(Effect f) { return f.Id == hit; });
                RectangleF box = EffectRectangle(effect);
                bool nearLeft = point.X - box.Left <= 6, nearRight = box.Right - point.X <= 6;
                if (!nearLeft && !nearRight) return;
                resizing = IsResizeHandle(effect, point.X); dragId = hit; dragStart = previewStart = effect.StartMs;
                dragDuration = previewDuration = effect.DurationMs; dragX = point.X; moved = false; Capture = true;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int row = (point.Y - HeaderHeight) / RowHeight;
            if (row < 0 || row >= VisibleRows().Count) return;
            if (FirstClickMs < 0 || SecondClickMs >= 0)
            { FirstClickMs = SnapTime(point.X); FirstClickRow = row; SecondClickMs = SecondClickRow = -1; }
            else
            {
                SecondClickMs = SnapTime(point.X); SecondClickRow = row;
                EventHandler callback = SelectionFinished; if (callback != null) callback(this, EventArgs.Empty);
            }
            SelectedEffectId = null; Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (dragId == null) return;
            int delta = (int)Math.Round((Logical(e.Location).X - dragX) * 1000.0 / PixelsPerSecond / Project.FrameMs) * Project.FrameMs;
            Effect effect = Project.Effects.Find(delegate(Effect f) { return f.Id == dragId; });
            int minimum = (effect.Kind == "Level" ? 1 : 2) * Project.FrameMs;
            if (resizing) previewDuration = Math.Max(minimum, Math.Min(Project.DurationMs - dragStart, dragDuration + delta));
            else previewStart = Math.Max(0, Math.Min(Project.DurationMs - dragDuration, dragStart + delta));
            moved = previewStart != dragStart || previewDuration != dragDuration; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragId != null)
            {
                string id = dragId; dragId = null; Capture = false;
                if (moved)
                {
                    EventHandler<EffectTimingEventArgs> callback = TimingRequested;
                    if (callback != null) callback(this, new EffectTimingEventArgs { Id = id, StartMs = previewStart, DurationMs = previewDuration });
                }
                Invalidate();
            }
            base.OnMouseUp(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                dragId = null; Capture = false; ClearSelection();
                if (PickingLibraryAnchor) { PickingLibraryAnchor = false; EventHandler cancelled = LibraryPickCancelled; if (cancelled != null) cancelled(this, EventArgs.Empty); }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture && dragId != null) { dragId = null; moved = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
        private void ClearRectangle() { FirstClickMs = FirstClickRow = SecondClickMs = SecondClickRow = -1; }
        public void ClearSelection() { ClearRectangle(); SelectedEffectId = null; Invalidate(); }
        public void RevealRow(string id)
        {
            HighlightTargetId = id; int row = VisibleRows().IndexOf(id);
            if (row >= 0) AutoScrollPosition = new Point(-AutoScrollPosition.X, Math.Max(0, HeaderHeight + row * RowHeight - 100));
            Invalidate();
        }
    }
}
