using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace HlsRecreation
{
    public class EffectLibrary { public int Version = 1; public string Name = ""; public List<LibraryItem> Items = new List<LibraryItem>(); }
    public class LibraryItem { public string Id = ""; public string Name = ""; public string Kind = ""; public CustomDefinition Definition; public EffectGroup Group; }
    public class EffectGroup { public int FrameMs; public int DurationMs; public List<LibraryRow> Rows = new List<LibraryRow>(); public List<Effect> Effects = new List<Effect>(); }
    public class LibraryRow { public string Id = ""; public string Name = ""; public string Kind = ""; public int Width; public int Height; }

    public static class EffectLibraries
    {
        private const int MaxItems = 1024, MaxRows = 32768, MaxEffects = 100000;
        public static EffectLibrary Empty(string name) { if (String.IsNullOrWhiteSpace(name) || name.Length > 256) throw new ArgumentException("Library name is required and limited to 256 characters.", "name"); return new EffectLibrary { Name = name }; }
        public static EffectLibrary Clone(EffectLibrary source) { if (source == null) throw new ArgumentNullException("source"); EffectLibrary copy = new EffectLibrary { Version = source.Version, Name = source.Name }; if (source.Items == null) { copy.Items = null; return copy; } int i; for (i = 0; i < source.Items.Count; i++) copy.Items.Add(Item(source.Items[i])); return copy; }
        public static List<string> Validate(EffectLibrary library)
        {
            List<string> errors = new List<string>(); if (library == null) { errors.Add("Effect library is required."); return errors; } if (library.Version != 1) errors.Add("Only effect library version 1 is supported."); if (String.IsNullOrWhiteSpace(library.Name) || library.Name.Length > 256) errors.Add("Library name is required and limited to 256 characters."); if (library.Items == null) { errors.Add("Library items are required."); return errors; } if (library.Items.Count > MaxItems) { errors.Add("Too many library items."); return errors; }
            Dictionary<string, bool> ids = new Dictionary<string, bool>(StringComparer.Ordinal), names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); int i; for (i = 0; i < library.Items.Count; i++) { LibraryItem x = library.Items[i]; if (x == null) { errors.Add("Library item " + i + " is null."); continue; } Identity(errors, ids, names, x.Id, x.Name); ItemErrors(errors, x, "Library item " + Label(x.Id) + ": "); } return errors;
        }
        public static void RequireValid(EffectLibrary library) { List<string> errors = Validate(library); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()), "library"); }
        public static EffectLibrary Load(string path)
        {
            string finalPath = LibraryPath(path); FileInfo info = new FileInfo(finalPath); if (!info.Exists) throw new FileNotFoundException("Library file was not found.", finalPath); if (info.Length < 1) throw new InvalidDataException("Library file is empty.");
            XmlReaderSettings settings = new XmlReaderSettings(); settings.DtdProcessing = DtdProcessing.Prohibit; settings.XmlResolver = null; settings.IgnoreWhitespace = true; settings.MaxCharactersInDocument = 0; EffectLibrary value;
            using (FileStream stream = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read)) { SequenceXml.Preflight(stream, "EffectLibrary"); using (XmlReader reader = XmlReader.Create(stream, settings)) value = new XmlSerializer(typeof(EffectLibrary)).Deserialize(reader) as EffectLibrary; }
            RequireValid(value); return Clone(value);
        }
        public static void Save(EffectLibrary library, string path)
        {
            RequireValid(library); string finalPath = LibraryPath(path), directory = Path.GetDirectoryName(finalPath); if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The library directory does not exist."); string temporary = finalPath + "." + Model.NewId() + ".tmp";
            try { XmlWriterSettings settings = new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.Entitize }; using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) using (XmlWriter writer = XmlWriter.Create(stream, settings)) new XmlSerializer(typeof(EffectLibrary)).Serialize(writer, Clone(library)); using (FileStream verify = File.OpenRead(temporary)) SequenceXml.Preflight(verify, "EffectLibrary"); if (File.Exists(finalPath)) File.Replace(temporary, finalPath, finalPath + ".bak"); else File.Move(temporary, finalPath); }
            catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        }
        public static LibraryItem Capture(Project project, Selection selection, string name)
        {
            Model.RequireValid(project); CaptureSelection(project, selection); if (String.IsNullOrWhiteSpace(name) || name.Length > 256) throw new ArgumentException("Group name is required and limited to 256 characters.", "name"); EffectGroup group = new EffectGroup { FrameMs = project.FrameMs, DurationMs = selection.DurationMs }; Dictionary<string, bool> rows = new Dictionary<string, bool>(StringComparer.Ordinal); int i;
            for (i = 0; i < selection.Rows.Count; i++) { group.Rows.Add(Row(project, selection.Rows[i])); rows.Add(selection.Rows[i], true); }
            for (i = 0; i < project.Effects.Count; i++) { Effect e = project.Effects[i]; if (!rows.ContainsKey(e.TargetId) || e.StartMs >= selection.StartMs + selection.DurationMs || e.StartMs + e.DurationMs <= selection.StartMs) continue; if (e.StartMs < selection.StartMs || e.StartMs + e.DurationMs > selection.StartMs + selection.DurationMs) throw new ArgumentException("Selection clips an effect edge; expand the rectangle to include the whole effect.", "selection"); Effect copy = Copy(e); copy.StartMs -= selection.StartMs; group.Effects.Add(copy); }
            LibraryItem result = new LibraryItem { Id = Model.NewId(), Name = name, Kind = "Group", Group = group }; List<string> errors = new List<string>(); ItemErrors(errors, result, ""); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray())); return result;
        }
        public static LibraryItem FromCustom(CustomDefinition definition, string name)
        {
            List<string> errors = CustomEffects.Validate(definition); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()), "definition"); if (String.IsNullOrWhiteSpace(name) || name.Length > 256) throw new ArgumentException("Custom item name is required and limited to 256 characters.", "name"); return new LibraryItem { Id = Model.NewId(), Name = name, Kind = "Custom", Definition = CustomEffects.Clone(definition) };
        }
        public static Project Insert(Project project, LibraryItem item, IList<string> visibleRows, int anchorRow, int anchorMs)
        {
            Model.RequireValid(project); if (visibleRows == null) throw new ArgumentNullException("visibleRows"); List<string> errors = new List<string>(); ItemErrors(errors, item, ""); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()), "item"); if (item.Kind != "Group") throw new ArgumentException("Only effect groups can be inserted into a sequence.", "item"); if (item.Group.FrameMs != project.FrameMs) throw new ArgumentException("Library and destination time resolution differ. Set the sequence to the library frame interval before insertion.", "item"); if (anchorRow < 0 || (long)anchorRow + item.Group.Rows.Count > visibleRows.Count) throw new ArgumentException("The library group does not fit below the selected upper-left row.", "anchorRow"); if (anchorMs < 0 || anchorMs % project.FrameMs != 0 || (long)anchorMs + item.Group.DurationMs > project.DurationMs) throw new ArgumentException("The library group does not fit at the selected timeline position.", "anchorMs"); if (project.Effects.Count + item.Group.Effects.Count > MaxEffects) throw new ArgumentException("Insert exceeds the effect safety limit.", "item");
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal); int i; for (i = 0; i < item.Group.Rows.Count; i++) { string target = visibleRows[anchorRow + i]; if (!Compatible(project, item.Group.Rows[i], target)) throw new ArgumentException("Target row " + (i + 1) + " is incompatible with the stored library row.", "visibleRows"); map.Add(item.Group.Rows[i].Id, target); }
            Project candidate = Model.Clone(project); for (i = 0; i < item.Group.Effects.Count; i++) { Effect copy = Copy(item.Group.Effects[i]); copy.Id = Model.NewId(); copy.TargetId = map[item.Group.Effects[i].TargetId]; copy.StartMs += anchorMs; candidate.Effects.Add(copy); } Model.RequireValid(candidate); return candidate;
        }
        private static void CaptureSelection(Project p, Selection s)
        {
            if (s == null || s.Rows == null || s.Rows.Count == 0 || s.Rows.Count > MaxRows || s.StartMs < 0 || s.DurationMs < p.FrameMs || s.StartMs % p.FrameMs != 0 || s.DurationMs % p.FrameMs != 0 || (long)s.StartMs + s.DurationMs > p.DurationMs) throw new ArgumentException("Selection must be a non-empty frame-aligned project rectangle.", "selection"); Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal); int i; for (i = 0; i < s.Rows.Count; i++) { string kind = Model.TargetKind(p, s.Rows[i]); if (String.IsNullOrEmpty(s.Rows[i]) || seen.ContainsKey(s.Rows[i]) || String.IsNullOrEmpty(kind) || kind == "DisplayGroup") throw new ArgumentException("Selection rows must be distinct timeline targets.", "selection"); seen.Add(s.Rows[i], true); }
        }
        private static LibraryRow Row(Project p, string id)
        {
            string kind = Model.TargetKind(p, id); LibraryRow result = new LibraryRow { Id = id, Name = Model.TargetName(p, id), Kind = kind }; if (kind == "PixelPlane") { int i; for (i = 0; i < p.PixelPlanes.Count; i++) if (p.PixelPlanes[i].Id == id) { result.Width = p.PixelPlanes[i].Width; result.Height = p.PixelPlanes[i].Height; break; } } return result;
        }
        private static bool Compatible(Project p, LibraryRow source, string target)
        {
            string kind = Model.TargetKind(p, target); if (source.Kind == "PixelPlane") { if (kind != "PixelPlane") return false; int i; for (i = 0; i < p.PixelPlanes.Count; i++) if (p.PixelPlanes[i].Id == target) return p.PixelPlanes[i].Width == source.Width && p.PixelPlanes[i].Height == source.Height; return false; } return (source.Kind == "Dimmer" || source.Kind == "RGB" || source.Kind == "Segment") && (kind == "Dimmer" || kind == "RGB" || kind == "Segment");
        }
        private static LibraryItem Item(LibraryItem x) { return x == null ? null : new LibraryItem { Id = x.Id, Name = x.Name, Kind = x.Kind, Definition = x.Definition == null ? null : CustomEffects.Clone(x.Definition), Group = x.Group == null ? null : Group(x.Group) }; }
        private static EffectGroup Group(EffectGroup x)
        {
            EffectGroup result = new EffectGroup { FrameMs = x.FrameMs, DurationMs = x.DurationMs }; if (x.Rows == null) result.Rows = null; else { int i; for (i = 0; i < x.Rows.Count; i++) { LibraryRow row = x.Rows[i]; result.Rows.Add(row == null ? null : new LibraryRow { Id = row.Id, Name = row.Name, Kind = row.Kind, Width = row.Width, Height = row.Height }); } } if (x.Effects == null) result.Effects = null; else { int i; for (i = 0; i < x.Effects.Count; i++) result.Effects.Add(x.Effects[i] == null ? null : Copy(x.Effects[i])); } return result;
        }
        private static Effect Copy(Effect x)
        {
            Effect result = new Effect { Id = x.Id, TargetId = x.TargetId, Kind = x.Kind, ColorHtml = x.ColorHtml, StartMs = x.StartMs, DurationMs = x.DurationMs, Level = x.Level, UseEndpoints = x.UseEndpoints, StartLevel = x.StartLevel, EndLevel = x.EndLevel, SpeedDivisor = x.SpeedDivisor, FadeInMs = x.FadeInMs, FadeOutMs = x.FadeOutMs }; if (x.Curve == null) result.Curve = null; else { int i; for (i = 0; i < x.Curve.Count; i++) { CurvePoint point = x.Curve[i]; result.Curve.Add(point == null ? null : new CurvePoint { Position = point.Position, Value = point.Value }); } } if (x.Frames == null) result.Frames = null; else { int i; for (i = 0; i < x.Frames.Count; i++) { PlaneFrame frame = x.Frames[i]; result.Frames.Add(frame == null ? null : new PlaneFrame { Position = frame.Position, Colors = frame.Colors == null ? null : new List<string>(frame.Colors) }); } } return result;
        }
        private static void ItemErrors(List<string> errors, LibraryItem item, string prefix)
        {
            if (item == null) { errors.Add(prefix + "item is required."); return; }
            if (String.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 128 || String.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 256) { errors.Add(prefix + "item identity is invalid."); return; }
            if (item.Kind != "Custom" && item.Kind != "Group") { errors.Add(prefix + "Kind must be Custom or Group."); return; }
            if (item.Kind == "Custom") { if (item.Definition == null || item.Group != null) { errors.Add(prefix + "must contain exactly a custom definition."); return; } Add(errors, CustomEffects.Validate(item.Definition), prefix); return; }
            if (item.Group == null || item.Definition != null) { errors.Add(prefix + "must contain exactly an effect group."); return; } GroupErrors(errors, item.Group, prefix);
        }
        private static void GroupErrors(List<string> errors, EffectGroup group, string prefix)
        {
            if (group.FrameMs != 10 && group.FrameMs != 20 && group.FrameMs != 25 && group.FrameMs != 50 && group.FrameMs != 100) errors.Add(prefix + "FrameMs is invalid."); if (group.DurationMs < 1 || group.FrameMs < 1 || group.DurationMs % group.FrameMs != 0 || group.DurationMs > 600000) errors.Add(prefix + "DurationMs is invalid."); if (group.Rows == null || group.Effects == null) { errors.Add(prefix + "rows and effects are required."); return; } if (group.Rows.Count == 0 || group.Rows.Count > MaxRows || group.Effects.Count > MaxEffects) errors.Add(prefix + "group collection limits are invalid.");
            Dictionary<string, LibraryRow> rows = new Dictionary<string, LibraryRow>(StringComparer.Ordinal); int i; for (i = 0; i < group.Rows.Count; i++) { LibraryRow row = group.Rows[i]; if (row == null || String.IsNullOrWhiteSpace(row.Id) || row.Id.Length > 128 || String.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 256 || (row.Kind != "Dimmer" && row.Kind != "RGB" && row.Kind != "Segment" && row.Kind != "PixelPlane") || rows.ContainsKey(row.Id)) { errors.Add(prefix + "has an invalid or duplicate row."); continue; } if (row.Kind == "PixelPlane") { if (row.Width < 1 || row.Width > 32 || row.Height < 1 || row.Height > 32) errors.Add(prefix + "has invalid PixelPlane row dimensions."); } else if (row.Width != 0 || row.Height != 0) errors.Add(prefix + "non-PixelPlane rows cannot have dimensions."); rows.Add(row.Id, row); }
            Dictionary<string, bool> ids = new Dictionary<string, bool>(StringComparer.Ordinal); for (i = 0; i < group.Effects.Count; i++) EffectErrors(errors, group.Effects[i], rows, ids, group, prefix);
        }
        private static void EffectErrors(List<string> errors, Effect effect, Dictionary<string, LibraryRow> rows, Dictionary<string, bool> ids, EffectGroup group, string prefix)
        {
            if (effect == null || String.IsNullOrWhiteSpace(effect.Id) || effect.Id.Length > 128 || ids.ContainsKey(effect.Id)) { errors.Add(prefix + "has an invalid or duplicate effect."); return; } ids.Add(effect.Id, true); LibraryRow row;
            if (String.IsNullOrWhiteSpace(effect.TargetId) || !rows.TryGetValue(effect.TargetId, out row) || effect.StartMs < 0 || effect.DurationMs < 1 || (long)effect.StartMs + effect.DurationMs > group.DurationMs || effect.DurationMs < group.FrameMs || (effect.Kind != "Level" && effect.DurationMs < group.FrameMs * 2) || !Unit(effect.Level) || !Unit(effect.StartLevel) || !Unit(effect.EndLevel) || (effect.SpeedDivisor != 1 && effect.SpeedDivisor != 2 && effect.SpeedDivisor != 3 && effect.SpeedDivisor != 4) || effect.FadeInMs < 0 || effect.FadeOutMs < 0 || effect.FadeInMs > effect.DurationMs || effect.FadeOutMs > effect.DurationMs) { errors.Add(prefix + "has an out-of-range effect."); return; }
            bool plane = row.Kind == "PixelPlane"; if (plane != (effect.Kind == "PixelPlane") || (!plane && effect.Kind != "Level" && effect.Kind != "RampUp" && effect.Kind != "RampDown" && effect.Kind != "Custom")) { errors.Add(prefix + "has an effect with an incompatible row kind."); return; }
            if (!plane) { try { Model.ParseColor(effect.ColorHtml); } catch (ArgumentException) { errors.Add(prefix + "has an invalid effect color."); } if (effect.SpeedDivisor != 1 || effect.FadeInMs != 0 || effect.FadeOutMs != 0 || effect.Frames == null || effect.Frames.Count != 0) errors.Add(prefix + "has invalid basic-only fields."); if (effect.UseEndpoints && effect.Kind != "RampUp" && effect.Kind != "RampDown") errors.Add(prefix + "uses endpoints on a non-ramp effect."); CurveErrors(errors, effect.Curve, effect.Kind == "Custom", prefix); }
            else { if (effect.UseEndpoints || effect.Curve == null || effect.Curve.Count != 0 || effect.Frames == null || effect.Frames.Count == 0 || effect.Frames.Count > 256) errors.Add(prefix + "has invalid PixelPlane data."); else { double prior = -1; int i, j; for (i = 0; i < effect.Frames.Count; i++) { PlaneFrame f = effect.Frames[i]; if (f == null || !Unit(f.Position) || f.Position <= prior || f.Colors == null || f.Colors.Count != row.Width * row.Height) { errors.Add(prefix + "has an invalid PixelPlane frame."); break; } prior = f.Position; for (j = 0; j < f.Colors.Count; j++) try { Model.ParseColor(f.Colors[j]); } catch (ArgumentException) { errors.Add(prefix + "has an invalid PixelPlane color."); break; } } } }
        }
        private static void CurveErrors(List<string> errors, List<CurvePoint> curve, bool custom, string prefix)
        {
            if (curve == null || (!custom && curve.Count != 0) || (custom && (curve.Count == 0 || curve.Count > 64))) { errors.Add(prefix + "has an invalid basic curve."); return; } double prior = -1; int i; for (i = 0; i < curve.Count; i++) { CurvePoint point = curve[i]; if (point == null || !Unit(point.Position) || !Unit(point.Value) || point.Position <= prior) { errors.Add(prefix + "has invalid curve points."); return; } prior = point.Position; }
        }
        private static string LibraryPath(string path) { if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Library path is required.", "path"); string result = Path.GetFullPath(path); if (result.StartsWith("\\\\", StringComparison.Ordinal) || !String.Equals(Path.GetExtension(result), ".hlsrlib", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a local .hlsrlib file.", "path"); return result; }
        private static void Identity(List<string> errors, Dictionary<string, bool> ids, Dictionary<string, bool> names, string id, string name) { if (String.IsNullOrWhiteSpace(id) || id.Length > 128) errors.Add("A library item id is required and limited to 128 characters."); else if (ids.ContainsKey(id)) errors.Add("Duplicate library item id."); else ids.Add(id, true); if (String.IsNullOrWhiteSpace(name) || name.Length > 256) errors.Add("A library item name is required and limited to 256 characters."); else if (names.ContainsKey(name)) errors.Add("Duplicate library item name."); else names.Add(name, true); }
        private static void Add(List<string> into, List<string> values, string prefix) { int i; for (i = 0; i < values.Count; i++) into.Add(prefix + values[i]); }
        private static bool Unit(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x) && x >= 0 && x <= 1; }
        private static string Label(string x) { return String.IsNullOrEmpty(x) ? "(unnamed)" : x; }
    }
}
