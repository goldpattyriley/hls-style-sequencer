using System;
using System.Collections.Generic;
using System.Drawing;

namespace HlsRecreation
{
    public class CustomDefinition { public string Id = ""; public string Name = ""; public List<CustomPart> Parts = new List<CustomPart>(); }
    public class CustomPart { public string Kind = "Level"; public int LengthCells = 2; public double StartLevel = 1; public double EndLevel = 1; public string ColorHtml = "#FFFFFF"; }
    public class CustomPlacement { public string Mode = "Chase"; public int RepeatCount = 1; public int PeriodCells; public int StaggerCells = 1; public int Seed = 1; public int CoveragePercent = 100; public bool RandomizePartOrder; public bool RandomIntensity; public int MinIntensityPercent; public int MaxIntensityPercent = 100; public string ColorMode = "Selected"; public string SelectedColor = "#FFFFFF"; }
    public class Selection { public List<string> Rows = new List<string>(); public int StartMs; public int DurationMs; }

    public static class CustomEffects
    {
        private const int MaxDefinitions = 1024, MaxParts = 256, MaxCells = 12000, MaxEffects = 100000;
        public static List<CustomDefinition> CloneDefinitions(List<CustomDefinition> values)
        {
            if (values == null) return null; List<CustomDefinition> result = new List<CustomDefinition>();
            int i; for (i = 0; i < values.Count; i++) result.Add(values[i] == null ? null : Clone(values[i])); return result;
        }
        public static CustomDefinition Clone(CustomDefinition value)
        {
            if (value == null) throw new ArgumentNullException("value"); CustomDefinition result = new CustomDefinition { Id = value.Id, Name = value.Name };
            if (value.Parts == null) { result.Parts = null; return result; }
            int i; for (i = 0; i < value.Parts.Count; i++) { CustomPart p = value.Parts[i]; result.Parts.Add(p == null ? null : new CustomPart { Kind = p.Kind, LengthCells = p.LengthCells, StartLevel = p.StartLevel, EndLevel = p.EndLevel, ColorHtml = p.ColorHtml }); } return result;
        }
        public static List<string> ValidateDefinitions(List<CustomDefinition> values)
        {
            List<string> errors = new List<string>(); if (values == null) { errors.Add("CustomEffects list is required."); return errors; } if (values.Count > MaxDefinitions) { errors.Add("Too many custom definitions."); return errors; }
            Dictionary<string, bool> ids = new Dictionary<string, bool>(StringComparer.Ordinal), names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); int i;
            for (i = 0; i < values.Count; i++) { CustomDefinition d = values[i]; if (d == null) { errors.Add("CustomEffects[" + i + "] is null."); continue; } Identity(errors, ids, names, d.Id, d.Name); Prefix(errors, Validate(d), "Custom definition " + Text(d.Id) + ": "); } return errors;
        }
        public static List<string> Validate(CustomDefinition value)
        {
            List<string> errors = new List<string>(); if (value == null) { errors.Add("Definition is required."); return errors; }
            if (String.IsNullOrWhiteSpace(value.Id) || value.Id.Length > 128) errors.Add("Definition id is required and limited to 128 characters."); if (String.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 256) errors.Add("Definition name is required and limited to 256 characters.");
            if (value.Parts == null) { errors.Add("Definition parts are required."); return errors; } if (value.Parts.Count == 0 || value.Parts.Count > MaxParts) errors.Add("Definition must contain 1.." + MaxParts + " parts."); int total = 0, i;
            for (i = 0; i < value.Parts.Count; i++) { CustomPart p = value.Parts[i]; if (p == null) { errors.Add("Part " + i + " is null."); continue; } if (p.Kind != "Level" && p.Kind != "RampUp" && p.Kind != "RampDown") errors.Add("Part " + i + " has an invalid Kind."); if (p.LengthCells < 1 || p.LengthCells > MaxCells || total > MaxCells - p.LengthCells) errors.Add("Part " + i + " makes recipe length invalid."); else total += p.LengthCells; if (!Unit(p.StartLevel) || !Unit(p.EndLevel)) errors.Add("Part " + i + " levels must be finite and 0..1."); if (p.Kind == "Level" && p.StartLevel != p.EndLevel) errors.Add("Level part " + i + " must use equal endpoints."); try { Model.ParseColor(p.ColorHtml); } catch (ArgumentException) { errors.Add("Part " + i + " has an invalid color."); } }
            return errors;
        }
        public static int TotalCells(CustomDefinition value) { if (value == null || value.Parts == null) return 0; int total = 0, i; for (i = 0; i < value.Parts.Count; i++) { if (value.Parts[i] == null || value.Parts[i].LengthCells < 1 || total > Int32.MaxValue - value.Parts[i].LengthCells) return 0; total += value.Parts[i].LengthCells; } return total; }
        public static CustomDefinition Example()
        {
            CustomDefinition result = new CustomDefinition { Id = Model.NewId(), Name = "Handmade Level and Ramp" };
            result.Parts.Add(new CustomPart { Kind = "Level", LengthCells = 5, StartLevel = .35, EndLevel = .35, ColorHtml = "#FFFFFF" }); result.Parts.Add(new CustomPart { Kind = "RampDown", LengthCells = 12, StartLevel = 1, EndLevel = 0, ColorHtml = "#FFFFFF" }); return result;
        }
        public static Project Place(Project project, CustomDefinition definition, Selection selection, CustomPlacement placement)
        {
            Model.RequireValid(project); Require(Validate(definition)); CheckSelection(project, selection); CheckPlacement(placement); int width = TotalCells(definition);
            if (width > selection.DurationMs / project.FrameMs) throw new ArgumentException("The complete recipe does not fit the selected time region.", "selection"); int i; for (i = 0; i < definition.Parts.Count; i++) if (definition.Parts[i].Kind != "Level" && definition.Parts[i].LengthCells < 2) throw new ArgumentException("Ramp parts need at least two timeline cells.", "definition");
            Project candidate = Model.Clone(project); Random random = new Random(placement.Seed); List<Stamp> stamps = Stamps(project, selection, placement, width, random, definition.Parts.Count, candidate.Effects.Count);
            for (i = 0; i < stamps.Count; i++) AddStamp(candidate, definition, stamps[i], placement, random); Model.RequireValid(candidate); return candidate;
        }
        private class Stamp { public string Target; public int Start; }
        private static List<Stamp> Stamps(Project project, Selection selection, CustomPlacement placement, int width, Random random, int parts, int existing)
        {
            List<Stamp> result = new List<Stamp>(); if (placement.CoveragePercent == 0) return result; int cells = selection.DurationMs / project.FrameMs, period = placement.PeriodCells == 0 ? width : placement.PeriodCells, row, repeat; long possible = 0;
            for (row = 0; row < selection.Rows.Count; row++)
            {
                if (placement.Mode == "Chase") { for (repeat = 0; repeat < placement.RepeatCount && row * placement.StaggerCells + repeat * period + width <= cells; repeat++) possible++; }
                else { int step = placement.PeriodCells > 0 ? placement.PeriodCells : 1, available = 1 + ((cells - width) / step); possible += Math.Min(placement.RepeatCount, available); }
                if ((long)existing + possible * parts > MaxEffects) throw new ArgumentException("Placement exceeds the generated-effect safety limit.", "placement");
            }
            for (row = 0; row < selection.Rows.Count; row++) { List<int> starts = new List<int>(); if (placement.Mode == "Chase") { for (repeat = 0; repeat < placement.RepeatCount; repeat++) { int start = row * placement.StaggerCells + repeat * period; if (start + width > cells) break; starts.Add(start); } } else { // Primary HLS RNG is unknown: no-repeat starts drawn from the period grid and persisted as ordinary effects.
                    int step = placement.PeriodCells > 0 ? placement.PeriodCells : 1, available = 1 + ((cells - width) / step), count = Math.Min(placement.RepeatCount, available); Dictionary<int, int> picks = new Dictionary<int, int>(); for (repeat = 0; repeat < count; repeat++) { int pick = random.Next(available - repeat), last = available - repeat - 1, actual = picks.ContainsKey(pick) ? picks[pick] : pick, replacement = picks.ContainsKey(last) ? picks[last] : last; picks[pick] = replacement; starts.Add(actual * step); } }
                for (repeat = 0; repeat < starts.Count; repeat++) if (random.Next(100) < placement.CoveragePercent) result.Add(new Stamp { Target = selection.Rows[row], Start = selection.StartMs + starts[repeat] * project.FrameMs }); }
            return result;
        }
        private static void AddStamp(Project project, CustomDefinition definition, Stamp stamp, CustomPlacement placement, Random random)
        {
            List<CustomPart> parts = new List<CustomPart>(); int i; for (i = 0; i < definition.Parts.Count; i++) parts.Add(definition.Parts[i]); if (placement.RandomizePartOrder) Shuffle(parts, random); int start = stamp.Start;
            for (i = 0; i < parts.Count; i++) { CustomPart p = parts[i]; double factor = placement.RandomIntensity ? Range(random, placement.MinIntensityPercent, placement.MaxIntensityPercent) / 100.0 : 1; string color = placement.ColorMode == "Selected" ? placement.SelectedColor : placement.ColorMode == "Embedded" ? p.ColorHtml : RandomColor(random); Effect effect = new Effect { Id = Model.NewId(), TargetId = stamp.Target, Kind = p.Kind, StartMs = start, DurationMs = p.LengthCells * project.FrameMs, ColorHtml = color }; effect.Level = p.Kind == "Level" ? p.StartLevel * factor : 1; effect.UseEndpoints = p.Kind != "Level"; effect.StartLevel = p.StartLevel * factor; effect.EndLevel = p.EndLevel * factor; project.Effects.Add(effect); start += p.LengthCells * project.FrameMs; }
        }
        private static void CheckSelection(Project p, Selection s)
        {
            if (s == null || s.Rows == null || s.Rows.Count == 0 || s.Rows.Count > 32768 || s.StartMs < 0 || s.DurationMs < p.FrameMs || s.StartMs % p.FrameMs != 0 || s.DurationMs % p.FrameMs != 0 || (long)s.StartMs + s.DurationMs > p.DurationMs) throw new ArgumentException("Selection must be a non-empty frame-aligned project rectangle.", "selection");
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal); int i; for (i = 0; i < s.Rows.Count; i++) { string kind = Model.TargetKind(p, s.Rows[i]); if (String.IsNullOrEmpty(s.Rows[i]) || seen.ContainsKey(s.Rows[i]) || (kind != "Dimmer" && kind != "RGB" && kind != "Segment")) throw new ArgumentException("Custom placement supports distinct Dimmer, RGB, and Segment rows only.", "selection"); seen.Add(s.Rows[i], true); }
        }
        private static void CheckPlacement(CustomPlacement x)
        {
            if (x == null || (x.Mode != "Chase" && x.Mode != "Random") || x.RepeatCount < 1 || x.RepeatCount > 10000 || x.PeriodCells < 0 || x.PeriodCells > MaxCells || x.StaggerCells < 0 || x.StaggerCells > MaxCells || x.CoveragePercent < 0 || x.CoveragePercent > 100 || x.MinIntensityPercent < 0 || x.MaxIntensityPercent > 100 || x.MinIntensityPercent > x.MaxIntensityPercent || (x.ColorMode != "Selected" && x.ColorMode != "Embedded" && x.ColorMode != "Random")) throw new ArgumentException("Placement values are invalid.", "placement"); try { Model.ParseColor(x.SelectedColor); } catch (ArgumentException) { throw new ArgumentException("SelectedColor is invalid.", "placement"); }
        }
        private static void Shuffle<T>(List<T> x, Random r) { int i; for (i = x.Count - 1; i > 0; i--) { int j = r.Next(i + 1); T t = x[i]; x[i] = x[j]; x[j] = t; } }
        private static int Range(Random r, int min, int max) { return min == max ? min : min + r.Next(max - min + 1); }
        private static string RandomColor(Random r) { return Model.HtmlColor(Hsv(r.Next(360))); }
        private static Color Hsv(int h) { double c = 1, x = 1 - Math.Abs(((h / 60.0) % 2) - 1), a = 0, b = 0, d = 0; if (h < 60) { a = c; b = x; } else if (h < 120) { a = x; b = c; } else if (h < 180) { b = c; d = x; } else if (h < 240) { b = x; d = c; } else if (h < 300) { a = x; d = c; } else { a = c; d = x; } return Color.FromArgb((int)Math.Round(a * 255), (int)Math.Round(b * 255), (int)Math.Round(d * 255)); }
        private static bool Unit(double x) { return !Double.IsNaN(x) && !Double.IsInfinity(x) && x >= 0 && x <= 1; }
        private static void Require(List<string> x) { if (x.Count != 0) throw new ArgumentException(String.Join(" ", x.ToArray())); }
        private static void Prefix(List<string> x, List<string> add, string p) { int i; for (i = 0; i < add.Count; i++) x.Add(p + add[i]); }
        private static string Text(string x) { return String.IsNullOrEmpty(x) ? "(unnamed)" : x; }
        private static void Identity(List<string> errors, Dictionary<string, bool> ids, Dictionary<string, bool> names, string id, string name) { if (String.IsNullOrWhiteSpace(id) || id.Length > 128) errors.Add("A custom definition id is required and limited to 128 characters."); else if (ids.ContainsKey(id)) errors.Add("Duplicate custom definition id."); else ids.Add(id, true); if (String.IsNullOrWhiteSpace(name) || name.Length > 256) errors.Add("A custom definition name is required and limited to 256 characters."); else if (names.ContainsKey(name)) errors.Add("Duplicate custom definition name."); else names.Add(name, true); }
    }
}
