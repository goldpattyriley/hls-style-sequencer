using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class PreviewNodeEventArgs : EventArgs
    {
        public string ElementId;
        public int Node;
        public List<string> ChannelIds;
    }

    // This control deliberately has no window handle or output side effects.  It is also
    // used for the run-time view, where EditMode is false.
    public sealed class LayoutCanvas : Control
    {
        public Project Project;
        public bool EditMode;
        public string SelectedElementId;
        public string Tool = "Select";
        public byte[] Frame;
        public Func<bool> BeforeEdit;
        public string BackgroundWarning { get { return backgroundWarning; } }
        public event EventHandler LayoutChanged;
        public event EventHandler<PreviewNodeEventArgs> NodeSelected;
        private Image background;
        private string backgroundIdentity = "", backgroundWarning = "";
        private Point mouseDown;
        private List<LayoutPoint> originalPoints;
        private int handle = -1;
        private bool creating, moving;

        public LayoutCanvas()
        {
            DoubleBuffered = true; BackColor = Color.FromArgb(36, 36, 36);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        private new PreviewLayout Layout { get { return Project == null ? null : Project.Layout; } }
        private RectangleF Area()
        {
            PreviewLayout l = Layout;
            if (l == null) return ClientRectangle;
            float w = Math.Max(1, ClientSize.Width - 16), h = Math.Max(1, ClientSize.Height - 34);
            float aspect = l.Width <= 0 || l.Height <= 0 ? 1 : (float)l.Width / l.Height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;
            return new RectangleF((ClientSize.Width - w) / 2f, 18 + (ClientSize.Height - 34 - h) / 2f, w, h);
        }
        private PointF ToScreen(LayoutPoint p)
        {
            RectangleF a = Area(); return new PointF(a.Left + (float)(Clamp(p.X) * a.Width), a.Top + (float)(Clamp(p.Y) * a.Height));
        }
        private LayoutPoint ToLayout(Point p)
        {
            RectangleF a = Area(); return new LayoutPoint { X = Clamp((p.X - a.Left) / a.Width), Y = Clamp((p.Y - a.Top) / a.Height) };
        }
        private static double Clamp(double n) { return n < 0 ? 0 : n > 1 ? 1 : n; }
        private static List<LayoutPoint> Copy(List<LayoutPoint> source)
        {
            List<LayoutPoint> result = new List<LayoutPoint>(); if (source != null) foreach (LayoutPoint p in source) result.Add(new LayoutPoint { X = p.X, Y = p.Y }); return result;
        }
        private PreviewElement Element(string id)
        {
            if (Layout == null || Layout.Elements == null) return null;
            return Layout.Elements.Find(delegate(PreviewElement e) { return e != null && e.Id == id; });
        }
        private void LoadBackground()
        {
            string path = Layout == null ? "" : Layout.BackgroundImage ?? "";
            string identity = BackgroundFileIdentity(path);
            if (String.Equals(identity, backgroundIdentity, StringComparison.Ordinal)) return;
            Image old = background; background = null; backgroundIdentity = identity; backgroundWarning = "";
            if (old != null) old.Dispose();
            if (String.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) { backgroundWarning = "Preview background image is missing: " + path; return; }
            try
            {
                // Clone while the stream is still open; the chosen photograph remains editable.
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(stream, true, true))
                {
                    if (source.Width < 1 || source.Height < 1 || source.Width > 8192 || source.Height > 8192 || ((long)source.Width * source.Height) > 16000000L) { backgroundWarning = "Preview background image exceeds the 8192px / 16M-pixel safety limit."; return; }
                    background = new Bitmap(source);
                }
            }
            catch { backgroundWarning = "Preview background image cannot be decoded."; }
        }
        public void RefreshBackgroundStatus() { LoadBackground(); }
        private static string BackgroundFileIdentity(string path)
        {
            if (String.IsNullOrEmpty(path)) return ""; try { FileInfo file = new FileInfo(path); return file.FullName + "|" + (file.Exists ? file.Length + "|" + file.LastWriteTimeUtc.Ticks : "missing"); } catch { return "invalid|" + path; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(BackColor); RectangleF a = Area();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush b = new SolidBrush(Color.FromArgb(25, Color.White))) e.Graphics.FillRectangle(b, a);
            LoadBackground();
            if (background != null)
            {
                float opacity = (float)(Layout == null ? .5 : Clamp(Layout.BackgroundOpacity));
                using (ImageAttributesLite attrs = new ImageAttributesLite(opacity)) attrs.Draw(e.Graphics, background, Rectangle.Round(a));
            }
            using (Pen border = new Pen(Color.DimGray)) e.Graphics.DrawRectangle(border, Rectangle.Round(a));
            if (Layout == null || Layout.Elements == null) { DrawMessage(e.Graphics, "Create a preview layout to draw it here."); return; }
            bool current = PreviewLayouts.IsCurrent(Project), activeFrame = Frame != null && current;
            foreach (PreviewElement item in Layout.Elements) DrawElement(e.Graphics, item, item.Id == SelectedElementId, activeFrame, current);
            if (!EditMode && !current) DrawMessage(e.Graphics, "Preview control file is not compiled/identified; editor layout shown only.");
            else if (Layout.Elements.Count == 0) DrawMessage(e.Graphics, "Use a drawing tool to add preview objects.");
            if (!String.IsNullOrEmpty(backgroundWarning)) DrawMessage(e.Graphics, backgroundWarning);
        }
        private void DrawMessage(Graphics g, string message)
        {
            using (Brush b = new SolidBrush(Color.Gainsboro)) g.DrawString(message, Font, b, 8, 2);
        }
        private void DrawElement(Graphics g, PreviewElement item, bool selected, bool activeFrame, bool current)
        {
            if (item == null) return;
            List<LayoutPoint> outline = PreviewLayouts.Outline(item);
            if (outline == null || outline.Count == 0) return;
            PointF[] points = new PointF[outline.Count]; for (int i = 0; i < points.Length; i++) points[i] = ToScreen(outline[i]);
            Color outlineColor = selected ? Color.Gold : (!EditMode && !activeFrame ? Color.DimGray : Color.FromArgb(160, Color.White));
            using (Pen line = new Pen(outlineColor, selected ? 2.5f : 1.4f))
            {
                if (points.Length == 1) g.DrawEllipse(line, points[0].X - 4, points[0].Y - 4, 8, 8);
                else g.DrawLines(line, points);
            }
            int count = Math.Max(1, item.NodeCount);
            List<LayoutPoint> nodes = PreviewLayouts.Nodes(item);
            if (count == 1 && activeFrame && nodes != null && nodes.Count != 0)
            {
                Color color = PreviewLayouts.NodeColor(Project, item, 0, Frame); using (Pen lit = new Pen(color, Math.Max(2, (float)item.PixelSize))) { if (points.Length > 1) g.DrawLines(lit, points); else g.DrawEllipse(lit, points[0].X - 3, points[0].Y - 3, 6, 6); }
            }
            if (count > 1 && nodes != null)
                for (int i = 0; i < nodes.Count; i++)
                {
                    Color color = activeFrame ? PreviewLayouts.NodeColor(Project, item, i, Frame) : Color.FromArgb(105, Color.LightGray);
                    PointF p = ToScreen(nodes[i]); float radius = Math.Max(2, (float)item.PixelSize / 2f);
                    using (Brush bulb = new SolidBrush(color)) g.FillEllipse(bulb, p.X - radius, p.Y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(Pens.Black, p.X - radius, p.Y - radius, radius * 2, radius * 2);
                }
            if (selected && EditMode)
            {
                foreach (LayoutPoint p in item.Points ?? new List<LayoutPoint>())
                {
                    PointF s = ToScreen(p); using (Brush h = new SolidBrush(Color.Gold)) g.FillRectangle(h, s.X - 3, s.Y - 3, 6, 6);
                    g.DrawRectangle(Pens.Black, s.X - 3, s.Y - 3, 6, 6);
                }
                if (!String.IsNullOrEmpty(item.Name)) g.DrawString(item.Name + "  G" + item.Group + " N" + item.Number, Font, Brushes.Gold, points[0]);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!EditMode || Layout == null || e.Button != MouseButtons.Left) { SelectNode(e.Location); return; }
            if (BeforeEdit != null && !BeforeEdit()) return;
            mouseDown = e.Location; LayoutPoint at = ToLayout(e.Location);
            if (Tool != null && Tool != "Select")
            {
                PreviewElement extending = Element(SelectedElementId);
                // The first drag establishes the first segment.  Subsequent clicks add
                // vertices; double-click below commits the polyline and restores select.
                if (Tool == "Polyline" && extending != null && extending.Kind == "Polyline" && extending.Points != null && extending.Points.Count >= 2)
                {
                    extending.Points.Add(at); Notify(); return;
                }
                PreviewElement created = new PreviewElement { Id = Model.NewId(), Name = NextName(Tool), Kind = Tool, Group = 1, Number = 1, NodeCount = 1, Points = new List<LayoutPoint>() };
                created.Points.Add(at); if (Tool != "Point") created.Points.Add(at);
                if (Tool == "Matrix") { created.Rows = 1; created.Columns = 1; }
                Layout.Elements.Add(created); SelectedElementId = created.Id; creating = true; Notify(); return;
            }
            PreviewElement item; int itemHandle;
            if (HitHandle(e.Location, out item, out itemHandle)) { SelectedElementId = item.Id; handle = itemHandle; originalPoints = Copy(item.Points); return; }
            item = HitElement(e.Location);
            if (item != null) { SelectedElementId = item.Id; moving = true; originalPoints = Copy(item.Points); Notify(); }
            else { SelectedElementId = null; Notify(); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); PreviewElement item = Element(SelectedElementId); if (item == null) return;
            if (creating && item.Points.Count > 0)
            {
                LayoutPoint p = ToLayout(e.Location);
                if (item.Kind == "Point") item.Points[0] = p; else item.Points[item.Points.Count - 1] = p;
                Invalidate(); return;
            }
            if (handle >= 0 && originalPoints != null && handle < item.Points.Count) { item.Points[handle] = ToLayout(e.Location); Invalidate(); return; }
            if (moving && originalPoints != null)
            {
                LayoutPoint before = ToLayout(mouseDown), now = ToLayout(e.Location); double dx = now.X - before.X, dy = now.Y - before.Y, minX = 1, maxX = 0, minY = 1, maxY = 0;
                for (int i = 0; i < originalPoints.Count; i++) { minX = Math.Min(minX, originalPoints[i].X); maxX = Math.Max(maxX, originalPoints[i].X); minY = Math.Min(minY, originalPoints[i].Y); maxY = Math.Max(maxY, originalPoints[i].Y); }
                dx = Math.Max(-minX, Math.Min(1 - maxX, dx)); dy = Math.Max(-minY, Math.Min(1 - maxY, dy));
                for (int i = 0; i < item.Points.Count && i < originalPoints.Count; i++) item.Points[i] = new LayoutPoint { X = originalPoints[i].X + dx, Y = originalPoints[i].Y + dy };
                Invalidate();
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (creating || moving || handle >= 0) Notify(); creating = moving = false; handle = -1; originalPoints = null;
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // A polyline is extended with clicks; double-click completes it and returns to select.
            PreviewElement item = Element(SelectedElementId);
            if (EditMode && item != null && item.Kind == "Polyline" && Tool == "Polyline") { Tool = "Select"; Notify(); }
        }
        private string NextName(string kind)
        {
            int n = 1; while (Layout.Elements.Exists(delegate(PreviewElement p) { return p.Name == kind + " " + n; })) n++; return kind + " " + n;
        }
        private bool HitHandle(Point p, out PreviewElement found, out int index)
        {
            found = null; index = -1; PreviewElement item = Element(SelectedElementId); if (item == null || item.Points == null) return false;
            for (int i = 0; i < item.Points.Count; i++) { PointF s = ToScreen(item.Points[i]); if (Math.Abs(s.X - p.X) <= 7 && Math.Abs(s.Y - p.Y) <= 7) { found = item; index = i; return true; } } return false;
        }
        private PreviewElement HitElement(Point p)
        {
            if (Layout.Elements == null) return null;
            for (int k = Layout.Elements.Count - 1; k >= 0; k--)
            {
                PreviewElement item = Layout.Elements[k]; List<LayoutPoint> nodes = PreviewLayouts.Nodes(item);
                if (nodes != null) foreach (LayoutPoint node in nodes) { PointF s = ToScreen(node); if (Distance(s, p) < 12) return item; }
                if (DistanceToOutline(item, p) <= 8) return item;
            }
            return null;
        }
        private void SelectNode(Point p)
        {
            if (Layout == null || Layout.Elements == null) return;
            for (int k = Layout.Elements.Count - 1; k >= 0; k--)
            {
                PreviewElement item = Layout.Elements[k];
                List<LayoutPoint> nodes = PreviewLayouts.Nodes(item); if (nodes == null) continue;
                for (int i = 0; i < nodes.Count; i++) if (Distance(ToScreen(nodes[i]), p) < 12)
                {
                    SelectedElementId = item.Id; EventHandler<PreviewNodeEventArgs> h = NodeSelected;
                    if (h != null) h(this, new PreviewNodeEventArgs { ElementId = item.Id, Node = i, ChannelIds = PreviewLayouts.ChannelsForNode(Project, item, i) });
                    Invalidate(); return;
                }
                if (item.NodeCount == 1 && DistanceToOutline(item, p) <= 8)
                {
                    SelectedElementId = item.Id; EventHandler<PreviewNodeEventArgs> h = NodeSelected;
                    if (h != null) h(this, new PreviewNodeEventArgs { ElementId = item.Id, Node = 0, ChannelIds = PreviewLayouts.ChannelsForNode(Project, item, 0) });
                    Invalidate(); return;
                }
            }
        }
        private float DistanceToOutline(PreviewElement item, Point point)
        {
            List<LayoutPoint> outline = PreviewLayouts.Outline(item); if (outline == null || outline.Count == 0) return Single.MaxValue;
            if (outline.Count == 1) return Distance(ToScreen(outline[0]), point); float best = Single.MaxValue;
            for (int i = 1; i < outline.Count; i++) best = Math.Min(best, SegmentDistance(ToScreen(outline[i - 1]), ToScreen(outline[i]), point)); return best;
        }
        private static float SegmentDistance(PointF a, PointF b, Point p)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y; if (dx == 0 && dy == 0) return Distance(a, p); float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy); t = Math.Max(0, Math.Min(1, t)); float x = a.X + dx * t, y = a.Y + dy * t; float ox = x - p.X, oy = y - p.Y; return (float)Math.Sqrt(ox * ox + oy * oy);
        }
        private static float Distance(PointF a, Point b) { float x = a.X - b.X, y = a.Y - b.Y; return (float)Math.Sqrt(x * x + y * y); }
        private void Notify() { Invalidate(); EventHandler h = LayoutChanged; if (h != null) h(this, EventArgs.Empty); }
        protected override void Dispose(bool disposing) { if (disposing && background != null) { background.Dispose(); background = null; } base.Dispose(disposing); }
    }

    // ImageAttributes is awkward to use repeatedly without leaking state on .NET 4.
    internal sealed class ImageAttributesLite : IDisposable
    {
        private readonly System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes();
        public ImageAttributesLite(float opacity)
        {
            System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix(); matrix.Matrix33 = opacity;
            attributes.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
        }
        public void Draw(Graphics graphics, Image image, Rectangle target) { graphics.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes); }
        public void Dispose() { attributes.Dispose(); }
    }
}
