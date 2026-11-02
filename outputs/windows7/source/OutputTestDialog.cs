using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HlsRecreation
{
    // This is intentionally a transient test surface: it operates on a clone and never
    // changes controller setup, preview mappings, or the user's physical-arm preference.
    public sealed class OutputTestDialog : Form
    {
        private readonly Project project;
        public readonly CheckedListBox RawList = new CheckedListBox();
        public readonly ComboBox Mode = new ComboBox();
        public readonly NumericUpDown Level = new NumericUpDown();
        public readonly TextBox Color = new TextBox();
        public readonly CheckBox PhysicalArm = new CheckBox();
        public readonly Label Status = new Label();
        public readonly Button StartButton, StopButton, AllOffButton;
        public RecordingOutputTransport Recording { get; private set; }
        public OutputSession Session { get; private set; }
        public OutputPump Pump { get; private set; }
        private readonly Timer statusTimer = new Timer();
        private int startTick;

        public OutputTestDialog(Project source)
        {
            project = Model.Clone(source); Text = "Test Output Lights"; Width = 690; Height = 480; MinimumSize = new Size(600, 390); StartPosition = FormStartPosition.CenterParent;
            RawList.Dock = DockStyle.Fill; RawList.CheckOnClick = true; RawList.FormattingEnabled = true; RawList.Format += delegate(object sender, ListControlConvertEventArgs e) { RawChannel c = e.ListItem as RawChannel; e.Value = c == null ? "" : c.OutputOrder.ToString("0000") + "  " + c.Name + "  U" + c.Universe + ":" + c.Address; };
            List<RawChannel> raws = new List<RawChannel>(project.Channels); raws.Sort(delegate(RawChannel a, RawChannel b) { return a.OutputOrder.CompareTo(b.OutputOrder); }); foreach (RawChannel raw in raws) RawList.Items.Add(raw, false);
            Mode.DropDownStyle = ComboBoxStyle.DropDownList; Mode.Items.AddRange(new object[] { "Solid", "Ramp", "Chase" }); Mode.SelectedIndex = 0; Mode.Width = 90;
            Level.Minimum = 0; Level.Maximum = 100; Level.Value = 100; Level.Width = 54; Color.Text = "#FFFFFF"; Color.Width = 80;
            PhysicalArm.Text = "Enable physical output for this session"; PhysicalArm.AutoSize = true; PhysicalArm.Checked = false; PhysicalArm.Name = "physicalArm"; PhysicalArm.CheckedChanged += delegate { if(StartButton != null) StartButton.Text = PhysicalArm.Checked ? "Start physical output" : "Start (recording / dry run)"; };
            StartButton = new Button { Text = "Start (recording / dry run)", AutoSize = true, Name = "startOutputTest" }; StartButton.Click += delegate { try { StartTest(); } catch (Exception error) { Status.Text = error.Message; if (ProjectDialog.SuppressErrorDialogs) throw; MessageBox.Show(this, error.Message, Text); } };
            StopButton = new Button { Text = "Stop", AutoSize = true, Name = "stopOutputTest", Enabled = false }; StopButton.Click += delegate { StopTest(); };
            AllOffButton = new Button { Text = "All Off", AutoSize = true, Name = "allOffOutputTest" }; AllOffButton.Click += delegate { AllOff(); };
            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(7), WrapContents = true }; controls.Controls.Add(new Label { Text = "Mode", AutoSize = true, Margin = new Padding(2, 7, 1, 0) }); controls.Controls.Add(Mode); controls.Controls.Add(new Label { Text = "Level", AutoSize = true, Margin = new Padding(8, 7, 1, 0) }); controls.Controls.Add(Level); controls.Controls.Add(new Label { Text = "Color", AutoSize = true, Margin = new Padding(8, 7, 1, 0) }); controls.Controls.Add(Color); controls.Controls.Add(StartButton); controls.Controls.Add(StopButton); controls.Controls.Add(AllOffButton); controls.Controls.Add(PhysicalArm);
            Status.Dock = DockStyle.Bottom; Status.Height = 40; Status.BorderStyle = BorderStyle.Fixed3D; Status.Padding = new Padding(6); Status.Text = "Dry run selected: packets are recorded only. Check raw channels to test.";
            Controls.Add(RawList); Controls.Add(controls); Controls.Add(Status);
            statusTimer.Interval = 200; statusTimer.Tick += delegate { UpdateStatus(); }; FormClosing += delegate(object sender, FormClosingEventArgs e) { StopTest(); if(Pump != null) e.Cancel = true; }; Disposed += delegate { statusTimer.Dispose(); };
        }
        public List<string> SelectedRawIds()
        {
            List<string> ids = new List<string>(); foreach (object item in RawList.CheckedItems) { RawChannel c = item as RawChannel; if (c != null) ids.Add(c.Id); } return ids;
        }
        public void StartTest()
        {
            bool armed = PhysicalArm.Checked; StopTest(); if(Pump != null) throw new InvalidOperationException("Wait for the previous output worker to finish stopping."); Recording = null; List<string> ids = SelectedRawIds(); if (ids.Count == 0) throw new ArgumentException("Check one or more raw channels to test.");
            string pattern = Convert.ToString(Mode.SelectedItem), selectedColor = Color.Text.Trim(); int selectedLevel = (int)Level.Value;
            List<string> colorErrors = new List<string>(); try { Model.ParseColor(Color.Text.Trim()); } catch { colorErrors.Add("Use a color in #RRGGBB form."); } if (colorErrors.Count != 0) throw new ArgumentException(colorErrors[0]);
            // Normal test runs always use recording.  A physical session requires an explicit
            // checkbox and does not become a stored setting. Native transport is constructed
            // only at that point; its handles are lazy and no constructor opens a device.
            IOutputTransport transport;
            if (armed) transport = new NativeOutputTransport(); else { Recording = new RecordingOutputTransport(); transport = Recording; }
            Session = new OutputSession(project, transport); startTick = Environment.TickCount;
            Pump = new OutputPump(Session, delegate { return unchecked(Environment.TickCount - startTick); }, delegate(int elapsed) { return ControllerOutput.TestFrame(project, ids, pattern, elapsed, selectedLevel, selectedColor); }, project.Output.IntervalMs);
            try { Pump.Start(armed); PhysicalArm.Checked = armed; statusTimer.Start(); StartButton.Enabled = false; StopButton.Enabled = true; RawList.Enabled = Mode.Enabled = Level.Enabled = Color.Enabled = PhysicalArm.Enabled = false; UpdateStatus(); }
            catch { SafeDispose(); throw; }
        }
        public void StopTest()
        {
            statusTimer.Stop(); string failure = "";
            if(Pump != null)
            {
                try { Pump.Stop(); }
                catch(Exception error) { Status.Text = "Output is still stopping: " + error.Message; PhysicalArm.Checked = false; StopButton.Enabled = true; return; }
                if(!String.IsNullOrEmpty(Pump.LastError)) failure = Pump.LastError;
            }
            if(Session != null && !String.IsNullOrEmpty(Session.LastError)) failure = Session.LastError;
            try { SafeDispose(); } catch(Exception error) { failure = error.Message; }
            StartButton.Enabled = Pump == null; StopButton.Enabled = Pump != null; PhysicalArm.Checked = false;
            RawList.Enabled = Mode.Enabled = Level.Enabled = Color.Enabled = PhysicalArm.Enabled = Pump == null;
            Status.Text = failure.Length == 0 ? "Stopped; blackout requested and transport released. Physical arm reset." : "Output cleanup report: " + failure;
        }
        public void AllOff()
        {
            StopTest();
        }
        private int FrameLength()
        {
            int max = 0; foreach (RawChannel c in project.Channels) { int width = c.Kind == "RGB" ? 3 : 1; if (c.Universe > 0) max = Math.Max(max, (c.Universe - 1) * 512 + c.Address - 1 + width); } return Math.Max(1, max);
        }
        private void UpdateStatus()
        {
            if (Session == null) return; string kind = Recording == null ? "physical session (armed)" : "recording dry run"; Status.Text = kind + ": " + Session.FramesSent + " frames, " + Session.PacketsSent + " packets" + (String.IsNullOrEmpty(Session.LastError) ? "" : "; " + Session.LastError);
            if(Pump != null) Status.Text += "; " + Pump.SkippedFrames + " skipped ticks; " + (Session.FramesSent * 1000.0 / Math.Max(1, unchecked(Environment.TickCount-startTick))).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture) + " sent frames/s";
            if (Pump != null && !String.IsNullOrEmpty(Pump.LastError)) Status.Text += "; pump: " + Pump.LastError;
        }
        private void SafeDispose()
        {
            if(Pump != null) { Pump.Dispose(); Pump = null; }
            if(Session != null) { Session.Dispose(); Session = null; }
        }
        protected override void Dispose(bool disposing) { if (disposing) { StopTest(); if(Pump != null) throw new InvalidOperationException("Output worker is still stopping; close again after cleanup completes."); } base.Dispose(disposing); }
    }
}
