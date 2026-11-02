using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace HlsRecreation
{
    public static class Checks
    {
        private static readonly List<string> lines = new List<string>();
        private static int count;
        private static void Check(bool value, string name)
        { if (!value) throw new Exception("FAIL: " + name); count++; lines.Add("PASS: " + name); }
        private static void Reject(Action operation, string name)
        { bool rejected = false; try { operation(); } catch { rejected = true; } Check(rejected, name); }
        private static Effect Plain(Project p, string target, string color)
        { return Model.NewEffect(p, target, "Level", 0, 1000, color); }

        public static int Run(string directory)
        {
            directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory); lines.Clear(); count = 0; ProjectDialog.SuppressErrorDialogs = true;
            try
            {
                string examplesPath=BundledExamples.EnsureExamples(Path.Combine(directory,"included-examples"));
                Project p = Model.Seed(); p.AudioFile=Path.Combine(Path.GetDirectoryName(examplesPath),"demo.wav"); Model.RequireValid(p);
                Check(typeof(Project).Assembly.GetName().ProcessorArchitecture == ProcessorArchitecture.X86, "native assembly targets x86 for 32/64-bit Windows 7");
                Check(typeof(Project).Assembly.GetReferencedAssemblies().All(delegate(AssemblyName a) { return a.Version.Major == 4 && a.Version.Minor == 0; }), "runtime references are .NET Framework 4 assemblies");
                Check(p.Channels.Count >= 12 && p.Groups.Count >= 3 && p.Segments.Count >= 2 && p.PixelPlanes.Count >= 1, "seed has raw channels, flexible groups, Segments and PixelPlane");
                Project q = Model.Clone(p); q.Name = "Independent clone";
                Check(p.Name != q.Name && p.Channels[0].Id == q.Channels[0].Id, "clone separates changes and retains stable channel identity");

                Project raw = Model.Empty(); Model.AddRawChannels(raw, 2, "RGB boundary", "RGB", 1, 510);
                Check(raw.Channels[0].Universe == 1 && raw.Channels[0].Address == 510 && raw.Channels[1].Universe == 2 && raw.Channels[1].Address == 1, "bulk RGB address assignment respects 512-slot universe boundary");
                Project collision = Model.Clone(raw); collision.Channels[1].Universe = 1; collision.Channels[1].Address = 510;
                Check(Model.Validate(collision).Count != 0, "physical RGB overlap rejected");
                Project order = Model.Clone(p); order.Channels[1].OutputOrder = order.Channels[0].OutputOrder;
                Check(Model.Validate(order).Count > 0, "duplicate raw output-order values rejected before wiring wizard");
                order = Model.Clone(p); order.Channels[0].OutputOrder = int.MaxValue;
                Reject(delegate { Model.AddRawChannels(order, 1, "Overflow guard", "Dimmer", 3, 1); }, "output-order increment cannot wrap into negative values");
                Project alpha = Model.Clone(p); alpha.Channels[0].ColorHtml = "Transparent";
                Check(Model.Validate(alpha).Count > 0, "transparent or named colors cannot become unintended white output");
                Project limits = Model.Clone(p); limits.Effects[0].Curve.Clear();
                for (int i = 0; i < 65; i++) limits.Effects[0].Curve.Add(new CurvePoint { Position = i / 64.0, Value = .5 });
                Check(Model.Validate(limits).Count > 0, "oversized curve list rejected");
                limits = Model.Clone(p); Effect boundedClip = limits.Effects.Find(delegate(Effect e) { return e.Kind == "PixelPlane"; });
                while (boundedClip.Frames.Count < 257) boundedClip.Frames.Add(new PlaneFrame { Position = .5, Colors = new List<string>(boundedClip.Frames[0].Colors) });
                Check(Model.Validate(limits).Count > 0, "oversized spatial frame list rejected");
                Project inert = Model.Clone(p); inert.Effects[0].SpeedDivisor = 2;
                Check(Model.Validate(inert).Count > 0, "time effects reject inert PixelPlane speed/fade fields");
                Project local = Model.Empty(); Model.AddRawChannels(local, 1, "Timing only", "Dimmer", 0, 1);
                local.Effects.Add(Plain(local, local.Channels[0].Id, "#ffffff")); Model.RequireValid(local);
                Check(Model.RenderFrame(local, 0).All(delegate(byte b) { return b == 0; }), "universe-zero authoring channel produces no physical output");

                Project group = Model.Clone(p); string ch = group.Channels[0].Id;
                DisplayGroup g1 = new DisplayGroup(); g1.Id = Model.NewId(); g1.Name = "Overlap A"; g1.Members.Add(ch); g1.Members.Add(group.Channels[1].Id);
                DisplayGroup g2 = new DisplayGroup(); g2.Id = Model.NewId(); g2.Name = "Overlap B"; g2.Members.Add(group.Channels[1].Id); g2.Members.Add(ch);
                group.Groups.Add(g1); group.Groups.Add(g2); Model.RequireValid(group);
                DisplayGroup sameLabel = new DisplayGroup { Id = Model.NewId(), Name = group.Segments[0].Name };
                sameLabel.Members.Add(group.Segments[0].Id); group.Groups.Add(sameLabel); Model.RequireValid(group);
                Check(group.Groups.Exists(delegate(DisplayGroup g) { return g.Name == group.Segments[0].Name; }), "group and Segment labels use independent naming scopes");
                Check(Model.Rows(group, g1.Id)[0] == ch && Model.Rows(group, g2.Id)[1] == ch, "same raw channel belongs to multiple independently ordered work groups");
                byte[] before = Model.RenderFrame(group, 700); group.Groups.Remove(g1);
                Check(Model.RenderFrame(group, 700).SequenceEqual(before), "deleting a view group leaves effects and output unchanged");
                int beforeOffset = Model.OutputOffset(group.Channels[0]); group.Channels[0].OutputOrder = 999;
                Check(Model.OutputOffset(group.Channels[0]) == beforeOffset, "display/output-order metadata does not silently rewrite physical address");

                Project seg = Model.Clone(p); seg.Effects.Clear(); Segment segment = seg.Segments[0];
                seg.Effects.Add(Plain(seg, segment.Id, "#ff0000")); Model.RequireValid(seg);
                byte[] fan = Model.RenderFrame(seg, 0);
                Check(segment.Members.All(delegate(string id) { RawChannel c = seg.Channels.Find(delegate(RawChannel x) { return x.Id == id; }); return fan[Model.OutputOffset(c)] == 255; }), "one Segment effect fans out to every raw member");
                Reject(delegate { Model.RemoveTarget(seg, segment.Members[0]); }, "referenced raw channel deletion prevented");
                seg.Segments[0].Members.Add(seg.Segments[0].Id);
                Check(Model.Validate(seg).Count > 0, "nested/cyclic Segment reference rejected");

                Project pp = Model.Clone(p); pp.Effects.Clear(); PixelPlane plane = pp.PixelPlanes[0];
                List<string> horizontal = Model.BuildPlaneMap(pp, 3, 2, pp.Channels[0].Id, "Horizontal", true);
                List<string> vertical = Model.BuildPlaneMap(pp, 3, 2, pp.Channels[0].Id, "Vertical", true);
                Check(horizontal[0] == pp.Channels[0].Id && horizontal[3] == pp.Channels[5].Id, "horizontal serpentine wizard maps logical XY to physical channel order");
                Check(vertical[0] == pp.Channels[0].Id && vertical[1] == pp.Channels[3].Id, "vertical serpentine wizard maps logical XY independently");
                Effect spatial = Model.NewEffect(pp, plane.Id, "PixelPlane", 0, 1000, "#ffffff");
                spatial.Frames.Clear(); PlaneFrame frame = new PlaneFrame(); frame.Position = 0;
                for (int i = 0; i < plane.Width * plane.Height; i++) frame.Colors.Add(i == 0 ? "#ff0000" : "#000000");
                spatial.Frames.Add(frame); pp.Effects.Add(spatial); Model.RequireValid(pp);
                frame.Position = .5;
                Check(Model.RenderFrame(pp, 0).All(delegate(byte b) { return b == 0; }), "future spatial key is dark before its authored key time");
                frame.Position = 0;
                string originalId = plane.Cells[0], swappedId = plane.Cells[1];
                byte[] first = Model.RenderFrame(pp, 0); plane.Cells[0] = swappedId; plane.Cells[1] = originalId;
                Model.RequireValid(pp); byte[] remapped = Model.RenderFrame(pp, 0);
                RawChannel originalChannel = pp.Channels.Find(delegate(RawChannel x) { return x.Id == originalId; });
                RawChannel swappedChannel = pp.Channels.Find(delegate(RawChannel x) { return x.Id == swappedId; });
                Check(first[Model.OutputOffset(originalChannel)] == 255 && remapped[Model.OutputOffset(swappedChannel)] == 255 && remapped[Model.OutputOffset(originalChannel)] == 0, "rewiring PixelPlane mapping preserves authored spatial frame and changes physical destination");
                plane.Cells[0] = ""; Model.RequireValid(pp);
                Check(Model.RenderFrame(pp, 0).All(delegate(byte b) { return b == 0; }), "unmapped PixelPlane hole remains dark");
                Project badPlane = Model.Clone(pp); badPlane.PixelPlanes[0].Width++;
                Check(Model.Validate(badPlane).Count > 0, "PixelPlane dimensions/frame length mismatch rejected");
                Reject(delegate { Model.NewEffect(pp, plane.Id, "RampUp", 0, 1000, "#ffffff"); }, "PixelPlane row rejects time-domain RampUp primitive");
                PlaneFrame late = new PlaneFrame(); late.Position = .5;
                for (int i = 0; i < plane.Width * plane.Height; i++) late.Colors.Add(i == 1 ? "#00ff00" : "#000000");
                spatial.Frames.Add(late); spatial.SpeedDivisor = 2;
                Check(Model.RenderFrame(pp, 600).All(delegate(byte b) { return b == 0; }), "half-speed spatial clip delays its second painted frame");
                spatial.SpeedDivisor = 1;
                Check(Model.RenderFrame(pp, 600).Any(delegate(byte b) { return b > 0; }), "full-speed spatial clip reaches its second painted frame");
                spatial.FadeOutMs = 500;
                Check(Model.RenderFrame(pp, 900).Max() < Model.RenderFrame(pp, 600).Max(), "spatial clip fade-out changes rendered intensity");

                EditSession edits = new EditSession(p); int changed = 0; edits.Changed += delegate { changed++; };
                Project invalid = Model.Clone(p); invalid.Channels[0].Address = 0;
                Reject(delegate { edits.Commit(invalid, "bad"); }, "invalid editor transaction rejected");
                Check(changed == 0 && edits.Project.Channels[0].Address > 0, "rejected transaction does not mutate project or history");
                Project named = Model.Clone(p); named.Name = "Changed"; edits.Commit(named, "Rename"); edits.Undo();
                Check(edits.Project.Name == p.Name && edits.CanRedo, "undo restores project snapshot"); edits.Redo();
                Check(edits.Project.Name == "Changed" && changed == 3, "redo restores edited structure and raises notifications");

                string path = Path.Combine(directory, "roundtrip.hlsr"); ProjectIO.Save(p, path); ProjectIO.Save(group, path);
                Project loaded = ProjectIO.Load(path);
                Check(loaded.Channels[0].Id == group.Channels[0].Id && loaded.Groups.Count == group.Groups.Count && File.Exists(path + ".bak"), "atomic save/load retains IDs, groups and previous-save backup");
                byte[] xmlBefore = File.ReadAllBytes(path);
                Reject(delegate { ProjectIO.Save(invalid, path); }, "invalid save rejected before touching file");
                Check(File.ReadAllBytes(path).SequenceEqual(xmlBefore), "failed save preserves previous project bytes");
                string hostile = Path.Combine(directory, "dtd.hlsr");
                string hostileXml = File.ReadAllText(path);
                int declaration = hostileXml.IndexOf("?>", StringComparison.Ordinal);
                hostileXml = hostileXml.Insert(declaration + 2, "<!DOCTYPE Project [<!ENTITY test 'Entity expansion should be prohibited'>]>");
                File.WriteAllText(hostile, hostileXml);
                Reject(delegate { ProjectIO.Load(hostile); }, "project loader prohibits DTD/entity expansion");
                string fseq = Path.Combine(directory, "native-demo.fseq"); Model.ExportFseq(p, fseq);
                using (BinaryReader reader = new BinaryReader(File.OpenRead(fseq)))
                {
                    Check(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "PSEQ", "native export FSEQ signature");
                    int offset = reader.ReadUInt16(); reader.ReadByte(); int version = reader.ReadByte(); reader.ReadUInt16();
                    int slots = reader.ReadInt32(), frames = reader.ReadInt32(); int step = reader.ReadByte();
                    Check(version == 1 && slots == Model.OutputSize(p) && frames == p.DurationMs / p.FrameMs && step == p.FrameMs, "FSEQ v1 dimensions and frame interval");
                    reader.BaseStream.Position = offset + (700 / p.FrameMs) * slots;
                    Check(reader.ReadBytes(slots).SequenceEqual(Model.RenderFrame(p, (700 / p.FrameMs) * p.FrameMs)), "exported frame equals direct sampling including hierarchy mapping");
                }
                float[] waveform = AudioClock.ReadWaveform(Path.Combine(Path.GetDirectoryName(examplesPath), "demo.wav"), 960);
                Check(waveform.Length == 960 && waveform.Max() > .1f, "original WAV decoded for waveform without opening audio device");
                RenderForms(p, directory);
                using (MainForm opened = new MainForm(new EditSession(p)))
                {
                    opened.RecoveryEnabled = false;
                    Project changedProject = opened.Session.Project; changedProject.Name = "Unsaved prior file";
                    opened.Session.Commit(changedProject, "Prior edit");
                    opened.LoadProject(path);
                    Check(!opened.Session.CanUndo && !opened.IsDirty && opened.CurrentFilePath == Path.GetFullPath(path), "opening a file resets history and saved-file state");
                    opened.Session.Undo();
                    Check(opened.Session.Project.Name == group.Name, "undo after Open cannot restore a different file into the current save path");
                    opened.SetFrameInterval(10);
                    Check(opened.Session.Project.FrameMs == 10, "native time-resolution command selects 100fps data sampling");
                }
                using (MainForm saveAs = new MainForm(new EditSession(p)))
                {
                    saveAs.RecoveryEnabled = false;
                    string relocated = Path.Combine(directory, "saved-elsewhere.hlsr"); saveAs.SaveProjectTo(relocated);
                    Check(File.Exists(ProjectIO.Load(relocated).AudioFile), "Save As preserves the source audio reference when the project moves folders");
                }
                using (TimelineView fine = new TimelineView())
                {
                    Project fineProject = Model.Clone(p); fineProject.FrameMs = 10; fineProject.Effects.Clear();
                    Effect visible = Model.NewEffect(fineProject, fineProject.Channels[0].Id, "Level", 10, 10, "#ff0000"); fineProject.Effects.Add(visible);
                    Effect top = Model.NewEffect(fineProject, fineProject.Channels[0].Id, "Level", 10, 10, "#00ff00"); fineProject.Effects.Add(top);
                    fine.Project = fineProject; fine.GroupId = fineProject.Groups[0].Id; fine.PixelsPerSecond = 1000;
                    Check(fine.SnapTime(TimelineView.LabelWidth + 17) == 10, "fine timeline snapping uses project 10ms interval");
                    Check(fine.HitEffect(new Point(TimelineView.LabelWidth + 14, TimelineView.HeaderHeight + 10)) == top.Id, "overlap hit-test selects the visible topmost fine-resolution effect");
                    Check(fine.HitEffect(new Point(TimelineView.LabelWidth + 14, 20)) == null, "waveform/header cannot select row-zero clips");
                    fine.PixelsPerSecond = 80;
                    Check(fine.IsResizeHandle(top, (int)Math.Ceiling(fine.EffectRectangle(top).Right) - 1), "minimum-width fine-resolution clip retains an accessible resize edge");
                }
                using (LightPreview many = new LightPreview())
                using (Bitmap bitmap = new Bitmap(250, 220))
                {
                    Project expanded = Model.Clone(p); Model.AddRawChannels(expanded, 110, "Preview extra", "Dimmer", 2, 1);
                    many.Project = expanded; many.Size = bitmap.Size; PrepareHiddenControl(many); many.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    Check(many.AutoScrollMinSize.Height > many.ClientSize.Height, "light preview has scrollable layout beyond one viewport");
                    many.AutoScrollPosition = new Point(0, many.AutoScrollMinSize.Height);
                    many.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    Check(many.VisibleChannelIds.Contains(expanded.Channels[expanded.Channels.Count - 1].Id), "last raw light remains reachable beyond the former 96-channel preview cap");
                }
                CheckManagerActions(p);
                CustomLibraryChecks.Run(directory, Check, Reject);
                LargeProjectChecks.Run(directory, Check);
                DisplayOutputChecks.Run(directory, Check, Reject);
                BundledExampleChecks.Run(directory, Check, Reject);
                lines.Add("PASS: " + count + " checks. No visible windows or audio device were opened. Actual Windows 7 execution is unverified.");
                File.WriteAllLines(Path.Combine(directory, "validation.txt"), lines.ToArray());
                return 0;
            }
            catch (Exception error)
            {
                lines.Add(error.ToString()); File.WriteAllLines(Path.Combine(directory, "validation.txt"), lines.ToArray()); return 1;
            }
        }
        private static void RenderForms(Project project, string directory)
        {
            using (MainForm form = new MainForm(new EditSession(project)))
            using (Bitmap bitmap = new Bitmap(1280, 880))
            {
                form.ClientSize = bitmap.Size; PrepareHiddenControl(form);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, "main-form.png"), System.Drawing.Imaging.ImageFormat.Png);
                Check(FindControls(form).OfType<TimelineView>().Any(), "native main form constructs a real timeline offscreen");
            }
            foreach (string name in new string[] { "RawChannelManager", "GroupManager", "SegmentManager", "PixelPlaneManager" })
            {
                Type type = typeof(MainForm).Assembly.GetType("HlsRecreation." + name);
                if (type == null) throw new Exception("Missing manager form " + name);
                using (Form dialog = (Form)Activator.CreateInstance(type, new object[] { project }))
                using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    PrepareHiddenControl(dialog);
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(directory, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    List<Control> controls = FindControls(dialog);
                    Check(controls.OfType<Button>().Count() >= 2, name + " contains working edit/save controls");
                    if (name == "RawChannelManager")
                        Check(controls.OfType<DataGridView>().Any(delegate(DataGridView g) { return g.Rows.Count >= project.Channels.Count; }), "raw manager displays channel records, not an empty field-binding grid");
                }
            }
            Effect clip = project.Effects.Find(delegate(Effect effect) { return effect.Kind == "PixelPlane"; });
            using (SpatialEffectEditor dialog = new SpatialEffectEditor(project, clip.Id))
            using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height))
            { PrepareHiddenControl(dialog); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "SpatialEffectEditor.png"), System.Drawing.Imaging.ImageFormat.Png); Check(true, "spatial editor constructs with seeded fades and actual matrix controls"); }
        }
        private static void CheckManagerActions(Project project)
        {
            using (GroupManager group = new GroupManager(project))
            {
                PrepareHiddenControl(group); InvokeButton(group, "New");
                group.groupAvailable.SetSelected(0, true); group.groupAvailable.SetSelected(1, true); InvokeButton(group, "Add →");
                DisplayGroup created = group.Result.Groups[group.Result.Groups.Count - 1];
                Check(created.Members.Count == 2, "group manager adds disjoint selected raw references without moving channels");
                group.groupOrdered.SelectedIndex = 1; InvokeButton(group, "Up");
                Check(created.Members[0] == project.Channels[1].Id, "group Up changes that group's row order");
                InvokeButton(group, "Remove");
                Check(created.Members.Count == 1 && group.Result.Channels.Count == project.Channels.Count, "group Remove keeps underlying channels");
                InvokeButton(group, "Delete");
                Check(group.Result.Groups.Count == project.Groups.Count, "group Delete removes only the editor view");
            }
            using (SegmentManager segment = new SegmentManager(project))
            {
                PrepareHiddenControl(segment); InvokeButton(segment, "New");
                ListBox rawList = (ListBox)typeof(SegmentManager).GetField("raw", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(segment);
                rawList.SetSelected(0, true); rawList.SetSelected(1, true); InvokeButton(segment, "Add raw");
                Segment created = segment.Result.Segments[segment.Result.Segments.Count - 1];
                Check(created.Members.Count == 2, "Segment manager authors actual raw membership");
                InvokeButton(segment, "Delete");
                Check(segment.Result.Segments.Count == project.Segments.Count, "unused Segment can be deleted without deleting raw channels");
            }
            using (RawChannelManager raw = new RawChannelManager(project))
            {
                PrepareHiddenControl(raw);
                raw.rawGrid.Rows[0].Cells["Name"].Value = "Edited before Add";
                raw.rawPrefix.Text = "Extra dimmer"; raw.rawUniverse.Value = 2; raw.rawAddress.Value = 1; raw.rawAddCount.Value = 1;
                raw.AddChannelsFromControls();
                Check(raw.Result.Channels.Count == project.Channels.Count + 1 && raw.Result.Channels[0].Name == "Edited before Add", "raw Add retains pending table edits and adds the requested physical channel");
                Check(project.Channels[0].Name != "Edited before Add", "manager working changes stay isolated until Save");
                raw.rawGrid.Rows[0].Cells["Universe"].Value = "not a number";
                Reject(delegate { raw.ApplyGridEdits(); }, "invalid numeric raw cell is rejected rather than silently reverted");
                Check(raw.Result.Channels[0].Universe == 1, "failed grid parse does not partly mutate manager data");
            }
            using (PixelPlaneManager map = new PixelPlaneManager(project))
            {
                PrepareHiddenControl(map);
                map.CreatePlane(); map.pixelName.Text = "Editable sparse plane"; map.pixelWidth.Value = 3; map.pixelHeight.Value = 2; map.ApplyDisplayed();
                map.pixelFirst.SelectedIndex = 0; map.pixelAxis.SelectedItem = "Horizontal"; map.pixelSerpentine.Checked = true; map.BuildMapping();
                PixelPlane plane = map.Result.PixelPlanes.Find(delegate(PixelPlane p) { return p.Name == "Editable sparse plane"; });
                Check(plane != null && plane.Cells.Count == 6 && plane.Cells[3] == project.Channels[5].Id, "PixelPlane manager creates dimensions and applies the named serpentine wizard");
                map.pixelCells.Rows[0].Cells["Channel"].Value = ""; map.ApplyDisplayed();
                Check(plane.Cells[0] == "", "manual PixelPlane empty-cell edit is retained as a hole");
                Check(project.PixelPlanes.Count + 1 == map.Result.PixelPlanes.Count, "new PixelPlane is isolated from the original project until Save");
            }
            Effect original = project.Effects.Find(delegate(Effect effect) { return effect.Kind == "PixelPlane"; });
            using (SpatialEffectEditor painted = new SpatialEffectEditor(project, original.Id))
            {
                PrepareHiddenControl(painted);
                int previous = painted.Result.Effects.Find(delegate(Effect effect) { return effect.Id == original.Id; }).Frames.Count;
                painted.AddKey(.5); painted.PaintAt(0, 0, Color.Red);
                Effect changed = painted.Result.Effects.Find(delegate(Effect effect) { return effect.Id == original.Id; });
                Check(changed.Frames.Count == previous + 1 && changed.Frames.Find(delegate(PlaneFrame key) { return key.Position == .5; }).Colors[0] == "#FF0000", "spatial Add key and cell painting change the selected key only");
                painted.spatialAddPosition.Value = .55M; painted.DuplicateKey();
                Check(changed.Frames.Find(delegate(PlaneFrame key) { return key.Position == .55; }).Colors[0] == "#FF0000", "spatial duplicate retains editable painted colors at its new key time");
                painted.DeleteKey();
                Check(changed.Frames.Count == previous + 1 && original.Frames.Count == previous, "spatial delete and Cancel isolation retain original frames");
            }
            CultureInfo previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Effect custom = project.Effects.Find(delegate(Effect effect) { return effect.Kind == "Custom"; });
                using (BasicEffectEditor dialog = new BasicEffectEditor(project, custom.Id))
                {
                    typeof(BasicEffectEditor).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[0]);
                    Check(dialog.Result.Effects.Find(delegate(Effect effect) { return effect.Id == custom.Id; }).Curve.Count == custom.Curve.Count, "Custom curve round-trips on comma-decimal Windows locale");
                }
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture; }
        }
        private static void InvokeButton(Control root, string caption)
        {
            Button button = FindControls(root).OfType<Button>().First(delegate(Button b) { return b.Text == caption; });
            // Unit invocation of this application's handler, not OS mouse/key input.
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
        }
        private static List<Control> FindControls(Control parent)
        {
            List<Control> controls = new List<Control>();
            foreach (Control child in parent.Controls) { controls.Add(child); controls.AddRange(FindControls(child)); }
            return controls;
        }
        public static void PrepareHiddenControl(Control control)
        {
            // Create this application's control handles while the top-level form stays
            // invisible. No Show, focus change, input simulation, or desktop capture.
            IntPtr handle = control.Handle;
            control.PerformLayout();
            foreach (Control child in control.Controls) PrepareHiddenControl(child);
        }
    }
}
