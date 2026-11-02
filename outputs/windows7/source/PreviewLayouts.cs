using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace HlsRecreation
{
    public class LayoutPoint { public double X; public double Y; }
    public class PreviewElement
    {
        public string Id = ""; public string Name = ""; public string Kind = "Line";
        public int Group = 1; public int Number = 1; public int NodeCount = 1;
        public List<LayoutPoint> Points = new List<LayoutPoint>(); public int Rows = 1; public int Columns = 1;
        public bool Serpentine; public bool Closed; public double PixelSize = 5;
    }
    public class PreviewLayout
    {
        public int Width = 1000; public int Height = 700; public string BackgroundImage = ""; public double BackgroundOpacity = .5;
        public List<PreviewElement> Elements = new List<PreviewElement>(); public bool Enabled; public string ControlFile = ""; public string BuildSignature = "";
        public string ControlHash = "";
    }
    public class PreviewAssignment { public string ChannelName = ""; public int Group; public int Number; public int Pixel; }
    public class PreviewControl { public int Version = 1; public PreviewLayout Layout = new PreviewLayout(); public List<PreviewAssignment> Assignments = new List<PreviewAssignment>(); }

    public static class PreviewLayouts
    {
        public static PreviewLayout Clone(PreviewLayout source)
        {
            PreviewLayout copy = new PreviewLayout(); if (source == null) return copy;
            copy.Width = source.Width; copy.Height = source.Height; copy.BackgroundImage = source.BackgroundImage; copy.BackgroundOpacity = source.BackgroundOpacity;
            copy.Enabled = source.Enabled; copy.ControlFile = source.ControlFile; copy.BuildSignature = source.BuildSignature; copy.ControlHash = source.ControlHash;
            if (source.Elements == null) { copy.Elements = null; return copy; }
            int i, j; for (i = 0; i < source.Elements.Count; i++)
            {
                PreviewElement x = source.Elements[i]; if (x == null) { copy.Elements.Add(null); continue; }
                PreviewElement e = new PreviewElement { Id = x.Id, Name = x.Name, Kind = x.Kind, Group = x.Group, Number = x.Number, NodeCount = x.NodeCount, Rows = x.Rows, Columns = x.Columns, Serpentine = x.Serpentine, Closed = x.Closed, PixelSize = x.PixelSize };
                if (x.Points == null) e.Points = null; else for (j = 0; j < x.Points.Count; j++) e.Points.Add(x.Points[j] == null ? null : new LayoutPoint { X = x.Points[j].X, Y = x.Points[j].Y });
                copy.Elements.Add(e);
            }
            return copy;
        }

        public static List<string> Validate(Project project)
        {
            List<string> errors = new List<string>(); if (project == null) { errors.Add("Project is required."); return errors; }
            PreviewLayout layout = project.Layout; if (layout == null) { errors.Add("Preview layout is required."); return errors; }
            if (layout.Width < 1 || layout.Width > 16384 || layout.Height < 1 || layout.Height > 16384) errors.Add("Preview dimensions must be 1..16384.");
            if (!Finite(layout.BackgroundOpacity) || layout.BackgroundOpacity < 0 || layout.BackgroundOpacity > 1) errors.Add("Preview background opacity must be 0..1.");
            if (layout.BackgroundImage == null || layout.BackgroundImage.Length > 4096 || layout.BackgroundImage.IndexOf("://", StringComparison.Ordinal) >= 0) errors.Add("Preview background must be a local path or blank.");
            if (layout.ControlFile == null || layout.ControlFile.Length > 4096 || layout.BuildSignature == null || layout.BuildSignature.Length > 128 || layout.ControlHash == null || layout.ControlHash.Length > 128) errors.Add("Preview control metadata is invalid.");
            if (layout.Elements == null) { errors.Add("Preview elements list is required."); return errors; }
            if (layout.Elements.Count > 2048) { errors.Add("Preview supports at most 2048 elements."); return errors; }
            Dictionary<string, bool> ids = new Dictionary<string, bool>(StringComparer.Ordinal); Dictionary<string, bool> names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            int i; for (i = 0; i < layout.Elements.Count; i++) ElementErrors(errors, layout.Elements[i], i, ids, names); return errors;
        }

        public static List<string> Diagnostics(Project project)
        {
            List<string> messages = new List<string>(); List<string> errors = Validate(project); if (errors.Count != 0) { messages.AddRange(errors); return messages; }
            Dictionary<string, RawChannel> mapped = new Dictionary<string, RawChannel>(StringComparer.Ordinal); Dictionary<string, int> limits = new Dictionary<string, int>(StringComparer.Ordinal); int i;
            for (i = 0; i < project.Layout.Elements.Count; i++) { PreviewElement shape = project.Layout.Elements[i]; if (shape != null) { string pair = Key(shape.Group, shape.Number, 0); int known; limits.TryGetValue(pair, out known); limits[pair] = Math.Max(known, shape.NodeCount); } }
            for (i = 0; i < project.Channels.Count; i++) { RawChannel raw = project.Channels[i]; if (raw != null && raw.PreviewGroup > 0 && raw.PreviewNumber > 0 && raw.PreviewPixel > 0) mapped[Key(raw.PreviewGroup, raw.PreviewNumber, raw.PreviewPixel)] = raw; }
            for (i = 0; i < project.Layout.Elements.Count; i++)
            {
                PreviewElement e = project.Layout.Elements[i]; if (e == null) continue; int n;
                for (n = 1; n <= e.NodeCount; n++) if (!mapped.ContainsKey(Key(e.Group, e.Number, n))) messages.Add("Preview " + Label(e) + " node " + n.ToString(CultureInfo.InvariantCulture) + " has no raw channel mapping.");
            }
            for (i = 0; i < project.Channels.Count; i++)
            {
                RawChannel raw = project.Channels[i]; if (raw == null) continue;
                if (raw.PreviewGroup == 0 && raw.PreviewNumber == 0 && raw.PreviewPixel == 0) continue;
                string pair = Key(raw.PreviewGroup, raw.PreviewNumber, 0); int limit;
                if (raw.PreviewGroup < 1 || raw.PreviewNumber < 1 || raw.PreviewPixel < 1) messages.Add("Raw " + raw.Name + " has an incomplete preview mapping.");
                else if (!limits.TryGetValue(pair, out limit)) messages.Add("Raw " + raw.Name + " maps to missing preview G" + raw.PreviewGroup + " / N" + raw.PreviewNumber + ".");
                else if (raw.PreviewPixel > limit) messages.Add("Raw " + raw.Name + " maps to pixel " + raw.PreviewPixel + " beyond G" + raw.PreviewGroup + " / N" + raw.PreviewNumber + ".");
            }
            if (project.Layout.Enabled && !IsCurrent(project)) messages.Add("The identified Preview Control File is missing or stale; compile and identify it again.");
            return messages;
        }

        public static string Fingerprint(Project project)
        {
            if (project == null || project.Layout == null) return "";
            StringBuilder text = new StringBuilder(); PreviewLayout layout = project.Layout;
            text.Append(layout.Width).Append('|').Append(layout.Height).Append('|').Append(Invariant(layout.BackgroundOpacity)).Append('|').Append(BackgroundIdentity(layout.BackgroundImage)).Append('\n');
            int i, j; if (layout.Elements != null) for (i = 0; i < layout.Elements.Count; i++) { PreviewElement e = layout.Elements[i]; if (e == null) { text.Append("<null>\n"); continue; } text.Append(e.Id).Append('|').Append(e.Name).Append('|').Append(e.Kind).Append('|').Append(e.Group).Append('|').Append(e.Number).Append('|').Append(e.NodeCount).Append('|').Append(e.Rows).Append('|').Append(e.Columns).Append('|').Append(e.Serpentine).Append('|').Append(e.Closed).Append('|').Append(Invariant(e.PixelSize)); if (e.Points != null) for (j = 0; j < e.Points.Count; j++) { LayoutPoint p = e.Points[j]; text.Append('|').Append(p == null ? "null" : Invariant(p.X) + "," + Invariant(p.Y)); } text.Append('\n'); }
            if (project.Channels != null) for (i = 0; i < project.Channels.Count; i++) { RawChannel r = project.Channels[i]; if (r != null) text.Append(r.Name).Append('|').Append(r.ColorHtml).Append('|').Append(r.PreviewGroup).Append('|').Append(r.PreviewNumber).Append('|').Append(r.PreviewPixel).Append('\n'); }
            using (SHA256 sha = SHA256.Create()) { byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())); StringBuilder hex = new StringBuilder(); for (i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture)); return hex.ToString(); }
        }

        public static List<LayoutPoint> Outline(PreviewElement e)
        {
            List<LayoutPoint> result = new List<LayoutPoint>(); if (e == null || e.Points == null || e.Points.Count == 0) return result;
            if (e.Kind == "Point") { result.Add(Copy(e.Points[0])); return result; }
            if (e.Kind == "Arch" && e.Points.Count >= 2)
            {
                LayoutPoint a = e.Points[0], b = e.Points[1]; double left = Math.Min(a.X, b.X), right = Math.Max(a.X, b.X), top = Math.Min(a.Y, b.Y), bottom = Math.Max(a.Y, b.Y), radiusX = (right - left) / 2.0, center = (left + right) / 2.0, radiusY = bottom - top; int i;
                for (i = 0; i <= 32; i++) { double t = Math.PI - ((Math.PI * i) / 32.0); result.Add(new LayoutPoint { X = center + radiusX * Math.Cos(t), Y = bottom - radiusY * Math.Sin(t) }); } return result;
            }
            if ((e.Kind == "Rectangle" || e.Kind == "Matrix") && e.Points.Count >= 2)
            { LayoutPoint a = e.Points[0], b = e.Points[1]; result.Add(new LayoutPoint { X = a.X, Y = a.Y }); result.Add(new LayoutPoint { X = b.X, Y = a.Y }); result.Add(new LayoutPoint { X = b.X, Y = b.Y }); result.Add(new LayoutPoint { X = a.X, Y = b.Y }); result.Add(new LayoutPoint { X = a.X, Y = a.Y }); return result; }
            int j; for (j = 0; j < e.Points.Count; j++) result.Add(Copy(e.Points[j])); if (e.Kind == "Polyline" && e.Closed && result.Count > 1) result.Add(Copy(result[0])); return result;
        }

        public static List<LayoutPoint> Nodes(PreviewElement e)
        {
            List<LayoutPoint> result = new List<LayoutPoint>(); if (e == null || e.NodeCount < 1) return result;
            if (e.Kind == "Matrix" && e.Points != null && e.Points.Count >= 2)
            {
                LayoutPoint a = e.Points[0], b = e.Points[1]; int row, col; for (row = 0; row < e.Rows; row++) for (col = 0; col < e.Columns; col++) { int use = e.Serpentine && (row % 2 == 1) ? e.Columns - 1 - col : col; result.Add(new LayoutPoint { X = a.X + ((b.X - a.X) * use / Math.Max(1, e.Columns - 1)), Y = a.Y + ((b.Y - a.Y) * row / Math.Max(1, e.Rows - 1)) }); } return result;
            }
            List<LayoutPoint> path = Outline(e); if (path.Count == 0) return result; if (e.NodeCount == 1) { result.Add(Copy(path[0])); return result; }
            double total = 0; int i; for (i = 1; i < path.Count; i++) total += Distance(path[i - 1], path[i]); if (total == 0) { for (i = 0; i < e.NodeCount; i++) result.Add(Copy(path[0])); return result; }
            bool closed = (e.Kind == "Rectangle" || (e.Kind == "Polyline" && e.Closed)) && path.Count > 2;
            for (i = 0; i < e.NodeCount; i++) result.Add(AtDistance(path, total * i / (closed ? e.NodeCount : e.NodeCount - 1))); return result;
        }

        public static Color NodeColor(Project project, PreviewElement e, int zeroBasedNode, byte[] frame)
        {
            if (project == null || e == null || frame == null || zeroBasedNode < 0 || zeroBasedNode >= e.NodeCount) return Color.Black;
            int r = 0, g = 0, b = 0, pixel = zeroBasedNode + 1, i; if (project.Channels == null) return Color.Black;
            for (i = 0; i < project.Channels.Count; i++) { RawChannel raw = project.Channels[i]; if (raw == null || raw.PreviewGroup != e.Group || raw.PreviewNumber != e.Number || raw.PreviewPixel != pixel || raw.Universe == 0) continue; int offset = Model.OutputOffset(raw); if (offset < 0 || offset >= frame.Length) continue; if (raw.Kind == "RGB") { r = Math.Max(r, frame[offset]); if (offset + 1 < frame.Length) g = Math.Max(g, frame[offset + 1]); if (offset + 2 < frame.Length) b = Math.Max(b, frame[offset + 2]); } else { Color tint; try { tint = Model.ParseColor(raw.ColorHtml); } catch (ArgumentException) { tint = Color.White; } int value = frame[offset]; r = Math.Max(r, tint.R * value / 255); g = Math.Max(g, tint.G * value / 255); b = Math.Max(b, tint.B * value / 255); } }
            return Color.FromArgb(r, g, b);
        }

        public static List<string> ChannelsForNode(Project project, PreviewElement e, int zeroBasedNode)
        {
            List<string> ids = new List<string>(); if (project == null || project.Channels == null || e == null || zeroBasedNode < 0 || zeroBasedNode >= e.NodeCount) return ids; int pixel = zeroBasedNode + 1, i;
            for (i = 0; i < project.Channels.Count; i++) { RawChannel raw = project.Channels[i]; if (raw != null && raw.PreviewGroup == e.Group && raw.PreviewNumber == e.Number && raw.PreviewPixel == pixel) ids.Add(raw.Id); } return ids;
        }

        public static Project Assign(Project project, string elementId, IList<string> rawIds, bool reverse)
        {
            if (project == null || rawIds == null) throw new ArgumentNullException(project == null ? "project" : "rawIds"); Project candidate = Model.Clone(project); PreviewElement e = Find(candidate.Layout, elementId); if (e == null) throw new ArgumentException("Unknown preview element.", "elementId");
            if (e.NodeCount > 1 && rawIds.Count != e.NodeCount) throw new ArgumentException("A multi-node preview element needs exactly one selected raw channel per node.", "rawIds"); if (e.NodeCount == 1 && rawIds.Count < 1) throw new ArgumentException("Select at least one raw channel.", "rawIds");
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal); int i; for (i = 0; i < rawIds.Count; i++) { if (!unique.Add(rawIds[i])) throw new ArgumentException("A raw channel may be assigned only once in one operation.", "rawIds"); RawChannel raw = Raw(candidate, rawIds[i]); if (raw == null) throw new ArgumentException("Unknown raw channel.", "rawIds"); raw.PreviewGroup = e.Group; raw.PreviewNumber = e.Number; raw.PreviewPixel = e.NodeCount == 1 ? 1 : (reverse ? e.NodeCount - i : i + 1); }
            List<string> errors = Validate(candidate); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()), "project"); return candidate;
        }

        public static Project Compile(Project project, string localPath)
        {
            if (String.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("A preview control path is required.", "localPath"); List<string> errors = Validate(project); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()), "project");
            Project candidate = Model.Clone(project); string full = Path.GetFullPath(localPath); PreviewControl control = new PreviewControl(); control.Layout = Clone(candidate.Layout); control.Layout.Enabled = false; control.Layout.ControlFile = ""; control.Layout.BuildSignature = ""; control.Layout.ControlHash = "";
            int i; for (i = 0; i < candidate.Channels.Count; i++) { RawChannel raw = candidate.Channels[i]; if (raw != null && raw.PreviewGroup > 0 && raw.PreviewNumber > 0 && raw.PreviewPixel > 0) control.Assignments.Add(new PreviewAssignment { ChannelName = raw.Name, Group = raw.PreviewGroup, Number = raw.PreviewNumber, Pixel = raw.PreviewPixel }); }
            string dir = Path.GetDirectoryName(full); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { XmlWriterSettings settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }; using (FileStream file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) using (XmlWriter writer = XmlWriter.Create(file, settings)) new XmlSerializer(typeof(PreviewControl)).Serialize(writer, control); using (FileStream verify = File.OpenRead(temp)) SequenceXml.Preflight(verify, "PreviewControl"); if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            candidate.Layout.Enabled = false; candidate.Layout.ControlFile = full; candidate.Layout.ControlHash = ControlFileHash(full); candidate.Layout.BuildSignature = Fingerprint(candidate); return candidate;
        }

        public static Project LoadInto(Project project, string localPath)
        {
            if (project == null) throw new ArgumentNullException("project"); string full = Path.GetFullPath(localPath); PreviewControl control; using (FileStream input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)) { SequenceXml.Preflight(input, "PreviewControl"); using (XmlReader reader = XmlReader.Create(input, SequenceXml.Settings())) control = (PreviewControl)new XmlSerializer(typeof(PreviewControl)).Deserialize(reader); }
            if (control == null || control.Version != 1 || control.Layout == null || control.Assignments == null) throw new InvalidDataException("Unsupported preview control file."); Project candidate = Model.Clone(project); candidate.Layout = Clone(control.Layout); candidate.Layout.Enabled = true; candidate.Layout.ControlFile = full;
            Dictionary<string, RawChannel> names = new Dictionary<string, RawChannel>(StringComparer.OrdinalIgnoreCase); int i; for (i = 0; i < candidate.Channels.Count; i++) if (candidate.Channels[i] != null) { candidate.Channels[i].PreviewGroup = 0; candidate.Channels[i].PreviewNumber = 0; candidate.Channels[i].PreviewPixel = 0; names[candidate.Channels[i].Name] = candidate.Channels[i]; }
            HashSet<string> assignedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (i = 0; i < control.Assignments.Count; i++) { PreviewAssignment a = control.Assignments[i]; RawChannel raw; if (a == null || String.IsNullOrWhiteSpace(a.ChannelName) || !assignedNames.Add(a.ChannelName) || !names.TryGetValue(a.ChannelName, out raw)) throw new InvalidDataException("Preview control has a blank, duplicate, or unavailable channel assignment: " + (a == null ? "(blank)" : a.ChannelName)); raw.PreviewGroup = a.Group; raw.PreviewNumber = a.Number; raw.PreviewPixel = a.Pixel; }
            List<string> errors = Validate(candidate); if (errors.Count != 0) throw new InvalidDataException(String.Join(" ", errors.ToArray())); candidate.Layout.ControlHash = ControlFileHash(full); candidate.Layout.BuildSignature = Fingerprint(candidate);
            try { Model.RequireValid(candidate); } catch (ArgumentException error) { throw new InvalidDataException("Preview control is not valid for this project: " + error.Message); }
            return candidate;
        }

        public static bool IsCurrent(Project project) { return project != null && project.Layout != null && project.Layout.Enabled && !String.IsNullOrWhiteSpace(project.Layout.ControlFile) && File.Exists(project.Layout.ControlFile) && String.Equals(project.Layout.BuildSignature, Fingerprint(project), StringComparison.Ordinal) && !String.IsNullOrEmpty(project.Layout.ControlHash) && project.Layout.ControlHash == ControlFileHash(project.Layout.ControlFile); }

        private sealed class ControlIdentity { public string Identity,Hash; }
        private static readonly Dictionary<string,ControlIdentity> controlHashes=new Dictionary<string,ControlIdentity>(StringComparer.OrdinalIgnoreCase);
        private static string ControlFileHash(string path)
        {
            try
            {
                FileInfo file=new FileInfo(path);string identity=file.Length+"|"+file.LastWriteTimeUtc.Ticks;
                lock(controlHashes)
                {
                    ControlIdentity cached;if(controlHashes.TryGetValue(file.FullName,out cached)&&cached.Identity==identity)return cached.Hash;
                    byte[] hash;using(FileStream input=File.OpenRead(file.FullName))using(SHA256 sha=SHA256.Create())hash=sha.ComputeHash(input);
                    string value=BitConverter.ToString(hash).Replace("-","");if(controlHashes.Count>=32)controlHashes.Clear();controlHashes[file.FullName]=new ControlIdentity{Identity=identity,Hash=value};return value;
                }
            }
            catch{return "";}
        }
        private static void ElementErrors(List<string> errors, PreviewElement e, int index, Dictionary<string, bool> ids, Dictionary<string, bool> names)
        {
            string where = "Preview element " + index.ToString(CultureInfo.InvariantCulture); if (e == null) { errors.Add(where + " is null."); return; } if (String.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 128 || ids.ContainsKey(e.Id)) errors.Add(where + " Id is missing, too long, or duplicate."); else ids.Add(e.Id, true); if (String.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 256 || names.ContainsKey(e.Name)) errors.Add(where + " Name is missing, too long, or duplicate."); else names.Add(e.Name, true);
            if (e.Kind != "Point" && e.Kind != "Line" && e.Kind != "Arch" && e.Kind != "Rectangle" && e.Kind != "Matrix" && e.Kind != "Polyline") errors.Add(where + " has an invalid Kind."); if (e.Group < 1 || e.Group > 32768 || e.Number < 1 || e.Number > 32768) errors.Add(where + " group and number must be 1..32768."); if (!Finite(e.PixelSize) || e.PixelSize <= 0 || e.PixelSize > 1024) errors.Add(where + " pixel size is invalid."); if (e.Points == null) { errors.Add(where + " points are required."); return; }
            int min = e.Kind == "Point" ? 1 : 2, max = e.Kind == "Polyline" ? 128 : 2; if (e.Points.Count < min || e.Points.Count > max) errors.Add(where + " has an invalid point count."); int i; for (i = 0; i < e.Points.Count; i++) if (e.Points[i] == null || !Finite(e.Points[i].X) || !Finite(e.Points[i].Y) || e.Points[i].X < 0 || e.Points[i].X > 1 || e.Points[i].Y < 0 || e.Points[i].Y > 1) errors.Add(where + " has a non-normalized point.");
            if (e.Kind == "Matrix") { if (e.Rows < 1 || e.Rows > 32 || e.Columns < 1 || e.Columns > 32 || e.NodeCount != e.Rows * e.Columns) errors.Add(where + " matrix dimensions/node count are invalid."); } else if (e.NodeCount < 1 || e.NodeCount > 32768) errors.Add(where + " node count must be 1..32768.");
        }
        private static PreviewElement Find(PreviewLayout layout, string id) { if (layout == null || layout.Elements == null) return null; int i; for (i = 0; i < layout.Elements.Count; i++) if (layout.Elements[i] != null && layout.Elements[i].Id == id) return layout.Elements[i]; return null; }
        private static RawChannel Raw(Project project, string id) { int i; for (i = 0; i < project.Channels.Count; i++) if (project.Channels[i] != null && project.Channels[i].Id == id) return project.Channels[i]; return null; }
        private static string Key(int group, int number, int pixel) { return group.ToString(CultureInfo.InvariantCulture) + "/" + number.ToString(CultureInfo.InvariantCulture) + "/" + pixel.ToString(CultureInfo.InvariantCulture); }
        private static string Label(PreviewElement e) { return String.IsNullOrEmpty(e.Name) ? e.Id : e.Name; }
        private static LayoutPoint Copy(LayoutPoint p) { return p == null ? new LayoutPoint() : new LayoutPoint { X = p.X, Y = p.Y }; }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static string Invariant(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static double Distance(LayoutPoint a, LayoutPoint b) { double x = b.X - a.X, y = b.Y - a.Y; return Math.Sqrt(x * x + y * y); }
        private static LayoutPoint AtDistance(List<LayoutPoint> path, double wanted) { double soFar = 0; int i; for (i = 1; i < path.Count; i++) { double length = Distance(path[i - 1], path[i]); if (soFar + length >= wanted) { double f = length == 0 ? 0 : (wanted - soFar) / length; return new LayoutPoint { X = path[i - 1].X + ((path[i].X - path[i - 1].X) * f), Y = path[i - 1].Y + ((path[i].Y - path[i - 1].Y) * f) }; } soFar += length; } return Copy(path[path.Count - 1]); }
        private static string BackgroundIdentity(string path) { if (String.IsNullOrWhiteSpace(path)) return ""; try { FileInfo f = new FileInfo(path); return f.FullName + "|" + (f.Exists ? f.Length.ToString(CultureInfo.InvariantCulture) + "|" + f.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "missing"); } catch (Exception) { return "invalid:" + path; } }
    }
}
