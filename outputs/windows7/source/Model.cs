using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace HlsRecreation
{
    public class Project
    {
        public int SchemaVersion = 4;
        public string Name = "Untitled";
        public string AudioFile = "demo.wav";
        public int DurationMs = 8000;
        public int FrameMs = 50;
        public List<RawChannel> Channels = new List<RawChannel>();
        public List<DisplayGroup> Groups = new List<DisplayGroup>();
        public List<Segment> Segments = new List<Segment>();
        public List<PixelPlane> PixelPlanes = new List<PixelPlane>();
        public List<Effect> Effects = new List<Effect>();
        public List<CustomDefinition> CustomEffects = new List<CustomDefinition>();
        public PreviewLayout Layout = new PreviewLayout();
        public OutputSettings Output = new OutputSettings();
    }

    public class RawChannel
    {
        public string Id = "";
        public string Name = "";
        public string Kind = "Dimmer";
        public string ColorHtml = "#FFFFFF";
        public int Universe;
        public int Address = 1;
        public int OutputOrder;
        public double PreviewX;
        public double PreviewY;
        public int PreviewGroup, PreviewNumber, PreviewPixel;
    }

    public class DisplayGroup
    {
        public string Id = "";
        public string Name = "";
        public List<string> Members = new List<string>();
    }

    public class Segment
    {
        public string Id = "";
        public string Name = "";
        public List<string> Members = new List<string>();
    }

    public class PixelPlane
    {
        public string Id = "";
        public string Name = "";
        public int Width = 1;
        public int Height = 1;
        // Keep constructor lists empty: XmlSerializer appends incoming list items.
        public List<string> Cells = new List<string>();
    }

    public class Effect
    {
        public string Id = "";
        public string TargetId = "";
        public string Kind = "Level";
        public string ColorHtml = "#FFFFFF";
        public int StartMs;
        public int DurationMs = 1000;
        public double Level = 1;
        public bool UseEndpoints;
        public double StartLevel;
        public double EndLevel = 1;
        public List<CurvePoint> Curve = new List<CurvePoint>();
        public List<PlaneFrame> Frames = new List<PlaneFrame>();
        public int SpeedDivisor = 1;
        public int FadeInMs;
        public int FadeOutMs;
    }

    public class CurvePoint
    {
        public double Position;
        public double Value;
    }

    public class PlaneFrame
    {
        public double Position;
        public List<string> Colors = new List<string>();
    }

    public static class Model
    {
        private static readonly int[] FrameSteps = new int[] { 10, 20, 25, 50, 100 };

        public static Project Empty()
        {
            Project project = new Project();
            project.Groups.Add(new DisplayGroup { Id = NewId(), Name = "Working Group" });
            return project;
        }

        public static Project Seed()
        {
            Project project = Empty();
            project.Name = "HLS Arches";
            project.DurationMs = 8000;
            project.FrameMs = 50;
            project.Groups[0].Name = "Arches";

            int i;
            for (i = 0; i < 12; i++)
            {
                RawChannel channel = new RawChannel();
                channel.Id = NewId();
                channel.Name = "Arch " + (i + 1).ToString("00", CultureInfo.InvariantCulture);
                channel.Kind = "RGB";
                channel.ColorHtml = "#FFFFFF";
                channel.Universe = 1;
                channel.Address = 1 + (i * 3);
                channel.OutputOrder = i + 1;
                channel.PreviewX = i;
                channel.PreviewY = 0;
                project.Channels.Add(channel);
                project.Groups[0].Members.Add(channel.Id);
            }

            DisplayGroup rawGroup = new DisplayGroup { Id = NewId(), Name = "Raw RGB" };
            for (i = 0; i < project.Channels.Count; i++) rawGroup.Members.Add(project.Channels[i].Id);
            project.Groups.Add(rawGroup);

            Segment left = new Segment { Id = NewId(), Name = "Left Arches" };
            Segment right = new Segment { Id = NewId(), Name = "Right Arches" };
            for (i = 0; i < 6; i++) left.Members.Add(project.Channels[i].Id);
            for (i = 6; i < 12; i++) right.Members.Add(project.Channels[i].Id);
            project.Segments.Add(left);
            project.Segments.Add(right);
            DisplayGroup segmentGroup = new DisplayGroup { Id = NewId(), Name = "Segments" };
            segmentGroup.Members.Add(left.Id);
            segmentGroup.Members.Add(right.Id);
            project.Groups.Add(segmentGroup);

            PixelPlane plane = new PixelPlane { Id = NewId(), Name = "Arch Matrix", Width = 6, Height = 2 };
            plane.Cells.Clear();
            for (i = 0; i < 12; i++) plane.Cells.Add(project.Channels[i].Id);
            project.PixelPlanes.Add(plane);
            project.Groups[0].Members.Add(left.Id);
            project.Groups[0].Members.Add(right.Id);
            project.Groups[0].Members.Add(plane.Id);
            DisplayGroup planeGroup = new DisplayGroup { Id = NewId(), Name = "PixelPlanes" };
            planeGroup.Members.Add(plane.Id);
            project.Groups.Add(planeGroup);

            Effect rise = NewEffect(project, left.Id, "RampUp", 0, 2000, "#E62E2E");
            rise.Level = 0.9;
            Effect fall = NewEffect(project, right.Id, "RampDown", 2000, 2000, "#245BFF");
            fall.Level = 0.8;
            Effect hand = NewEffect(project, project.Channels[0].Id, "Custom", 4000, 2000, "#FFFFFF");
            hand.Curve.Clear();
            hand.Curve.Add(new CurvePoint { Position = 0, Value = 0 });
            hand.Curve.Add(new CurvePoint { Position = .25, Value = 1 });
            hand.Curve.Add(new CurvePoint { Position = .6, Value = .35 });
            hand.Curve.Add(new CurvePoint { Position = 1, Value = 1 });
            Effect clip = NewEffect(project, plane.Id, "PixelPlane", 5000, 3000, "#000000");
            clip.SpeedDivisor = 1;
            clip.FadeInMs = 250;
            clip.FadeOutMs = 350;
            clip.Frames.Clear();
            clip.Frames.Add(new PlaneFrame { Position = 0, Colors = PlaneColors(plane.Width * plane.Height, "#000000") });
            List<string> diagonal = PlaneColors(plane.Width * plane.Height, "#000000");
            diagonal[0] = "#00FF66";
            diagonal[7] = "#00FF66";
            clip.Frames.Add(new PlaneFrame { Position = .35, Colors = diagonal });
            List<string> opposite = PlaneColors(plane.Width * plane.Height, "#000000");
            opposite[5] = "#A020FF";
            opposite[6] = "#A020FF";
            clip.Frames.Add(new PlaneFrame { Position = .7, Colors = opposite });
            clip.Frames.Add(new PlaneFrame { Position = 1, Colors = PlaneColors(plane.Width * plane.Height, "#000000") });
            project.Effects.Add(rise);
            project.Effects.Add(fall);
            project.Effects.Add(hand);
            project.Effects.Add(clip);
            RequireValid(project);
            return project;
        }

        public static Project Clone(Project source)
        {
            if (source == null) throw new ArgumentNullException("source");
            Project copy = new Project();
            copy.SchemaVersion = source.SchemaVersion;
            copy.Name = source.Name;
            copy.AudioFile = source.AudioFile;
            copy.DurationMs = source.DurationMs;
            copy.FrameMs = source.FrameMs;
            copy.CustomEffects = HlsRecreation.CustomEffects.CloneDefinitions(source.CustomEffects);
            copy.Layout = PreviewLayouts.Clone(source.Layout);
            copy.Output = ControllerOutput.Clone(source.Output);
            int i;
            for (i = 0; i < SafeCount(source.Channels); i++)
            {
                RawChannel x = source.Channels[i];
                if (x == null) { copy.Channels.Add(null); continue; }
                copy.Channels.Add(new RawChannel { Id = x.Id, Name = x.Name, Kind = x.Kind, ColorHtml = x.ColorHtml, Universe = x.Universe, Address = x.Address, OutputOrder = x.OutputOrder, PreviewX = x.PreviewX, PreviewY = x.PreviewY, PreviewGroup = x.PreviewGroup, PreviewNumber = x.PreviewNumber, PreviewPixel = x.PreviewPixel });
            }
            for (i = 0; i < SafeCount(source.Groups); i++)
            {
                DisplayGroup x = source.Groups[i];
                if (x == null) { copy.Groups.Add(null); continue; }
                copy.Groups.Add(new DisplayGroup { Id = x.Id, Name = x.Name, Members = CopyStrings(x.Members) });
            }
            for (i = 0; i < SafeCount(source.Segments); i++)
            {
                Segment x = source.Segments[i];
                if (x == null) { copy.Segments.Add(null); continue; }
                copy.Segments.Add(new Segment { Id = x.Id, Name = x.Name, Members = CopyStrings(x.Members) });
            }
            for (i = 0; i < SafeCount(source.PixelPlanes); i++)
            {
                PixelPlane x = source.PixelPlanes[i];
                if (x == null) { copy.PixelPlanes.Add(null); continue; }
                copy.PixelPlanes.Add(new PixelPlane { Id = x.Id, Name = x.Name, Width = x.Width, Height = x.Height, Cells = CopyStrings(x.Cells) });
            }
            for (i = 0; i < SafeCount(source.Effects); i++)
            {
                Effect x = source.Effects[i];
                if (x == null) { copy.Effects.Add(null); continue; }
                Effect effect = new Effect { Id = x.Id, TargetId = x.TargetId, Kind = x.Kind, ColorHtml = x.ColorHtml, StartMs = x.StartMs, DurationMs = x.DurationMs, Level = x.Level, UseEndpoints = x.UseEndpoints, StartLevel = x.StartLevel, EndLevel = x.EndLevel, SpeedDivisor = x.SpeedDivisor, FadeInMs = x.FadeInMs, FadeOutMs = x.FadeOutMs };
                int j;
                for (j = 0; j < SafeCount(x.Curve); j++)
                {
                    CurvePoint point = x.Curve[j];
                    effect.Curve.Add(point == null ? null : new CurvePoint { Position = point.Position, Value = point.Value });
                }
                for (j = 0; j < SafeCount(x.Frames); j++)
                {
                    PlaneFrame frame = x.Frames[j];
                    effect.Frames.Add(frame == null ? null : new PlaneFrame { Position = frame.Position, Colors = CopyStrings(frame.Colors) });
                }
                copy.Effects.Add(effect);
            }
            return copy;
        }

        public static string NewId() { return Guid.NewGuid().ToString("N"); }

        public static List<string> Validate(Project project)
        {
            List<string> errors = new List<string>();
            if (project == null) { errors.Add("Project is required."); return errors; }
            if (project.SchemaVersion != 4) errors.Add("SchemaVersion must be 4 (older schema-2/3 files are upgraded by Open).");
            if (String.IsNullOrWhiteSpace(project.Name) || project.Name.Length > 256) errors.Add("Project name is required and limited to 256 characters.");
            if (project.AudioFile == null || project.AudioFile.Length > 4096 || project.AudioFile.StartsWith("\\\\", StringComparison.Ordinal) || project.AudioFile.IndexOf("://", StringComparison.Ordinal) >= 0) errors.Add("Audio must be a local WAV path.");
            if (project.DurationMs < 1 || project.DurationMs > 600000) errors.Add("DurationMs must be 1..600000.");
            if (!IsFrameStep(project.FrameMs)) errors.Add("FrameMs must be 10, 20, 25, 50, or 100.");
            else if (project.DurationMs > 0 && project.DurationMs % project.FrameMs != 0) errors.Add("DurationMs must align to FrameMs.");
            if (project.Channels == null) errors.Add("Channels list is required.");
            if (project.Groups == null) errors.Add("Groups list is required.");
            if (project.Segments == null) errors.Add("Segments list is required.");
            if (project.PixelPlanes == null) errors.Add("PixelPlanes list is required.");
            if (project.Effects == null) errors.Add("Effects list is required.");
            if (project.CustomEffects == null) errors.Add("Custom effects list is required.");
            if (project.Layout == null) errors.Add("Preview layout is required.");
            if (project.Output == null) errors.Add("Output settings are required.");
            if (errors.Count != 0) return errors;
            errors.AddRange(HlsRecreation.CustomEffects.ValidateDefinitions(project.CustomEffects));
            errors.AddRange(PreviewLayouts.Validate(project));
            errors.AddRange(ControllerOutput.Validate(project.Output));
            if (project.Channels.Count > 32768 || project.Groups.Count > 1024 || project.Segments.Count > 4096 || project.PixelPlanes.Count > 256 || project.Effects.Count > 100000)
            { errors.Add("Project exceeds the native milestone collection limits."); return errors; }

            Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, RawChannel> raws = new Dictionary<string, RawChannel>(StringComparer.Ordinal);
            Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<int> outputOrders = new HashSet<int>();
            int i;
            for (i = 0; i < project.Channels.Count; i++)
            {
                RawChannel channel = project.Channels[i];
                if (channel == null) { errors.Add("Channels[" + i + "] is null."); continue; }
                AddIdentity(errors, ids, names, channel.Id, channel.Name, "channel");
                if (!IsRawKind(channel.Kind)) errors.Add("Channel " + Text(channel.Id) + " has invalid Kind.");
                if (!IsFinite(channel.PreviewX) || !IsFinite(channel.PreviewY)) errors.Add("Channel " + Text(channel.Id) + " has non-finite preview coordinates.");
                if (channel.PreviewGroup < 0 || channel.PreviewNumber < 0 || channel.PreviewPixel < 0 || channel.PreviewGroup > 65535 || channel.PreviewNumber > 65535 || channel.PreviewPixel > 32768 || ((channel.PreviewGroup == 0 || channel.PreviewNumber == 0) && (channel.PreviewGroup != 0 || channel.PreviewNumber != 0 || channel.PreviewPixel != 0))) errors.Add("Preview assignments must be zero/unassigned or positive group, number and sub-index values.");
                ValidateColor(errors, channel.ColorHtml, "Channel " + Text(channel.Id));
                if (channel.OutputOrder < 1 || !outputOrders.Add(channel.OutputOrder)) errors.Add("Raw channel output orders must be positive and unique.");
                if (channel.Universe < 0 || channel.Universe > 64) errors.Add("Channel " + Text(channel.Id) + " has invalid Universe.");
                if (channel.Address < 1 || channel.Address > 512) errors.Add("Channel " + Text(channel.Id) + " has invalid Address.");
                if (channel.Kind == "RGB" && channel.Address > 510) errors.Add("RGB channel " + Text(channel.Id) + " crosses a universe boundary.");
                if (!String.IsNullOrEmpty(channel.Id)) { raws[channel.Id] = channel; kinds[channel.Id] = "RawChannel"; }
            }
            names.Clear();
            for (i = 0; i < project.Groups.Count; i++)
            {
                DisplayGroup group = project.Groups[i];
                if (group == null) { errors.Add("Groups[" + i + "] is null."); continue; }
                AddIdentity(errors, ids, names, group.Id, group.Name, "group");
                if (group.Members == null) errors.Add("Group " + Text(group.Id) + " Members is required.");
                else if (group.Members.Count > 32768) errors.Add("A group may contain at most 32768 rows.");
            }
            names.Clear();
            for (i = 0; i < project.Segments.Count; i++)
            {
                Segment segment = project.Segments[i];
                if (segment == null) { errors.Add("Segments[" + i + "] is null."); continue; }
                AddIdentity(errors, ids, names, segment.Id, segment.Name, "segment");
                if (segment.Members == null) errors.Add("Segment " + Text(segment.Id) + " Members is required.");
                else if (segment.Members.Count > 32768) errors.Add("A Segment may contain at most 32768 raw channels.");
                else if (!String.IsNullOrEmpty(segment.Id)) kinds[segment.Id] = "Segment";
            }
            names.Clear();
            for (i = 0; i < project.PixelPlanes.Count; i++)
            {
                PixelPlane plane = project.PixelPlanes[i];
                if (plane == null) { errors.Add("PixelPlanes[" + i + "] is null."); continue; }
                AddIdentity(errors, ids, names, plane.Id, plane.Name, "pixel plane");
                if (plane.Width < 1 || plane.Width > 32 || plane.Height < 1 || plane.Height > 32) errors.Add("PixelPlane " + Text(plane.Id) + " dimensions must be 1..32.");
                if (plane.Cells == null) errors.Add("PixelPlane " + Text(plane.Id) + " Cells is required.");
                else if (plane.Width >= 1 && plane.Height >= 1 && plane.Cells.Count != plane.Width * plane.Height) errors.Add("PixelPlane " + Text(plane.Id) + " cell count does not match dimensions.");
                if (!String.IsNullOrEmpty(plane.Id)) kinds[plane.Id] = "PixelPlane";
            }
            ValidatePhysicalOverlap(project, errors);
            ValidateReferences(project, errors, raws, kinds);
            ValidateEffects(project, errors, ids, kinds);
            return errors;
        }

        public static void RequireValid(Project project)
        {
            List<string> errors = Validate(project);
            if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray()), "project");
        }

        public static List<string> TargetIds(Project project)
        {
            RequireValid(project);
            List<string> result = new List<string>();
            int i;
            for (i = 0; i < project.Channels.Count; i++) result.Add(project.Channels[i].Id);
            for (i = 0; i < project.Segments.Count; i++) result.Add(project.Segments[i].Id);
            for (i = 0; i < project.PixelPlanes.Count; i++) result.Add(project.PixelPlanes[i].Id);
            return result;
        }

        public static string TargetName(Project project, string id)
        {
            if (project == null || String.IsNullOrEmpty(id)) return "";
            RawChannel raw = FindRaw(project, id); if (raw != null) return raw.Name;
            DisplayGroup group = FindGroup(project, id); if (group != null) return group.Name;
            Segment segment = FindSegment(project, id); if (segment != null) return segment.Name;
            PixelPlane plane = FindPlane(project, id); return plane == null ? "" : plane.Name;
        }

        public static string TargetKind(Project project, string id)
        {
            if (project == null || String.IsNullOrEmpty(id)) return "";
            RawChannel raw = FindRaw(project, id); if (raw != null) return raw.Kind;
            if (FindGroup(project, id) != null) return "DisplayGroup";
            if (FindSegment(project, id) != null) return "Segment";
            if (FindPlane(project, id) != null) return "PixelPlane";
            return "";
        }

        public static List<string> Rows(Project project, string groupId)
        {
            RequireValid(project);
            DisplayGroup group = FindGroup(project, groupId);
            if (group == null) throw new ArgumentException("Unknown group.", "groupId");
            return CopyStrings(group.Members);
        }

        public static int ChannelWidth(RawChannel channel)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            return channel.Kind == "RGB" ? 3 : 1;
        }

        public static int OutputOffset(RawChannel channel)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (channel.Universe == 0) return -1;
            return ((channel.Universe - 1) * 512) + channel.Address - 1;
        }

        public static int OutputSize(Project project)
        {
            RequireValid(project);
            return OutputSizeUnchecked(project);
        }
        private static int OutputSizeUnchecked(Project project)
        {
            int size = 1;
            int i;
            for (i = 0; i < project.Channels.Count; i++)
            {
                RawChannel channel = project.Channels[i];
                if (channel.Universe != 0) size = Math.Max(size, OutputOffset(channel) + ChannelWidth(channel));
            }
            return size;
        }

        public static void AddRawChannels(Project project, int count, string prefix, string kind, int universe, int address)
        {
            RequireValid(project);
            if (count < 1 || count > 4096) throw new ArgumentOutOfRangeException("count", "Add 1..4096 raw channels in one operation.");
            if (!IsRawKind(kind)) throw new ArgumentException("Kind must be Dimmer or RGB.", "kind");
            if (universe < 0 || universe > 64 || address < 1 || address > 512) throw new ArgumentException("Invalid physical address.");
            if (String.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Prefix is required.", "prefix");
            int width = kind == "RGB" ? 3 : 1;
            Project candidate = Clone(project);
            int nextOrder = 0;
            int i;
            for (i = 0; i < candidate.Channels.Count; i++) nextOrder = Math.Max(nextOrder, candidate.Channels[i].OutputOrder);
            if (nextOrder > int.MaxValue - count) throw new ArgumentException("Output order has no room for more channels; renumber existing orders first.");
            int nextUniverse = universe;
            int nextAddress = address;
            for (i = 0; i < count; i++)
            {
                if (nextAddress + width - 1 > 512)
                {
                    if (nextUniverse == 0 || nextUniverse == 64) throw new ArgumentException("Channels exceed the available universe range.");
                    nextUniverse++;
                    nextAddress = 1;
                }
                string name = prefix + " " + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (FindRawByName(candidate, name) != null) throw new ArgumentException("Duplicate channel name: " + name, "prefix");
                candidate.Channels.Add(new RawChannel { Id = NewId(), Name = name, Kind = kind, ColorHtml = "#FFFFFF", Universe = nextUniverse, Address = nextAddress, OutputOrder = nextOrder + i + 1 });
                nextAddress += width;
            }
            RequireValid(candidate);
            project.Channels = candidate.Channels;
        }

        public static void RemoveTarget(Project project, string id)
        {
            RequireValid(project);
            if (String.IsNullOrEmpty(id)) throw new ArgumentException("Target id is required.", "id");
            if (FindGroup(project, id) != null) { project.Groups.Remove(FindGroup(project, id)); return; }
            if (FindRaw(project, id) == null && FindSegment(project, id) == null && FindPlane(project, id) == null) throw new ArgumentException("Unknown target.", "id");
            if (IsReferencedByEffect(project, id) || IsReferencedBySegment(project, id) || IsReferencedByPlane(project, id)) throw new InvalidOperationException("Target is still used by an effect, segment, or pixel plane.");
            RawChannel raw = FindRaw(project, id);
            if (raw != null) project.Channels.Remove(raw);
            Segment segment = FindSegment(project, id);
            if (segment != null) project.Segments.Remove(segment);
            PixelPlane plane = FindPlane(project, id);
            if (plane != null) project.PixelPlanes.Remove(plane);
            int i;
            for (i = 0; i < project.Groups.Count; i++) project.Groups[i].Members.RemoveAll(delegate(string member) { return member == id; });
            RequireValid(project);
        }

        public static List<string> BuildPlaneMap(Project project, int width, int height, string firstRawId, string axis, bool serpentine)
        {
            RequireValid(project);
            if (width < 1 || width > 32 || height < 1 || height > 32) throw new ArgumentOutOfRangeException("width");
            if (axis != "Horizontal" && axis != "Vertical") throw new ArgumentException("Axis must be Horizontal or Vertical.", "axis");
            List<RawChannel> rgb = new List<RawChannel>();
            int i;
            for (i = 0; i < project.Channels.Count; i++) if (project.Channels[i].Kind == "RGB") rgb.Add(project.Channels[i]);
            rgb.Sort(delegate(RawChannel a, RawChannel b) { int result = a.OutputOrder.CompareTo(b.OutputOrder); return result != 0 ? result : String.CompareOrdinal(a.Id, b.Id); });
            int start = -1;
            for (i = 0; i < rgb.Count; i++) if (rgb[i].Id == firstRawId) { start = i; break; }
            if (start < 0) throw new ArgumentException("firstRawId must identify an RGB raw channel.", "firstRawId");
            List<string> cells = new List<string>();
            for (i = 0; i < width * height; i++) cells.Add("");
            int source = start;
            int major, minor, x, y;
            int majorCount = axis == "Horizontal" ? height : width;
            int minorCount = axis == "Horizontal" ? width : height;
            for (major = 0; major < majorCount; major++)
            {
                for (minor = 0; minor < minorCount; minor++)
                {
                    int travel = serpentine && (major % 2 == 1) ? minorCount - 1 - minor : minor;
                    if (axis == "Horizontal") { x = travel; y = major; }
                    else { x = major; y = travel; }
                    if (source < rgb.Count) cells[(y * width) + x] = rgb[source].Id;
                    source++;
                }
            }
            return cells;
        }

        public static Effect NewEffect(Project project, string targetId, string kind, int startMs, int durationMs, string color)
        {
            RequireValid(project);
            string targetKind = TargetKind(project, targetId);
            if (targetKind.Length == 0 || targetKind == "DisplayGroup") throw new ArgumentException("Unknown effect target.", "targetId");
            if (startMs < 0 || durationMs < 1 || ((long)startMs + durationMs) > project.DurationMs) throw new ArgumentException("Effect is outside project duration.");
            ValidateColorOrThrow(color, "color");
            bool plane = targetKind == "PixelPlane";
            if (plane != (kind == "PixelPlane")) throw new ArgumentException(plane ? "PixelPlane targets require PixelPlane effects." : "Basic effects cannot target a PixelPlane.", "kind");
            if (!plane && !IsBasicEffectKind(kind)) throw new ArgumentException("Unsupported effect kind.", "kind");
            Effect effect = new Effect { Id = NewId(), TargetId = targetId, Kind = kind, ColorHtml = HtmlColor(ParseColor(color)), StartMs = startMs, DurationMs = durationMs, Level = 1 };
            if (kind == "Custom")
            {
                effect.Curve.Add(new CurvePoint { Position = 0, Value = 1 });
                effect.Curve.Add(new CurvePoint { Position = 1, Value = 1 });
            }
            if (plane)
            {
                PixelPlane pixelPlane = FindPlane(project, targetId);
                effect.Frames.Add(new PlaneFrame { Position = 0, Colors = PlaneColors(pixelPlane.Width * pixelPlane.Height, effect.ColorHtml) });
            }
            Project candidate = Clone(project);
            candidate.Effects.Add(effect);
            RequireValid(candidate);
            return effect;
        }

        public static Func<int, byte[]> CreateFrameRenderer(Project source)
        {
            RequireValid(source); Project snapshot = Clone(source); int size = OutputSizeUnchecked(snapshot);
            return delegate(int time) { return RenderFrameUnchecked(snapshot, time, size); };
        }
        public static byte[] RenderFrame(Project project, int timeMs)
        {
            RequireValid(project);
            return RenderFrameUnchecked(project, timeMs, OutputSizeUnchecked(project));
        }
        private static byte[] RenderFrameUnchecked(Project project, int timeMs, int outputSize)
        {
            if (timeMs < 0) timeMs = 0;
            byte[] output = new byte[outputSize];
            int i;
            for (i = 0; i < project.Effects.Count; i++)
            {
                Effect effect = project.Effects[i];
                if (timeMs < effect.StartMs || timeMs >= effect.StartMs + effect.DurationMs) continue;
                if (effect.Kind == "PixelPlane") RenderPlaneEffect(project, effect, timeMs, output);
                else RenderBasicEffect(project, effect, timeMs, output);
            }
            return output;
        }

        public static void ExportFseq(Project project, string path)
        {
            RequireValid(project);
            if (String.IsNullOrEmpty(path)) throw new ArgumentException("Path is required.", "path");
            int channels = OutputSizeUnchecked(project);
            int frames = project.DurationMs / project.FrameMs;
            int maxUniverse = 0;
            int i;
            for (i = 0; i < project.Channels.Count; i++) maxUniverse = Math.Max(maxUniverse, project.Channels[i].Universe);
            string finalPath = Path.GetFullPath(path);
            string temporaryPath = finalPath + "." + NewId() + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
                {
                    writer.Write(Encoding.ASCII.GetBytes("PSEQ"));
                    writer.Write((ushort)28);
                    writer.Write((byte)0);
                    writer.Write((byte)1);
                    writer.Write((ushort)28);
                    writer.Write((uint)channels);
                    writer.Write((uint)frames);
                    writer.Write((byte)project.FrameMs);
                    writer.Write((byte)0);
                    writer.Write((ushort)maxUniverse);
                    writer.Write((ushort)512);
                    writer.Write((byte)1);
                    writer.Write((byte)2);
                    writer.Write((ushort)0);
                    for (i = 0; i < frames; i++) writer.Write(RenderFrameUnchecked(project, i * project.FrameMs, channels));
                }
                if (File.Exists(finalPath)) File.Replace(temporaryPath, finalPath, null);
                else File.Move(temporaryPath, finalPath);
            }
            catch
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                throw;
            }
        }

        public static Color ParseColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#') throw new ArgumentException("Use an opaque #RRGGBB color.", "value");
            for (int i = 1; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i])) throw new ArgumentException("Use an opaque #RRGGBB color.", "value");
            int rgb = Int32.Parse(value.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }

        public static string HtmlColor(Color color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture) + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static void RenderBasicEffect(Project project, Effect effect, int timeMs, byte[] output)
        {
            double progress = (double)(timeMs - effect.StartMs) / effect.DurationMs;
            double intensity = effect.Level;
            if (effect.UseEndpoints)
            {
                double endpointProgress = Clamp01((double)(timeMs - effect.StartMs) / Math.Max(1, effect.DurationMs - project.FrameMs));
                intensity *= effect.StartLevel + (effect.EndLevel - effect.StartLevel) * endpointProgress;
            }
            else if (effect.Kind == "RampUp") intensity *= progress;
            else if (effect.Kind == "RampDown") intensity *= 1.0 - progress;
            else if (effect.Kind == "Custom") intensity *= CurveValue(effect.Curve, progress);
            intensity = Clamp01(intensity);
            Color color = ParseColor(effect.ColorHtml);
            RawChannel raw = FindRaw(project, effect.TargetId);
            if (raw != null) ApplyRaw(raw, color, intensity, output);
            else
            {
                Segment segment = FindSegment(project, effect.TargetId);
                int i;
                for (i = 0; i < segment.Members.Count; i++) ApplyRaw(FindRaw(project, segment.Members[i]), color, intensity, output);
            }
        }

        private static void RenderPlaneEffect(Project project, Effect effect, int timeMs, byte[] output)
        {
            PixelPlane plane = FindPlane(project, effect.TargetId);
            double normalized = ((double)(timeMs - effect.StartMs) / effect.DurationMs) / effect.SpeedDivisor;
            if (normalized < effect.Frames[0].Position) return;
            PlaneFrame frame = effect.Frames[0];
            int i;
            for (i = 0; i < effect.Frames.Count; i++)
            {
                if (effect.Frames[i].Position > normalized) break;
                frame = effect.Frames[i];
            }
            double fade = 1.0;
            int elapsed = timeMs - effect.StartMs;
            if (effect.FadeInMs > 0) fade = Math.Min(fade, (double)elapsed / effect.FadeInMs);
            if (effect.FadeOutMs > 0) fade = Math.Min(fade, (double)(effect.DurationMs - elapsed) / effect.FadeOutMs);
            fade = Clamp01(fade);
            for (i = 0; i < plane.Cells.Count; i++)
            {
                if (!String.IsNullOrEmpty(plane.Cells[i])) ApplyRaw(FindRaw(project, plane.Cells[i]), ParseColor(frame.Colors[i]), fade, output);
            }
        }

        // The original HLS overlap precedence is not established by the evidence.
        // This recreation deliberately uses component-wise highest-takes-precedence.
        private static void ApplyRaw(RawChannel raw, Color color, double intensity, byte[] output)
        {
            if (raw == null || raw.Universe == 0) return;
            int offset = OutputOffset(raw);
            if (raw.Kind == "Dimmer")
            {
                WriteHtp(output, offset, (byte)Math.Round(Math.Max(color.R, Math.Max(color.G, color.B)) * Clamp01(intensity)));
                return;
            }
            WriteHtp(output, offset, (byte)Math.Round(color.R * Clamp01(intensity)));
            WriteHtp(output, offset + 1, (byte)Math.Round(color.G * Clamp01(intensity)));
            WriteHtp(output, offset + 2, (byte)Math.Round(color.B * Clamp01(intensity)));
        }

        private static void WriteHtp(byte[] output, int offset, byte value)
        {
            if (offset >= 0 && offset < output.Length && value > output[offset]) output[offset] = value;
        }

        private static void ValidatePhysicalOverlap(Project project, List<string> errors)
        {
            Dictionary<int, string> addresses = new Dictionary<int, string>();
            int i, j;
            for (i = 0; i < project.Channels.Count; i++)
            {
                RawChannel channel = project.Channels[i];
                if (channel == null || channel.Universe == 0 || !IsRawKind(channel.Kind) || channel.Address < 1 || channel.Address > 512) continue;
                int offset = OutputOffset(channel);
                for (j = 0; j < ChannelWidth(channel); j++)
                {
                    int address = offset + j;
                    if (addresses.ContainsKey(address)) errors.Add("Physical output overlap between " + Text(addresses[address]) + " and " + Text(channel.Id) + ".");
                    else addresses.Add(address, channel.Id);
                }
            }
        }

        private static void ValidateReferences(Project project, List<string> errors, Dictionary<string, RawChannel> raws, Dictionary<string, string> kinds)
        {
            int i, j;
            for (i = 0; i < project.Groups.Count; i++)
            {
                DisplayGroup group = project.Groups[i]; if (group == null || group.Members == null) continue;
                Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (j = 0; j < group.Members.Count; j++)
                {
                    string member = group.Members[j];
                    if (String.IsNullOrEmpty(member) || !kinds.ContainsKey(member)) errors.Add("Group " + Text(group.Id) + " has an invalid member.");
                    else if (seen.ContainsKey(member)) errors.Add("Group " + Text(group.Id) + " has a duplicate member.");
                    else seen.Add(member, true);
                }
            }
            for (i = 0; i < project.Segments.Count; i++)
            {
                Segment segment = project.Segments[i]; if (segment == null || segment.Members == null) continue;
                Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (j = 0; j < segment.Members.Count; j++)
                {
                    string member = segment.Members[j];
                    if (String.IsNullOrEmpty(member) || !raws.ContainsKey(member)) errors.Add("Segment " + Text(segment.Id) + " may contain raw channels only.");
                    else if (seen.ContainsKey(member)) errors.Add("Segment " + Text(segment.Id) + " has a duplicate member.");
                    else seen.Add(member, true);
                }
            }
            for (i = 0; i < project.PixelPlanes.Count; i++)
            {
                PixelPlane plane = project.PixelPlanes[i]; if (plane == null || plane.Cells == null) continue;
                Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (j = 0; j < plane.Cells.Count; j++)
                {
                    string cell = plane.Cells[j];
                    if (String.IsNullOrEmpty(cell)) continue;
                    RawChannel raw;
                    if (!raws.TryGetValue(cell, out raw) || raw.Kind != "RGB") errors.Add("PixelPlane " + Text(plane.Id) + " cells must reference RGB raw channels or holes.");
                    else if (seen.ContainsKey(cell)) errors.Add("PixelPlane " + Text(plane.Id) + " contains a duplicate raw channel.");
                    else seen.Add(cell, true);
                }
            }
        }

        private static void ValidateEffects(Project project, List<string> errors, Dictionary<string, string> ids, Dictionary<string, string> kinds)
        {
            int i;
            for (i = 0; i < project.Effects.Count; i++)
            {
                Effect effect = project.Effects[i];
                if (effect == null) { errors.Add("Effects[" + i + "] is null."); continue; }
                string ignoredName = "";
                AddIdentity(errors, ids, null, effect.Id, ignoredName, "effect");
                string targetKind = "";
                if (String.IsNullOrEmpty(effect.TargetId) || !kinds.TryGetValue(effect.TargetId, out targetKind)) errors.Add("Effect " + Text(effect.Id) + " has an invalid target.");
                if (effect.StartMs < 0 || effect.DurationMs < 1 || ((long)effect.StartMs + effect.DurationMs) > project.DurationMs) errors.Add("Effect " + Text(effect.Id) + " is outside the timeline.");
                if (effect.DurationMs < project.FrameMs) errors.Add("Effect " + Text(effect.Id) + " must be at least one frame long.");
                if (effect.Kind != "Level" && effect.DurationMs < (project.FrameMs * 2)) errors.Add("Non-Level effect " + Text(effect.Id) + " must be at least two frames long.");
                if (!IsFinite(effect.Level) || effect.Level < 0 || effect.Level > 1) errors.Add("Effect " + Text(effect.Id) + " Level must be finite and 0..1.");
                if (!IsFinite(effect.StartLevel) || !IsFinite(effect.EndLevel) || effect.StartLevel < 0 || effect.StartLevel > 1 || effect.EndLevel < 0 || effect.EndLevel > 1) errors.Add("Ramp endpoints must be finite and 0..1.");
                if (effect.UseEndpoints && effect.Kind != "RampUp" && effect.Kind != "RampDown") errors.Add("Explicit endpoints apply to ramp primitives only.");
                if (effect.SpeedDivisor != 1 && effect.SpeedDivisor != 2 && effect.SpeedDivisor != 3 && effect.SpeedDivisor != 4) errors.Add("Effect " + Text(effect.Id) + " has an invalid SpeedDivisor.");
                if (effect.FadeInMs < 0 || effect.FadeOutMs < 0 || effect.FadeInMs > effect.DurationMs || effect.FadeOutMs > effect.DurationMs) errors.Add("Effect " + Text(effect.Id) + " has invalid fades.");
                if (targetKind == "PixelPlane")
                {
                    if (effect.Level != 1) errors.Add("PixelPlane intensity is authored through cell colors and fades; Level must be 1.");
                    if (effect.Kind != "PixelPlane") errors.Add("Basic effects are not allowed on PixelPlanes.");
                    PixelPlane plane = FindPlane(project, effect.TargetId);
                    ValidateFrames(effect, plane, errors);
                    if (effect.Curve == null) errors.Add("Effect " + Text(effect.Id) + " Curve list is required.");
                    else if (effect.Curve.Count != 0) errors.Add("PixelPlane effects cannot have a time curve.");
                }
                else
                {
                    if (effect.SpeedDivisor != 1 || effect.FadeInMs != 0 || effect.FadeOutMs != 0) errors.Add("Speed and clip fades apply only to PixelPlane spatial clips.");
                    if (!IsBasicEffectKind(effect.Kind)) errors.Add("Effect " + Text(effect.Id) + " has invalid basic kind.");
                    ValidateColor(errors, effect.ColorHtml, "Effect " + Text(effect.Id));
                    if (effect.Frames == null) errors.Add("Effect " + Text(effect.Id) + " Frames list is required.");
                    else if (effect.Frames.Count != 0) errors.Add("Basic effects cannot have PixelPlane frames.");
                    ValidateCurve(effect, errors);
                }
            }
        }

        private static void ValidateCurve(Effect effect, List<string> errors)
        {
            if (effect.Curve == null) { errors.Add("Effect " + Text(effect.Id) + " Curve list is required."); return; }
            if (effect.Curve.Count > 64) { errors.Add("A Custom curve may contain at most 64 points."); return; }
            if (effect.Kind != "Custom" && effect.Curve.Count != 0) errors.Add("Only Custom effects accept curve points.");
            if (effect.Kind == "Custom" && effect.Curve.Count == 0) errors.Add("Custom effect " + Text(effect.Id) + " needs curve points.");
            double previous = -1;
            int i;
            for (i = 0; i < effect.Curve.Count; i++)
            {
                CurvePoint point = effect.Curve[i];
                if (point == null || !IsFinite(point.Position) || !IsFinite(point.Value) || point.Position < 0 || point.Position > 1 || point.Value < 0 || point.Value > 1 || point.Position <= previous) errors.Add("Effect " + Text(effect.Id) + " has invalid curve points.");
                else previous = point.Position;
            }
        }

        private static void ValidateFrames(Effect effect, PixelPlane plane, List<string> errors)
        {
            if (effect.Frames == null) { errors.Add("PixelPlane effect " + Text(effect.Id) + " Frames list is required."); return; }
            if (effect.Frames.Count > 256) { errors.Add("A spatial clip may contain at most 256 frame keys."); return; }
            if (effect.Frames.Count == 0) errors.Add("PixelPlane effect " + Text(effect.Id) + " needs frame keys.");
            double previous = -1;
            int i, j;
            for (i = 0; i < effect.Frames.Count; i++)
            {
                PlaneFrame frame = effect.Frames[i];
                if (frame == null || !IsFinite(frame.Position) || frame.Position < 0 || frame.Position > 1 || frame.Position <= previous) { errors.Add("PixelPlane effect " + Text(effect.Id) + " has invalid frame ordering."); continue; }
                previous = frame.Position;
                if (frame.Colors == null || plane == null || frame.Colors.Count != plane.Width * plane.Height) { errors.Add("PixelPlane effect " + Text(effect.Id) + " frame size does not match plane."); continue; }
                for (j = 0; j < frame.Colors.Count; j++) ValidateColor(errors, frame.Colors[j], "PixelPlane effect " + Text(effect.Id) + " frame color");
            }
        }

        private static void AddIdentity(List<string> errors, Dictionary<string, string> ids, Dictionary<string, string> names, string id, string name, string label)
        {
            if (id != null && id.Length > 128) errors.Add("A " + label + " id exceeds 128 characters.");
            if (name != null && name.Length > 256) errors.Add("A " + label + " name exceeds 256 characters.");
            if (String.IsNullOrWhiteSpace(id)) errors.Add("A " + label + " id is required.");
            else if (ids.ContainsKey(id)) errors.Add("Duplicate id: " + id + ".");
            else ids.Add(id, label);
            if (names != null)
            {
                if (String.IsNullOrWhiteSpace(name)) errors.Add("A " + label + " name is required.");
                else if (names.ContainsKey(name)) errors.Add("Duplicate name: " + name + ".");
                else names.Add(name, label);
            }
        }

        private static bool IsReferencedByEffect(Project project, string id)
        {
            int i; for (i = 0; i < project.Effects.Count; i++) if (project.Effects[i].TargetId == id) return true; return false;
        }
        private static bool IsReferencedBySegment(Project project, string id)
        {
            int i, j; for (i = 0; i < project.Segments.Count; i++) for (j = 0; j < project.Segments[i].Members.Count; j++) if (project.Segments[i].Members[j] == id) return true; return false;
        }
        private static bool IsReferencedByPlane(Project project, string id)
        {
            int i, j; for (i = 0; i < project.PixelPlanes.Count; i++) for (j = 0; j < project.PixelPlanes[i].Cells.Count; j++) if (project.PixelPlanes[i].Cells[j] == id) return true; return false;
        }
        private static RawChannel FindRaw(Project project, string id) { int i; if (project == null || project.Channels == null) return null; for (i = 0; i < project.Channels.Count; i++) if (project.Channels[i] != null && project.Channels[i].Id == id) return project.Channels[i]; return null; }
        private static RawChannel FindRawByName(Project project, string name) { int i; for (i = 0; i < project.Channels.Count; i++) if (project.Channels[i].Name == name) return project.Channels[i]; return null; }
        private static DisplayGroup FindGroup(Project project, string id) { int i; if (project == null || project.Groups == null) return null; for (i = 0; i < project.Groups.Count; i++) if (project.Groups[i] != null && project.Groups[i].Id == id) return project.Groups[i]; return null; }
        private static Segment FindSegment(Project project, string id) { int i; if (project == null || project.Segments == null) return null; for (i = 0; i < project.Segments.Count; i++) if (project.Segments[i] != null && project.Segments[i].Id == id) return project.Segments[i]; return null; }
        private static PixelPlane FindPlane(Project project, string id) { int i; if (project == null || project.PixelPlanes == null) return null; for (i = 0; i < project.PixelPlanes.Count; i++) if (project.PixelPlanes[i] != null && project.PixelPlanes[i].Id == id) return project.PixelPlanes[i]; return null; }
        private static List<string> CopyStrings(List<string> values) { return values == null ? null : new List<string>(values); }
        private static int SafeCount<T>(List<T> values) { return values == null ? 0 : values.Count; }
        private static List<string> PlaneColors(int count, string color) { List<string> result = new List<string>(); int i; for (i = 0; i < count; i++) result.Add(color); return result; }
        private static bool IsRawKind(string kind) { return kind == "Dimmer" || kind == "RGB"; }
        private static bool IsBasicEffectKind(string kind) { return kind == "Level" || kind == "RampUp" || kind == "RampDown" || kind == "Custom"; }
        private static bool IsFrameStep(int value) { int i; for (i = 0; i < FrameSteps.Length; i++) if (FrameSteps[i] == value) return true; return false; }
        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Clamp01(double value) { return value < 0 ? 0 : (value > 1 ? 1 : value); }
        private static string Text(string value) { return String.IsNullOrEmpty(value) ? "(unnamed)" : value; }
        private static void ValidateColor(List<string> errors, string value, string owner) { try { ParseColor(value); } catch (ArgumentException) { errors.Add(owner + " has an invalid color."); } }
        private static void ValidateColorOrThrow(string value, string parameter) { ParseColor(value); }
        private static double CurveValue(List<CurvePoint> points, double progress)
        {
            if (points == null || points.Count == 0) return 0;
            if (progress <= points[0].Position) return points[0].Value;
            int i;
            for (i = 1; i < points.Count; i++)
            {
                if (progress <= points[i].Position)
                {
                    CurvePoint a = points[i - 1]; CurvePoint b = points[i];
                    double fraction = (progress - a.Position) / (b.Position - a.Position);
                    return a.Value + ((b.Value - a.Value) * fraction);
                }
            }
            return points[points.Count - 1].Value;
        }
    }

    public class EditSession
    {
        private class Snapshot
        {
            public Project Project;
            public string Description;
        }
        private Project current;
        private readonly List<Snapshot> undo = new List<Snapshot>();
        private readonly List<Snapshot> redo = new List<Snapshot>();
        public Project Project { get { return Model.Clone(current); } }
        public bool CanUndo { get { return undo.Count != 0; } }
        public bool CanRedo { get { return redo.Count != 0; } }
        public event EventHandler Changed;
        public string LastChange { get; private set; }

        public EditSession(Project project)
        {
            Model.RequireValid(project);
            current = Model.Clone(project);
            LastChange = "";
        }

        public void Commit(Project project, string description)
        {
            Model.RequireValid(project);
            Push(undo, current, LastChange);
            current = Model.Clone(project);
            redo.Clear();
            LastChange = String.IsNullOrWhiteSpace(description) ? "Edit" : description;
            OnChanged();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            Push(redo, current, LastChange);
            Snapshot snapshot = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            current = Model.Clone(snapshot.Project);
            LastChange = "Undo: " + snapshot.Description;
            OnChanged();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            Push(undo, current, LastChange);
            Snapshot snapshot = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            current = Model.Clone(snapshot.Project);
            LastChange = "Redo: " + snapshot.Description;
            OnChanged();
        }

        private void Push(List<Snapshot> list, Project project, string description)
        {
            list.Add(new Snapshot { Project = Model.Clone(project), Description = description ?? "Edit" });
            if (list.Count > 100) list.RemoveAt(0);
        }

        private void OnChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
