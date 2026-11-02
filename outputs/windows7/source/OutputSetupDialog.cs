using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.IO.Ports;

namespace HlsRecreation
{
    public sealed class OutputSetupDialog : ProjectDialog
    {
        public DataGridView ControllerGrid { get; private set; }
        public readonly TextBox PlanText = new TextBox();
        public readonly NumericUpDown Interval = new NumericUpDown();
        private bool binding;

        public OutputSetupDialog(Project project) : base(project)
        {
            Text = "Select Output Method and Prepare for Show"; Width = 1160; Height = 680; MinimumSize = new Size(1024, 600);
            ControllerGrid = new DataGridView { Dock = DockStyle.Fill, Name = "controllerGrid", AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter };
            ControllerGrid.DataError += delegate { };
            AddCheck("Enabled", "Enabled", 55); AddText("Name", "Controller name", 125); AddCombo("Protocol", "Protocol", new string[] { "E131", "Renard" }, 72); AddText("Address", "Unicast IPv4", 104); AddText("LocalAddress", "NIC IPv4 (optional)", 115); AddCheck("Multicast", "Multicast", 66); AddInt("FirstUniverse", "First U", 53); AddInt("UniverseCount", "U count", 55); AddInt("FirstAddress", "First addr", 62); AddInt("ChannelCount", "Bytes", 54); AddInt("Priority", "Priority", 54); AddInt("Port", "UDP", 52); AddText("ComPort", "COM", 55); AddInt("BaudRate", "Baud", 68);
            ControllerGrid.CellValueChanged += delegate { if (!binding) UpdatePlanHint(); };
            ControllerGrid.CurrentCellDirtyStateChanged += delegate { if (ControllerGrid.IsCurrentCellDirty) ControllerGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false, AutoScroll = true, Padding = new Padding(5) };
            top.Controls.Add(B("New E1.31", delegate { FlushControllers(); working.Output.Controllers.Add(NewController("E131")); Bind(); }));
            top.Controls.Add(B("New Renard", delegate { FlushControllers(); working.Output.Controllers.Add(NewController("Renard")); Bind(); }));
            top.Controls.Add(B("Delete", delegate { FlushControllers(); if (ControllerGrid.CurrentRow != null) { string id = Convert.ToString(ControllerGrid.CurrentRow.Cells["Id"].Value); working.Output.Controllers.RemoveAll(delegate(Controller x) { return x.Id == id; }); Bind(); } }));
            top.Controls.Add(B("Prepare channels", delegate { OutputPlan plan = PrepareCurrent(); PlanText.Text = Format(plan); }));
            top.Controls.Add(new Label { Text = "Frame interval", AutoSize = true, Margin = new Padding(12, 7, 1, 0) }); Interval.Minimum = 23; Interval.Maximum = 60000; Interval.Width = 58; top.Controls.Add(Interval); top.Controls.Add(new Label { Text = "ms", AutoSize = true, Margin = new Padding(1, 7, 1, 0) });
            top.Controls.Add(B("Choose NIC...", delegate { ChooseEndpoint(false); }));
            top.Controls.Add(B("Choose COM...", delegate { ChooseEndpoint(true); }));
            PlanText.Dock = DockStyle.Bottom; PlanText.Multiline = true; PlanText.ReadOnly = true; PlanText.ScrollBars = ScrollBars.Vertical; PlanText.Height = 145; PlanText.BackColor = SystemColors.Window; PlanText.Name = "outputPlan";
            Controls.Add(ControllerGrid); Controls.Add(PlanText); Controls.Add(top); Controls.Add(Buttons()); Bind();
        }
        private sealed class EndpointChoice
        {
            public string Label,Value;
            public override string ToString(){return Label;}
        }
        private void ChooseEndpoint(bool serial)
        {
            if(ControllerGrid.CurrentRow==null)throw new ArgumentException("Select an output controller row first.");
            using(Form pick=new Form{Text=serial?"Select COM port":"Select output network interface",Size=new Size(520,180),StartPosition=FormStartPosition.CenterParent})
            using(ComboBox choices=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList})
            {
                if(serial)foreach(string port in SerialPort.GetPortNames())choices.Items.Add(new EndpointChoice{Label=port,Value=port});
                else
                {
                    choices.Items.Add(new EndpointChoice{Label="System route (no specific interface)",Value=""});
                    foreach(NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                        foreach(UnicastIPAddressInformation item in nic.GetIPProperties().UnicastAddresses)
                            if(item.Address.AddressFamily==AddressFamily.InterNetwork)choices.Items.Add(new EndpointChoice{Label=nic.Name+" — "+item.Address,Value=item.Address.ToString()});
                }
                if(choices.Items.Count==0)throw new ArgumentException("No COM ports were found. You can enter a known port name in the grid.");
                choices.SelectedIndex=0;Button ok=new Button{Text="Use selection",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};
                pick.Controls.Add(choices);pick.Controls.Add(ok);pick.AcceptButton=ok;
                if(pick.ShowDialog(this)==DialogResult.OK)ControllerGrid.CurrentRow.Cells[serial?"ComPort":"LocalAddress"].Value=((EndpointChoice)choices.SelectedItem).Value;
            }
        }
        private Controller NewController(string protocol)
        {
            string prefix = protocol == "E131" ? "E1.31 controller" : "Renard controller"; int n = 1; string name = prefix; while(working.Output.Controllers.Exists(delegate(Controller c) { return c.Name == name; })) { n++; name = prefix + " " + n; }
            return new Controller { Id = Model.NewId(), Name = name, Protocol = protocol, Enabled = false, FirstUniverse = 1, UniverseCount = 1, FirstAddress = 1, ChannelCount = 24, Address = "127.0.0.1", ComPort = "COM1", Port = 5568, Priority = 100, BaudRate = 57600 };
        }
        private void AddText(string name, string header, int width) { ControllerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width }); }
        private void AddInt(string name, string header, int width) { AddText(name, header, width); }
        private void AddCheck(string name, string header, int width) { ControllerGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = name, HeaderText = header, Width = width }); }
        private void AddCombo(string name, string header, string[] values, int width) { DataGridViewComboBoxColumn col = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, Width = width }; col.Items.AddRange(values); ControllerGrid.Columns.Add(col); }
        private void Bind()
        {
            binding = true; ControllerGrid.Rows.Clear(); Interval.Value = Math.Max(Interval.Minimum, Math.Min(Interval.Maximum, working.Output.IntervalMs));
            foreach (Controller c in working.Output.Controllers) ControllerGrid.Rows.Add(c.Enabled, c.Name, c.Protocol, c.Address, c.LocalAddress, c.Multicast, c.FirstUniverse, c.UniverseCount, c.FirstAddress, c.ChannelCount, c.Priority, c.Port, c.ComPort, c.BaudRate);
            // A hidden identity cell avoids exposing mutable infrastructure identity to normal editing.
            if (ControllerGrid.Columns["Id"] == null) { DataGridViewTextBoxColumn id = new DataGridViewTextBoxColumn { Name = "Id", Visible = false }; ControllerGrid.Columns.Add(id); for (int i = 0; i < ControllerGrid.Rows.Count; i++) ControllerGrid.Rows[i].Cells["Id"].Value = working.Output.Controllers[i].Id; }
            else for (int i = 0; i < ControllerGrid.Rows.Count; i++) ControllerGrid.Rows[i].Cells["Id"].Value = working.Output.Controllers[i].Id;
            binding = false; UpdatePlanHint();
        }
        private static int Integer(object value, string label)
        {
            int n; if (!Int32.TryParse(Convert.ToString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) throw new ArgumentException(label + " must be a whole number."); return n;
        }
        public void FlushControllers()
        {
            if (!ControllerGrid.EndEdit()) throw new ArgumentException("Complete the current controller cell edit first.");
            Project candidate = Model.Clone(working); candidate.Output.IntervalMs = (int)Interval.Value;
            List<Controller> values = new List<Controller>();
            foreach (DataGridViewRow row in ControllerGrid.Rows)
            {
                if (row.IsNewRow) continue; string id = Convert.ToString(row.Cells["Id"].Value); Controller prior = candidate.Output.Controllers.Find(delegate(Controller c) { return c.Id == id; });
                Controller controller = prior == null ? NewController("E131") : prior;
                controller.Id = id; controller.Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value); controller.Name = Convert.ToString(row.Cells["Name"].Value).Trim(); controller.Protocol = Convert.ToString(row.Cells["Protocol"].Value);
                controller.Address = Convert.ToString(row.Cells["Address"].Value).Trim(); controller.LocalAddress = Convert.ToString(row.Cells["LocalAddress"].Value).Trim(); controller.Multicast = Convert.ToBoolean(row.Cells["Multicast"].Value);
                controller.FirstUniverse = Integer(row.Cells["FirstUniverse"].Value, "First universe"); controller.UniverseCount = Integer(row.Cells["UniverseCount"].Value, "Universe count"); controller.FirstAddress = Integer(row.Cells["FirstAddress"].Value, "First address"); controller.ChannelCount = Integer(row.Cells["ChannelCount"].Value, "Channel count");
                controller.Priority = Integer(row.Cells["Priority"].Value, "Priority"); controller.Port = Integer(row.Cells["Port"].Value, "UDP port"); controller.ComPort = Convert.ToString(row.Cells["ComPort"].Value).Trim(); controller.BaudRate = Integer(row.Cells["BaudRate"].Value, "Baud rate"); values.Add(controller);
            }
            candidate.Output.Controllers = values; List<string> errors = ControllerOutput.Validate(candidate.Output); if (errors.Count != 0) throw new ArgumentException(String.Join(" ", errors.ToArray())); working = candidate;
        }
        public OutputPlan PrepareCurrent() { FlushControllers(); return ControllerOutput.Prepare(working); }
        private void UpdatePlanHint()
        {
            if (binding) return;
            try { OutputPlan plan = PrepareCurrent(); PlanText.Text = Format(plan); }
            catch (Exception error) { PlanText.Text = "Controller edits pending: " + error.Message; }
        }
        private static string Format(OutputPlan plan)
        {
            if (plan == null) return "No output plan."; List<string> all = new List<string>(); if (!String.IsNullOrEmpty(plan.Summary)) all.Add(plan.Summary); if (plan.Errors != null) all.AddRange(plan.Errors); if (plan.Warnings != null) all.AddRange(plan.Warnings); return String.Join(Environment.NewLine, all.ToArray());
        }
        protected override void Accept() { FlushControllers(); OutputPlan plan = ControllerOutput.Prepare(working); if (plan.Errors != null && plan.Errors.Count != 0) throw new ArgumentException(String.Join(" ", plan.Errors.ToArray())); base.Accept(); }
    }
}
