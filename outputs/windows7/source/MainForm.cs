using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace HlsRecreation
{
    public sealed class MainForm : Form
    {
        private EditSession session;
        private readonly TimelineView timeline = new TimelineView();
        private readonly LightPreview preview = new LightPreview();
        private readonly LayoutCanvas layoutPreview = new LayoutCanvas();
        private readonly CheckBox physicalOutput = new CheckBox { Text = "Send to controllers", AutoSize = true, Checked = false, Margin = new Padding(5, 7, 4, 0) };
        private OutputPump outputPump;
        private Func<int, byte[]> playbackRenderer;
        private string compiledShowPath;
        private readonly ComboBox groups = new ComboBox();
        private readonly Timer timer = new Timer();
        private readonly AudioClock clock = new AudioClock();
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel showStatus = new ToolStripStatusLabel { Text = "Show: not compiled" };
        private readonly Label position = new Label { Text = "00:00.000", AutoSize = true, Margin = new Padding(4, 8, 4, 0) };
        private string fileName, activeGroup;
        private bool refreshing, dirty;
        private Color toolColor = Color.White;
        private LibraryItem pendingLibraryItem;
        private string lastLibraryPath;
        public bool RecoveryEnabled = true;
        public EditSession Session { get { return session; } }
        public string CurrentFilePath { get { return fileName; } }
        public bool IsDirty { get { return dirty; } }
        public MainForm(EditSession initial)
        {
            session = new EditSession(ResolveAudioReference(initial.Project, null)); Text = "HLS-style Sequencer - Windows 7"; Size = new Size(1240, 780); MinimumSize = new Size(1000, 650);
            Font = new Font("Tahoma", 8.25f); Build(); session.Changed += SessionChanged;
            timer.Interval = 25; timer.Tick += Tick; RefreshUi(); ReloadWaveform();
        }
        private void Build()
        {
            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("File"), audio = new ToolStripMenuItem("Audio File"), manage = new ToolStripMenuItem("Manage Channels");
            file.DropDownItems.Add("New", null, delegate { if (ConfirmDiscard()) ReplaceProject(Model.Empty(), null); });
            file.DropDownItems.Add("Open...", null, Open); file.DropDownItems.Add("Save", null, delegate { SaveCurrent(); });
            file.DropDownItems.Add("Save As...", null, delegate { SaveAs(); }); file.DropDownItems.Add("Restore recovery", null, RestoreRecovery);
            file.DropDownItems.Add("Export FSEQ...", null, Export);
            audio.DropDownItems.Add("Add WAV file for this sequence...", null, ChooseAudio);
            ToolStripMenuItem sequence = new ToolStripMenuItem("Sequence"), resolution = new ToolStripMenuItem("Time resolution");
            foreach (int interval in new int[] { 10, 20, 25, 50, 100 })
            {
                int selectedInterval = interval;
                resolution.DropDownItems.Add(interval + " ms (" + (1000 / interval) + " fps)", null, delegate { Guard(delegate { SetFrameInterval(selectedInterval); }); });
            }
            sequence.DropDownItems.Add(resolution);
            ToolStripMenuItem library = new ToolStripMenuItem("Library");
            library.DropDownItems.Add("Make New Library / Open Library...", null, ManageLibrary);
            library.DropDownItems.Add("Add Effect Group to a Library...", null, ManageLibrary);
            library.DropDownItems.Add("Insert Into Sequence an Effect from Library...", null, ManageLibrary);
            sequence.DropDownItems.Add("Create / modify custom effects...", null, delegate { OpenCustomEditor(CurrentSelection()); });
            manage.DropDownItems.Add("Manage Raw Channels", null, delegate { Edit(new RawChannelManager(session.Project), "Raw channels"); });
            manage.DropDownItems.Add("Select Channels and Assign to Display Group", null, delegate { Edit(new GroupManager(session.Project), "Display groups"); });
            manage.DropDownItems.Add("Select Group to Work On", null, delegate { groups.Focus(); groups.DroppedDown = true; });
            manage.DropDownItems.Add("Manage Segments", null, delegate { Edit(new SegmentManager(session.Project), "Segments"); });
            manage.DropDownItems.Add("Manage PixelPlanes", null, delegate { Edit(new PixelPlaneManager(session.Project), "PixelPlanes"); });
            ToolStripMenuItem display = new ToolStripMenuItem("Preview"), output = new ToolStripMenuItem("Output");
            display.DropDownItems.Add("Create / Edit Preview...", null, delegate { Edit(new PreviewLayoutDialog(session.Project), "Preview layout"); });
            display.DropDownItems.Add("Compile / Save Preview Control File...", null, CompilePreview);
            display.DropDownItems.Add("Identify Preview Control File...", null, IdentifyPreview);
            output.DropDownItems.Add("Prepare Channels for Output", null, delegate { Guard(delegate { StopPreview(false); OutputPlan plan = ControllerOutput.Prepare(session.Project); ShowReport("Prepare Channels for Output", plan.Summary + Environment.NewLine + String.Join(Environment.NewLine, plan.Errors.ToArray()) + Environment.NewLine + String.Join(Environment.NewLine, plan.Warnings.ToArray())); }); });
            output.DropDownItems.Add("Select Output Method...", null, delegate { Edit(new OutputSetupDialog(session.Project), "Controller output setup"); });
            output.DropDownItems.Add("Compile for Show...", null, CompileShow);
            output.DropDownItems.Add("Check compiled show data...", null, CheckShow);
            output.DropDownItems.Add("Test Output Lights...", null, delegate { Guard(delegate { StopPreview(false); using(OutputTestDialog dialog = new OutputTestDialog(session.Project)) dialog.ShowDialog(this); }); });
            menu.Items.Add(file); menu.Items.Add(audio); menu.Items.Add(sequence); menu.Items.Add(manage); menu.Items.Add(library); menu.Items.Add(display); menu.Items.Add(output);
            menu.Items.Add("Undo", null, delegate { session.Undo(); }); menu.Items.Add("Redo", null, delegate { session.Redo(); }); MainMenuStrip = menu;

            FlowLayoutPanel tools = new FlowLayoutPanel(); tools.Dock = DockStyle.Top; tools.Height = 35; tools.WrapContents = false; tools.AutoScroll = true;
            groups.Name = "activeGroup"; groups.Width = 165; groups.DropDownStyle = ComboBoxStyle.DropDownList; groups.FormattingEnabled = true;
            groups.Format += delegate(object s, ListControlConvertEventArgs e) { DisplayGroup g = e.ListItem as DisplayGroup; e.Value = g == null ? "" : g.Name; };
            groups.SelectedIndexChanged += delegate
            {
                if (refreshing) return; DisplayGroup g = groups.SelectedItem as DisplayGroup;
                CancelLibraryPick();
                activeGroup = g == null ? null : g.Id; timeline.ClearSelection(); timeline.HighlightTargetId = null; timeline.GroupId = activeGroup; timeline.Invalidate();
            };
            tools.Controls.Add(new Label { Text = "Work group", AutoSize = true, Margin = new Padding(6, 8, 4, 0) }); tools.Controls.Add(groups);
            Button color = new Button { Text = "Color", Width = 47, Height = 26 }; color.BackColor = toolColor;
            color.Click += delegate { using (ColorDialog dialog = new ColorDialog()) { dialog.Color = toolColor; if (dialog.ShowDialog(this) == DialogResult.OK) { toolColor = dialog.Color; color.BackColor = toolColor; } } };
            tools.Controls.Add(color);
            foreach (string label in new string[] { "Level", "RampUP", "RampDN", "Custom" })
            {
                Button button = new Button { Text = label, Width = 58, Height = 26 }; button.Click += Tool; tools.Controls.Add(button);
            }
            Button spatial = new Button { Text = "Edit spatial", Width = 80, Height = 26 }; spatial.Click += EditSpatial; tools.Controls.Add(spatial);
            Button play = new Button { Text = "Preview", Width = 65, Height = 26 }; play.Click += Preview; tools.Controls.Add(play);
            Button stop = new Button { Text = "Stop", Width = 45, Height = 26 }; stop.Click += delegate { Guard(delegate { StopPreview(true); }); }; tools.Controls.Add(stop); tools.Controls.Add(physicalOutput); tools.Controls.Add(position);
            ComboBox zoom = new ComboBox(); zoom.DropDownStyle = ComboBoxStyle.DropDownList; zoom.Width = 80;
            zoom.Items.AddRange(new object[] { "80 px/s", "160 px/s", "320 px/s" }); zoom.SelectedIndex = 0;
            zoom.SelectedIndexChanged += delegate { timeline.PixelsPerSecond = zoom.SelectedIndex == 0 ? 80 : zoom.SelectedIndex == 1 ? 160 : 320; timeline.Invalidate(); }; tools.Controls.Add(zoom);

            TableLayoutPanel body = new TableLayoutPanel(); body.Dock = DockStyle.Fill; body.ColumnCount = 2; body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 77)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            timeline.Name = "timeline"; timeline.Dock = DockStyle.Fill; preview.Dock = DockStyle.Fill; preview.Name = "lightPreview";
            Panel previewHost = new Panel { Dock = DockStyle.Fill }; layoutPreview.Dock = DockStyle.Fill; layoutPreview.EditMode = false; layoutPreview.Name = "displayPreview";
            previewHost.Controls.Add(preview); previewHost.Controls.Add(layoutPreview);
            body.Controls.Add(timeline, 0, 0); body.Controls.Add(previewHost, 1, 0);
            layoutPreview.NodeSelected += delegate(object sender, PreviewNodeEventArgs selected) { if(selected.ChannelIds.Count > 0) RevealRaw(selected.ChannelIds[0]); else Say("Preview node has no raw-channel assignment."); };
            timeline.SelectionFinished += delegate { Say("Rectangle ready. Choose Level, RampUP, RampDN or Custom."); };
            timeline.LibraryAnchorPicked += delegate(object sender, LibraryAnchorEventArgs e) { Guard(delegate { InsertPendingAt(e.Row, e.TimeMs); }); };
            timeline.LibraryPickCancelled += delegate { CancelLibraryPick(); Say("Library insertion cancelled."); };
            timeline.TimingRequested += delegate(object s, EffectTimingEventArgs e)
            {
                Guard(delegate { Project p = session.Project; Effect effect = p.Effects.Find(delegate(Effect f) { return f.Id == e.Id; }); if (effect == null) return; effect.StartMs = e.StartMs; effect.DurationMs = e.DurationMs; session.Commit(p, "Move/resize effect"); });
            };
            timeline.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { string id = timeline.HitEffect(e.Location); if (id != null) Context(id, e.Location); } };
            preview.ChannelSelected += delegate(object s, RawSelectedEventArgs e) { RevealRaw(e.Id); };
            StatusStrip footer = new StatusStrip(); status.Spring = true; status.TextAlign = ContentAlignment.MiddleLeft; footer.Items.Add(status); footer.Items.Add(showStatus);
            // WinForms docks in reverse z-order: fill must be added first.
            Controls.Add(body); Controls.Add(tools); Controls.Add(menu); Controls.Add(footer);
            Say("Two clicks mark a rectangle. Raw channels -> work group -> handcrafted effects. Preview and output maps are independent.");
        }
        private void SessionChanged(object sender, EventArgs e)
        {
            StopPreview(false); dirty = true; showStatus.Text = compiledShowPath == null ? "Show: not compiled" : "Show: changed — compile again"; RefreshUi(); ReloadWaveform(); Say(session.LastChange + ". Playback/render inputs changed.");
            if (RecoveryEnabled)
                try { ProjectIO.Save(session.Project, RecoveryPath()); } catch (Exception error) { Say("Edit retained, recovery save failed: " + error.Message); }
        }
        private static string RecoveryPath() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HlsStyleSequencer", "recovery.hlsr"); }
        private void RefreshUi()
        {
            CancelLibraryPick();
            Project p = session.Project; string wanted = activeGroup; refreshing = true;
            groups.DataSource = null; groups.DataSource = p.Groups;
            DisplayGroup chosen = p.Groups.Find(delegate(DisplayGroup g) { return g.Id == wanted; });
            groups.SelectedItem = chosen ?? (p.Groups.Count == 0 ? null : p.Groups[0]);
            activeGroup = groups.SelectedItem == null ? null : ((DisplayGroup)groups.SelectedItem).Id; refreshing = false;
            timeline.ClearSelection(); timeline.Project = p; timeline.GroupId = activeGroup; timeline.Invalidate(); preview.Project = p; preview.Frame = null; preview.Invalidate();
            layoutPreview.Project = p; layoutPreview.Frame = null; layoutPreview.Visible = p.Layout.Elements.Count != 0; preview.Visible = !layoutPreview.Visible; layoutPreview.Invalidate();
            if(compiledShowPath != null) compiledShowPath = Path.GetFullPath(compiledShowPath);
            Text = "HLS-style Sequencer - " + p.Name + (dirty ? " *" : "") + " - Windows 7 target";
        }
        private string AudioPath()
        {
            string audio = session.Project.AudioFile;
            return Path.IsPathRooted(audio) ? audio : Path.Combine(fileName == null ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(fileName), audio);
        }
        private void ReloadWaveform() { try { timeline.Waveform = AudioClock.ReadWaveform(AudioPath(), 960); } catch { timeline.Waveform = new float[0]; } timeline.Invalidate(); }
        private void Tool(object sender, EventArgs e)
        {
            Guard(delegate
            {
                string button = ((Button)sender).Text;
                Selection selected = CurrentSelection();
                Project selectedProject = session.Project;
                if (button == "Custom" && (selected.Rows.Count == 0 || selected.Rows.TrueForAll(delegate(string id) { return Model.TargetKind(selectedProject, id) != "PixelPlane"; })))
                { OpenCustomEditor(selected); return; }
                if (button == "Custom" && selected.Rows.Exists(delegate(string id) { return Model.TargetKind(selectedProject, id) != "PixelPlane"; }))
                { Say("Select time-domain raw/Segment rows separately from PixelPlane rows for Custom placement."); return; }
                if (timeline.FirstClickMs < 0 || timeline.SecondClickMs < 0) { Say("Mark both corners of the grid rectangle first."); return; }
                Project p = session.Project; List<string> rows = timeline.VisibleRows();
                int low = Math.Min(timeline.FirstClickRow, timeline.SecondClickRow), high = Math.Max(timeline.FirstClickRow, timeline.SecondClickRow);
                int start = Math.Min(timeline.FirstClickMs, timeline.SecondClickMs), duration = Math.Abs(timeline.SecondClickMs - timeline.FirstClickMs) + p.FrameMs;
                string kind = ((Button)sender).Text.Replace("RampUP", "RampUp").Replace("RampDN", "RampDown");
                if (kind != "Level" && duration < p.FrameMs * 2) { Say("Select at least two output intervals for a ramp or custom effect."); return; }
                int added = 0;
                for (int row = low; row <= high && row < rows.Count; row++)
                {
                    string actual = kind;
                    if (Model.TargetKind(p, rows[row]) == "PixelPlane") { if (kind != "Custom") continue; actual = "PixelPlane"; }
                    p.Effects.Add(Model.NewEffect(p, rows[row], actual, start, duration, Model.HtmlColor(toolColor))); added++;
                }
                if (added == 0) { Say("PixelPlane rows use Custom spatial clips; basic Level/Ramp tools do not apply."); return; }
                session.Commit(p, "Added " + kind + " to " + added + " rows");
            });
        }
        public Selection CurrentSelection()
        {
            Selection selected = new Selection();
            if (timeline.FirstClickMs < 0 || timeline.SecondClickMs < 0) return selected;
            List<string> rows = timeline.VisibleRows(); int low = Math.Min(timeline.FirstClickRow, timeline.SecondClickRow), high = Math.Max(timeline.FirstClickRow, timeline.SecondClickRow);
            if (low < 0 || high >= rows.Count) return selected;
            for (int row = low; row <= high; row++) selected.Rows.Add(rows[row]);
            selected.StartMs = Math.Min(timeline.FirstClickMs, timeline.SecondClickMs);
            selected.DurationMs = Math.Abs(timeline.SecondClickMs - timeline.FirstClickMs) + session.Project.FrameMs;
            return selected;
        }
        private void OpenCustomEditor(Selection selected)
        {
            StopPreview(false);
            using (CustomEffectsDialog dialog = new CustomEffectsDialog(session.Project, selected, toolColor))
                if (dialog.ShowDialog(this) == DialogResult.OK) Guard(delegate { session.Commit(dialog.Result, dialog.PlacementRequested ? "Placed serial custom effect" : "Custom effect definitions"); });
        }
        private void ManageLibrary(object sender, EventArgs e)
        {
            StopPreview(false);
            using (EffectLibraryDialog dialog = new EffectLibraryDialog(session.Project, CurrentSelection()))
            {
                if (lastLibraryPath != null && File.Exists(lastLibraryPath))
                    try { dialog.OpenLibrary(lastLibraryPath); } catch (Exception error) { Say("Previous library could not be reopened: " + error.Message); }
                DialogResult answer = dialog.ShowDialog(this);
                lastLibraryPath = dialog.CurrentPath;
                if (answer != DialogResult.OK) return;
                if (dialog.ProjectChanged) Guard(delegate { session.Commit(dialog.Result, "Import custom effect definition"); });
                if (dialog.PendingInsert != null && MessageBox.Show(this, "Insert this library effect group? Next click chooses its upper-left grid cell.", "Insert library effect", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Guard(delegate { ArmLibraryInsertion(dialog.PendingInsert); });
            }
        }
        public void ArmLibraryInsertion(LibraryItem item)
        {
            if (item == null || item.Kind != "Group") throw new ArgumentException("Select an effect group; import a custom definition before placing it with Custom.");
            EffectLibrary shell = EffectLibraries.Empty("Pending insertion"); shell.Items.Add(item); EffectLibraries.RequireValid(shell);
            pendingLibraryItem = EffectLibraries.Clone(shell).Items[0]; timeline.PickingLibraryAnchor = true; timeline.ClearSelection(); timeline.Focus();
            Say("Click the upper-left cell for the library effect group. Esc cancels. Existing wiring stays unchanged.");
        }
        public void InsertPendingAt(int row, int timeMs)
        {
            if (pendingLibraryItem == null) throw new ArgumentException("Choose a library group first.");
            Project inserted = EffectLibraries.Insert(session.Project, pendingLibraryItem, timeline.VisibleRows(), row, timeMs);
            CancelLibraryPick(); session.Commit(inserted, "Insert library effect group");
        }
        private void CancelLibraryPick() { pendingLibraryItem = null; timeline.PickingLibraryAnchor = false; }
        private void Context(string id, Point location)
        {
            Effect effect = session.Project.Effects.Find(delegate(Effect f) { return f.Id == id; }); if (effect == null) return;
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Closed += delegate { menu.Dispose(); };
            menu.Items.Add(effect.Kind == "PixelPlane" ? "Edit timing" : "Modify effect", null, delegate { Edit(new BasicEffectEditor(session.Project, id), "Effect timing/value"); });
            if (effect.Kind == "PixelPlane") menu.Items.Add("Edit painted frames", null, delegate { Edit(new SpatialEffectEditor(session.Project, id), "Spatial frames"); });
            menu.Items.Add("Delete", null, delegate { Guard(delegate { Project p = session.Project; p.Effects.RemoveAll(delegate(Effect f) { return f.Id == id; }); session.Commit(p, "Delete effect"); }); }); menu.Show(timeline, location);
        }
        private void EditSpatial(object sender, EventArgs e)
        {
            string id = timeline.SelectedEffectId; Effect f = session.Project.Effects.Find(delegate(Effect effect) { return effect.Id == id; });
            if (f != null && f.Kind == "PixelPlane") Edit(new SpatialEffectEditor(session.Project, id), "Painted spatial clip"); else Say("Select a PixelPlane clip, then Edit spatial. Create one with Custom on a PixelPlane row.");
        }
        private void Edit(ProjectDialog dialog, string description)
        {
            using(dialog) Guard(delegate { StopPreview(false); if(dialog.ShowDialog(this) == DialogResult.OK) { session.Commit(dialog.Result, description); if(dialog is PreviewLayoutDialog) Say("Preview saved/compiled. Identify its Preview Control File before playback."); } });
        }
        private void RevealRaw(string id)
        {
            Project p = session.Project; RawChannel raw = p.Channels.Find(delegate(RawChannel c) { return c.Id == id; }); if (raw == null) return;
            string reveal=id;
            if(!timeline.VisibleRows().Contains(id))
            {
                DisplayGroup group=p.Groups.Find(delegate(DisplayGroup g){return g.Members.Contains(id);});
                if(group==null)
                    foreach(DisplayGroup candidate in p.Groups)
                    {
                        foreach(string target in candidate.Members)
                        {
                            Segment segment=p.Segments.Find(delegate(Segment x){return x.Id==target;});PixelPlane plane=p.PixelPlanes.Find(delegate(PixelPlane x){return x.Id==target;});
                            if((segment!=null&&segment.Members.Contains(id))||(plane!=null&&plane.Cells.Contains(id))){group=candidate;reveal=target;break;}
                        }
                        if(group!=null)break;
                    }
                if(group!=null){activeGroup=group.Id;RefreshUi();}
            }
            timeline.RevealRow(reveal); List<string> routes = new List<string>(); foreach(Controller route in p.Output.Controllers) if(route.Enabled) { int offset = (raw.Universe - route.FirstUniverse) * 512 + raw.Address - route.FirstAddress; if((route.Protocol == "E131" && raw.Universe >= route.FirstUniverse && raw.Universe < route.FirstUniverse + route.UniverseCount) || (route.Protocol == "Renard" && offset >= 0 && offset + Model.ChannelWidth(raw) <= route.ChannelCount)) routes.Add(route.Name); }
            Say(raw.Name + " | universe " + raw.Universe + " / address " + raw.Address + " | " + raw.Kind + (timeline.VisibleRows().Contains(reveal)?"":" | not assigned to a work-group row") + " | controllers: " + (routes.Count == 0 ? "none covering these slots" : String.Join(", ", routes.ToArray())));
        }
        private void Preview(object sender, EventArgs e)
        {
            Guard(delegate
            {
                bool armed = physicalOutput.Checked; StopPreview(false);
                Project snapshot = session.Project; playbackRenderer = Model.CreateFrameRenderer(snapshot);
                try
                {
                    clock.Play(AudioPath());
                    if(armed)
                    {
                        Func<int,byte[]> render = playbackRenderer;
                        OutputSession output = new OutputSession(snapshot, new NativeOutputTransport());
                        outputPump = new OutputPump(output, delegate { return clock.PositionMs; }, delegate(int time) { return render(time); }, snapshot.Output.IntervalMs);
                        outputPump.Start(true);
                    }
                    physicalOutput.Checked = armed; physicalOutput.Enabled = false; timeline.Playing = true; timer.Start();
                    Say(armed ? "Audio-clock playback; controller output armed for this session. Stop requests blackout." : "Audio-device-clock preview. Controller output is off.");
                }
                catch { StopPreview(false); throw; }
            });
        }
        private void Tick(object sender, EventArgs e)
        {
            try
            {
                if(outputPump != null && !outputPump.IsRunning) { string report = outputPump.LastError; StopPreview(false); Say("Controller output stopped: " + report); return; }
                if(!clock.IsPlaying || clock.PositionMs >= timeline.Project.DurationMs) { StopPreview(false); return; }
                int time = clock.PositionMs; timeline.PlayheadMs = time;
                byte[] frame = playbackRenderer(time); preview.Frame = frame; layoutPreview.Frame = frame;
                position.Text = TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
                timeline.Invalidate(); preview.Invalidate(); layoutPreview.Invalidate();
            }
            catch(Exception error) { try { StopPreview(false); } catch(Exception stopError) { Say("Output is still stopping: " + stopError.Message); return; } Say("Preview stopped: " + error.Message); }
        }
        private void StopPreview(bool announce)
        {
            string failure = null;
            if(outputPump != null)
            {
                outputPump.Stop(); // A timeout keeps the pump/audio alive for a safe retry.
                failure = String.IsNullOrEmpty(outputPump.LastError) ? null : outputPump.LastError;
                outputPump.Dispose(); outputPump = null;
            }
            try { clock.Stop(); } catch(Exception error) { failure = error.Message; }
            finally
            {
                timer.Stop(); timeline.Playing = false; timeline.PlayheadMs = 0; position.Text = "00:00.000"; physicalOutput.Checked = false; physicalOutput.Enabled = true; playbackRenderer = null;
                preview.Frame = null; layoutPreview.Frame = null; timeline.Invalidate(); preview.Invalidate(); layoutPreview.Invalidate();
            }
            if(failure != null) Say("Stop report: " + failure); else if(announce) Say("Stopped. Preview is dark; output blackout requested and physical arm reset.");
        }
        private void ShowReport(string title, string text)
        {
            using(Form report = new Form { Text = title, Size = new Size(850,550), StartPosition = FormStartPosition.CenterParent })
            { report.Controls.Add(new TextBox { Text = text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill }); report.ShowDialog(this); }
        }
        private void CompilePreview(object sender, EventArgs e)
        {
            Guard(delegate { StopPreview(false); using(SaveFileDialog save = new SaveFileDialog { Filter = "Preview Control File (*.hlsrpreview)|*.hlsrpreview" })
                if(save.ShowDialog(this) == DialogResult.OK) { session.Commit(PreviewLayouts.Compile(session.Project, save.FileName), "Save compiled preview control"); Say("Preview Control File saved. Use Preview > Identify Preview Control File to activate it."); } });
        }
        private void IdentifyPreview(object sender, EventArgs e)
        {
            Guard(delegate { StopPreview(false); using(OpenFileDialog open = new OpenFileDialog { Filter = "Preview Control File (*.hlsrpreview)|*.hlsrpreview" })
                if(open.ShowDialog(this) == DialogResult.OK) { session.Commit(PreviewLayouts.LoadInto(session.Project, open.FileName), "Identify preview control"); Say("Preview identified; assignments matched by raw channel names. Physical output map retained."); } });
        }
        private void CompileShow(object sender, EventArgs e)
        {
            Guard(delegate { StopPreview(false); using(SaveFileDialog save = new SaveFileDialog { Filter = "Rendered show data (*.fseq)|*.fseq" })
                if(save.ShowDialog(this) == DialogResult.OK) { try { compiledShowPath = CompiledShowIO.Compile(session.Project, save.FileName); } catch { showStatus.Text = "Show: compile failed — check data"; throw; } showStatus.Text = "Show: current"; Say("Compiled FSEQ + controller manifest. Playback data is current; no controller transmission started."); } });
        }
        private void CheckShow(object sender, EventArgs e)
        {
            Guard(delegate { StopPreview(false); using(OpenFileDialog open = new OpenFileDialog { Filter = "Compiled show manifest (*.hlsrshow)|*.hlsrshow" })
                if(open.ShowDialog(this) == DialogResult.OK) { compiledShowPath = open.FileName; bool current = CompiledShowIO.IsCurrent(session.Project, compiledShowPath); showStatus.Text = current ? "Show: current" : "Show: stale / missing"; Say(current ? "Compiled show data is current." : "Compiled show data is missing, modified or outdated. Compile for Show again."); } });
        }
        private void Guard(Action action) { try { action(); } catch (Exception error) { Say(error.Message); MessageBox.Show(this, error.Message, "HLS-style Sequencer", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
        private void Say(string text) { status.Text = text; }
        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            DialogResult answer = MessageBox.Show(this, "Save changes before leaving this sequence?", "Unsaved sequence", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return answer == DialogResult.No || (answer == DialogResult.Yes && SaveCurrent());
        }
        private void ReplaceProject(Project p, string path)
        {
            StopPreview(false); session.Changed -= SessionChanged; session = new EditSession(ResolveAudioReference(p, path)); session.Changed += SessionChanged;
            fileName = path; activeGroup = null; dirty = false; compiledShowPath = null; showStatus.Text = "Show: not compiled"; RefreshUi(); ReloadWaveform();
        }
        public void LoadProject(string path) { ReplaceProject(ProjectIO.Load(path), Path.GetFullPath(path)); }
        public void SaveProjectTo(string path)
        {
            Project candidate = session.Project; string full = Path.GetFullPath(path), audio = AudioPath();
            string sibling = Path.Combine(Path.GetDirectoryName(full), Path.GetFileName(audio));
            candidate.AudioFile = String.Equals(audio, sibling, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(audio) : audio;
            ProjectIO.Save(candidate, full); fileName = full; dirty = false; RefreshUi();
        }
        private static Project ResolveAudioReference(Project p, string path)
        {
            Project copy = Model.Clone(p);
            string directory = path == null ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(Path.GetFullPath(path));
            if(!String.IsNullOrEmpty(copy.Layout.BackgroundImage) && !Path.IsPathRooted(copy.Layout.BackgroundImage)) copy.Layout.BackgroundImage = Path.GetFullPath(Path.Combine(directory,copy.Layout.BackgroundImage));
            if(!String.IsNullOrEmpty(copy.Layout.ControlFile) && !Path.IsPathRooted(copy.Layout.ControlFile)) copy.Layout.ControlFile = Path.GetFullPath(Path.Combine(directory,copy.Layout.ControlFile));
            if (!Path.IsPathRooted(copy.AudioFile)) copy.AudioFile = Path.GetFullPath(Path.Combine(path == null ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(Path.GetFullPath(path)), copy.AudioFile));
            return copy;
        }
        public void SetFrameInterval(int milliseconds) { Project p = session.Project; p.FrameMs = milliseconds; session.Commit(p, "Time resolution " + milliseconds + "ms"); }
        private void Open(object sender, EventArgs e) { if (!ConfirmDiscard()) return; using (OpenFileDialog dialog = new OpenFileDialog()) { dialog.Filter = "HLS-style projects (*.hlsr)|*.hlsr"; if (dialog.ShowDialog(this) == DialogResult.OK) Guard(delegate { LoadProject(dialog.FileName); }); } }
        private bool SaveCurrent() { if (String.IsNullOrEmpty(fileName)) return SaveAs(); try { SaveProjectTo(fileName); return true; } catch (Exception error) { Say("Save failed: " + error.Message); return false; } }
        private bool SaveAs() { using (SaveFileDialog dialog = new SaveFileDialog()) { dialog.Filter = "HLS-style projects (*.hlsr)|*.hlsr"; if (dialog.ShowDialog(this) != DialogResult.OK) return false; try { SaveProjectTo(dialog.FileName); return true; } catch (Exception error) { Say("Save failed: " + error.Message); return false; } } }
        private void Export(object sender, EventArgs e) { using (SaveFileDialog dialog = new SaveFileDialog()) { dialog.Filter = "Rendered FSEQ (*.fseq)|*.fseq"; if (dialog.ShowDialog(this) == DialogResult.OK) Guard(delegate { Model.ExportFseq(session.Project, dialog.FileName); Say("Exported rendered FSEQ values. Editable project stays in .hlsr."); }); } }
        private void RestoreRecovery(object sender, EventArgs e) { if (!ConfirmDiscard()) return; Guard(delegate { ReplaceProject(ProjectIO.Load(RecoveryPath()), null); dirty = true; Say("Recovered sequence. Save As to keep a project copy."); }); }
        private void ChooseAudio(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "16-bit PCM WAV (*.wav)|*.wav"; if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Guard(delegate { int length = AudioClock.ReadDurationMs(dialog.FileName); Project p = session.Project; int aligned = ((length + p.FrameMs - 1) / p.FrameMs) * p.FrameMs; foreach (Effect f in p.Effects) if (f.StartMs + f.DurationMs > aligned) throw new ArgumentException("This audio ends before existing effects. Trim those effects before replacing the audio."); p.AudioFile = dialog.FileName; p.DurationMs = aligned; session.Commit(p, "Audio file"); });
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !ConfirmDiscard()) e.Cancel = true; if (!e.Cancel) try { StopPreview(false); } catch(Exception error) { e.Cancel = true; Say(error.Message); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { StopPreview(false); timer.Stop(); timer.Dispose(); try { clock.Dispose(); } catch { } if (session != null) session.Changed -= SessionChanged; } base.Dispose(disposing); }
    }

    public sealed class RawSelectedEventArgs : EventArgs { public string Id; }
    public sealed class LightPreview : ScrollableControl
    {
        public Project Project; public byte[] Frame; public event EventHandler<RawSelectedEventArgs> ChannelSelected;
        private readonly Dictionary<string, Rectangle> boxes = new Dictionary<string, Rectangle>();
        public IList<string> VisibleChannelIds { get { return new List<string>(boxes.Keys); } }
        public LightPreview() { DoubleBuffered = true; BackColor = Color.FromArgb(28, 28, 28); AutoScroll = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); boxes.Clear(); e.Graphics.Clear(BackColor);
            if (Project == null) return;
            // Reserve the scrollbar once using outer width, so showing the bar does
            // not change column count and invalidate an end-of-list scroll target.
            int available = Math.Max(40, Width - SystemInformation.VerticalScrollBarWidth);
            int cols = Math.Max(1, available / 75), width = Math.Max(35, available / cols);
            Size size = new Size(available, 62 + ((Project.Channels.Count + cols - 1) / cols) * 66 + 30);
            if (AutoScrollMinSize != size) AutoScrollMinSize = size;
            int firstRow = Math.Max(0, (-AutoScrollPosition.Y - 62) / 66);
            int end = Math.Min(Project.Channels.Count, (firstRow + ClientSize.Height / 66 + 3) * cols);
            for (int i = firstRow * cols; i < end; i++)
            {
                RawChannel c = Project.Channels[i]; int x = (i % cols) * width + 6 + AutoScrollPosition.X, y = 62 + (i / cols) * 66 + AutoScrollPosition.Y;
                Color color = Color.Black; int offset = Model.OutputOffset(c);
                if (Frame != null && offset >= 0 && offset < Frame.Length)
                    color = c.Kind == "RGB" && offset + 2 < Frame.Length ? Color.FromArgb(Frame[offset], Frame[offset + 1], Frame[offset + 2]) : Color.FromArgb(Frame[offset], Frame[offset], Frame[offset]);
                Rectangle box = new Rectangle(x, y, width - 12, 26); boxes[c.Id] = box;
                using (Brush brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush, box); e.Graphics.DrawRectangle(Pens.DimGray, box);
                e.Graphics.DrawString(c.Name, Font, Brushes.White, new RectangleF(x, y + 29, width - 8, 16));
                e.Graphics.DrawString("U" + c.Universe + ":" + c.Address, Font, Brushes.Silver, x, y + 44);
            }
            using (Brush background = new SolidBrush(BackColor)) { e.Graphics.FillRectangle(background, 0, 0, ClientSize.Width, 52); e.Graphics.FillRectangle(background, 0, ClientSize.Height - 24, ClientSize.Width, 24); }
            e.Graphics.DrawString("Display preview / output mapping", Font, Brushes.White, 8, 10);
            e.Graphics.DrawString("Select a light to identify its raw row.", Font, Brushes.Silver, 8, 29);
            e.Graphics.DrawString("Preview only - no network output", Font, Brushes.Gainsboro, 8, Height - 24);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Y < 52 || e.Y > ClientSize.Height - 24) return; foreach (KeyValuePair<string, Rectangle> box in boxes) if (box.Value.Contains(e.Location)) { EventHandler<RawSelectedEventArgs> callback = ChannelSelected; if (callback != null) callback(this, new RawSelectedEventArgs { Id = box.Key }); break; } }
    }
    public sealed class BasicEffectEditor : ProjectDialog
    {
        private readonly Effect effect;
        private readonly NumericUpDown start = new NumericUpDown(), duration = new NumericUpDown(), level = new NumericUpDown();
        private readonly NumericUpDown rampStart = new NumericUpDown(), rampEnd = new NumericUpDown();
        private readonly TextBox color = new TextBox(), curve = new TextBox();
        private readonly ComboBox kind = new ComboBox();
        private readonly string originalKind;
        private readonly bool originalEndpointMode;
        private readonly decimal initialRampStart, initialRampEnd;
        public BasicEffectEditor(Project p, string id) : base(p)
        {
            effect = working.Effects.Find(delegate(Effect f) { return f.Id == id; }); if (effect == null) throw new ArgumentException("Effect does not exist.");
            originalKind = effect.Kind; originalEndpointMode = effect.UseEndpoints;
            Text = effect.Kind == "PixelPlane" ? "Spatial clip timing" : "Modify effect";
            TableLayoutPanel fields = new TableLayoutPanel(); fields.Dock = DockStyle.Fill; fields.ColumnCount = 2; fields.Padding = new Padding(12);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            start.Maximum = p.DurationMs; start.Value = effect.StartMs; duration.Minimum = 1; duration.Maximum = p.DurationMs; duration.Value = effect.DurationMs;
            level.DecimalPlaces = 2; level.Increment = .05M; level.Maximum = 1; level.Value = (decimal)effect.Level;
            rampStart.DecimalPlaces = rampEnd.DecimalPlaces = 2; rampStart.Maximum = rampEnd.Maximum = 100;
            rampStart.Value = (decimal)((effect.UseEndpoints ? effect.StartLevel : effect.Kind == "RampDown" ? 1 : 0) * effect.Level * 100);
            rampEnd.Value = (decimal)((effect.UseEndpoints ? effect.EndLevel : effect.Kind == "RampDown" ? 0 : 1) * effect.Level * 100);
            initialRampStart = rampStart.Value; initialRampEnd = rampEnd.Value;
            kind.DropDownStyle = ComboBoxStyle.DropDownList; kind.Items.AddRange(new object[] { "Level", "RampUp", "RampDown", "Custom" }); kind.SelectedItem = effect.Kind;
            EventHandler endpoints = delegate { string value = Convert.ToString(kind.SelectedItem); bool ramp = value == "RampUp" || value == "RampDown"; rampStart.Enabled = rampEnd.Enabled = ramp; level.Enabled = !ramp; };
            kind.SelectedIndexChanged += endpoints; endpoints(this, EventArgs.Empty);
            color.Text = effect.ColorHtml; curve.Multiline = true; curve.Height = 100; curve.Text = CurveText(effect.Curve);
            AddField(fields, "Start (milliseconds)", start); AddField(fields, "Duration (milliseconds)", duration);
            if (effect.Kind != "PixelPlane") { AddField(fields, "Type", kind); AddField(fields, "Intensity (Level / curve)", level); AddField(fields, "Ramp start intensity %", rampStart); AddField(fields, "Ramp end intensity %", rampEnd); AddField(fields, "Color #RRGGBB", color); AddField(fields, "Custom curve: time:value; ...", curve); }
            else AddField(fields, "Spatial cells / speed / fades", new Label { Text = "Use Edit painted frames for spatial content.", AutoSize = true });
            Controls.Add(fields); Controls.Add(Buttons());
        }
        private static void AddField(TableLayoutPanel panel, string text, Control control) { int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.Absolute, control is TextBox && ((TextBox)control).Multiline ? 120 : 36)); panel.Controls.Add(new Label { Text = text, AutoSize = true }, 0, row); control.Dock = DockStyle.Fill; panel.Controls.Add(control, 1, row); }
        private static string CurveText(List<CurvePoint> points) { List<string> values = new List<string>(); foreach (CurvePoint point in points) values.Add(point.Position.ToString(CultureInfo.InvariantCulture) + ":" + point.Value.ToString(CultureInfo.InvariantCulture)); return String.Join("; ", values.ToArray()); }
        protected override void Accept()
        {
            effect.StartMs = (int)start.Value; effect.DurationMs = (int)duration.Value;
            if (effect.Kind != "PixelPlane")
            {
                effect.Kind = Convert.ToString(kind.SelectedItem); effect.Level = (double)level.Value; effect.ColorHtml = color.Text.Trim(); effect.Curve.Clear();
                bool legacyUnchanged = !originalEndpointMode && originalKind == effect.Kind && rampStart.Value == initialRampStart && rampEnd.Value == initialRampEnd;
                effect.UseEndpoints = (effect.Kind == "RampUp" || effect.Kind == "RampDown") && !legacyUnchanged;
                effect.StartLevel = effect.UseEndpoints ? (double)rampStart.Value / 100 : 0;
                effect.EndLevel = effect.UseEndpoints ? (double)rampEnd.Value / 100 : 1;
                if (effect.UseEndpoints) effect.Level = 1;
                if (effect.Kind == "Custom") foreach (string item in curve.Text.Split(';')) { string[] values = item.Trim().Split(':'); double x, y; if (values.Length != 2 || !Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !Double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) throw new ArgumentException("Use normalized curve points, for example 0:0; 0.5:1; 1:0."); effect.Curve.Add(new CurvePoint { Position = x, Value = y }); }
            }
            base.Accept();
        }
    }
}
