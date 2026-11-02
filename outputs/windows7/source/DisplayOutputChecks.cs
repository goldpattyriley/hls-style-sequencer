using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HlsRecreation
{
    public static class DisplayOutputChecks
    {
        private static Controller Route(string protocol)
        {
            return new Controller { Id=Model.NewId(), Name=protocol+" test", Protocol=protocol, Enabled=true, Address="127.0.0.1", FirstUniverse=1, UniverseCount=1, FirstAddress=1, ChannelCount=24, ComPort="COM77", BaudRate=57600 };
        }
        private static Project Small()
        {
            Project p=Model.Empty();p.Name="Handcrafted output check";p.DurationMs=1000;p.FrameMs=25;
            Model.AddRawChannels(p,3,"Hand RGB","RGB",1,1);
            foreach(RawChannel raw in p.Channels){p.Groups[0].Members.Add(raw.Id);p.Effects.Add(Model.NewEffect(p,raw.Id,"Level",0,1000,"#FF4010"));}
            p.Output.Controllers.Add(Route("E131"));
            PreviewElement line=Shape("Roof line","Polyline",3,1,1,.15,.4,.85,.4);line.Points.Insert(1,new LayoutPoint{X=.5,Y=.15});p.Layout.Elements.Add(line);
            return PreviewLayouts.Assign(p,line.Id,p.Channels.Select(delegate(RawChannel r){return r.Id;}).ToList(),false);
        }
        private static PreviewElement Shape(string name,string kind,int nodes,int group,int number,double x1,double y1,double x2,double y2)
        {return new PreviewElement{Id=Model.NewId(),Name=name,Kind=kind,NodeCount=nodes,Group=group,Number=number,Points=new List<LayoutPoint>{new LayoutPoint{X=x1,Y=y1},new LayoutPoint{X=x2,Y=y2}}};}
        private sealed class Spy:IOutputTransport
        {
            public bool Physical;public string FailId;public int Disposals,Writes;public List<OutputPacket> Packets=new List<OutputPacket>();
            public bool IsPhysical{get{return Physical;}}
            public void Write(OutputPacket p){Writes++;if(p.ControllerId==FailId)throw new IOException("Injected write failure");Packets.Add(p);}
            public void Dispose(){Disposals++;}
        }
        private static void Paint(Control c,string path,int width,int height)
        {c.Size=new Size(width,height);Checks.PrepareHiddenControl(c);using(Bitmap b=new Bitmap(width,height)){c.DrawToBitmap(b,new Rectangle(0,0,width,height));b.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
        private static void Mouse(Control c,string method,int x,int y,MouseButtons button)
        {typeof(Control).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{new MouseEventArgs(button,1,x,y,0)});}
        private static IEnumerable<Control> Walk(Control parent)
        { foreach(Control c in parent.Controls){yield return c;foreach(Control child in Walk(c))yield return child;} }
        private static void Press(Control root,string text)
        {Button button=Walk(root).OfType<Button>().First(delegate(Button b){return b.Text==text;});typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{EventArgs.Empty});}
        private static void Accept(ProjectDialog dialog)
        {dialog.GetType().GetMethod("Accept",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[0]);}
        private static bool Near(double a,double b){return Math.Abs(a-b)<.00001;}
        public static void Run(string directory,Action<bool,string> check,Action<Action,string> reject)
        {
            string root=Path.Combine(directory,"display-output");Directory.CreateDirectory(root);
            Project p=Small();Model.RequireValid(p);byte[] frame=Model.RenderFrame(p,100);PreviewElement line=p.Layout.Elements[0];
            check(p.SchemaVersion==4&&frame[0]==255&&frame[1]==64,"schema4 integrates preview/output without changing handcrafted rendered values");
            Func<int,byte[]> renderer=Model.CreateFrameRenderer(p);Project edit=Model.Clone(p);edit.Effects[0].ColorHtml="#000000";
            p.Effects[0].ColorHtml="#000000";check(renderer(100)[0]==255,"playback renderer snapshots edits once and renders without per-frame revalidation");p=Small();line=p.Layout.Elements[0];
            List<LayoutPoint> nodes=PreviewLayouts.Nodes(line);check(nodes.Count==3&&Near(nodes[1].X,.5)&&Near(nodes[1].Y,.15),"polyline nodes follow equal path distances across a hand-drawn roof");
            PreviewElement arch=Shape("Arch","Arch",5,1,2,.2,.2,.8,.7);List<LayoutPoint> arc=PreviewLayouts.Outline(arch);
            check(arc.Min(delegate(LayoutPoint q){return q.Y;})<.25&&arc.Max(delegate(LayoutPoint q){return q.Y;})>.65,"arch uses both drag coordinates as its bounding box");
            PreviewElement rectangle=Shape("Closed string","Rectangle",4,2,1,.1,.1,.9,.9);List<LayoutPoint> corners=PreviewLayouts.Nodes(rectangle);
            check(corners.Count==4&&!Near(corners[0].X,corners[3].X)||corners.Count==4&&!Near(corners[0].Y,corners[3].Y),"closed strings have distinct first and last pixels");
            PreviewElement matrix=Shape("Matrix","Matrix",6,2,2,.1,.1,.9,.9);matrix.Rows=2;matrix.Columns=3;matrix.Serpentine=true;
            List<LayoutPoint> grid=PreviewLayouts.Nodes(matrix);check(grid.Count==6&&grid[0].X<grid[1].X&&grid[3].X>grid[4].X,"matrix nodes preserve serpentine physical order");
            Color lit=PreviewLayouts.NodeColor(p,line,0,frame);check(lit.R==255&&lit.G==64&&lit.B==16,"preview RGB uses exact physical frame slots");
            Project reverse=PreviewLayouts.Assign(p,line.Id,p.Channels.Select(delegate(RawChannel r){return r.Id;}).ToList(),true);
            check(reverse.Channels[0].PreviewPixel==3&&p.Channels[0].PreviewPixel==1&&reverse.Channels[0].Address==p.Channels[0].Address,"reverse preview assignment is independent of physical patch and source project");
            reject(delegate{PreviewLayouts.Assign(p,line.Id,new List<string>{p.Channels[0].Id,p.Channels[0].Id,p.Channels[2].Id},false);},"duplicate raw IDs cannot silently overwrite pixel assignments");
            Project orphan=Model.Clone(p);orphan.Channels[0].PreviewNumber=99;check(PreviewLayouts.Diagnostics(orphan).Any(delegate(string s){return s.IndexOf("Hand RGB",StringComparison.OrdinalIgnoreCase)>=0;}),"diagnostics identify orphan raw preview mappings by channel name");
            string control=Path.Combine(root,"yard.hlsrpreview");p=PreviewLayouts.Compile(p,control);check(!p.Layout.Enabled&&!PreviewLayouts.IsCurrent(p),"Save compiles a Preview Control File without silently activating it");p=PreviewLayouts.LoadInto(p,control);check(PreviewLayouts.IsCurrent(p),"Identify explicitly activates the compiled Preview Control File");
            Project changed=Model.Clone(p);changed.Layout.Elements[0].Points[0].X+=.02;check(!PreviewLayouts.IsCurrent(changed),"moving geometry marks the identified preview control stale");
            changed=Model.Clone(p);changed.Channels[0].PreviewPixel=2;check(!PreviewLayouts.IsCurrent(changed),"changing raw preview mapping marks compiled control stale");
            Project other=Small();foreach(RawChannel raw in other.Channels){raw.Id=Model.NewId();raw.Universe=2;raw.Address+=30;}other.Effects.Clear();other.Groups[0].Members=other.Channels.Select(delegate(RawChannel r){return r.Id;}).ToList();
            other=PreviewLayouts.LoadInto(other,control);check(other.Channels[0].PreviewPixel==1&&other.Channels[0].Universe==2&&other.Channels[0].Address==31,"Identify reuses a layout by channel names while retaining another song's physical patch");
            byte[] originalControl=File.ReadAllBytes(control);Project modifiedControl=Model.Clone(p);modifiedControl.Layout.Elements[0].Points[0].Y+=.03;PreviewLayouts.Compile(modifiedControl,control);
            check(!PreviewLayouts.IsCurrent(p),"external or undo-divergent control-file contents invalidate the active preview");File.WriteAllBytes(control,originalControl);File.SetLastWriteTimeUtc(control,DateTime.UtcNow.AddSeconds(2));check(PreviewLayouts.IsCurrent(p),"restoring matching compiled control data restores freshness");
            ProjectIO.Save(p,Path.Combine(root,"roundtrip.hlsr"));Project loaded=ProjectIO.Load(Path.Combine(root,"roundtrip.hlsr"));check(loaded.Layout.Elements.Count==1&&loaded.Output.Controllers.Count==1&&loaded.Channels[1].PreviewPixel==2,"schema4 saves preview, controller settings and canonical raw mapping");
            string old=File.ReadAllText(Path.Combine(root,"roundtrip.hlsr")).Replace("<SchemaVersion>4</SchemaVersion>","<SchemaVersion>3</SchemaVersion>");File.WriteAllText(Path.Combine(root,"schema3.hlsr"),old);
            check(ProjectIO.Load(Path.Combine(root,"schema3.hlsr")).SchemaVersion==4,"schema3 opens as schema4 without rewriting the old file");
            Guid cid=new Guid("00112233-4455-6677-8899-aabbccddeeff");byte[] slots=new byte[512];slots[0]=0x7f;slots[511]=0x80;
            byte[] packet=ControllerOutput.EncodeE131(cid,"A\U0001F600B",0x1234,255,100,slots,false);
            check(packet.Length==638&&packet[0]==0&&packet[1]==16&&Encoding.ASCII.GetString(packet,4,9)=="ASC-E1.17","E1.31 ACN preamble and exact638-byte packet");
            check(packet[16]==0x72&&packet[17]==0x6e&&packet[38]==0x72&&packet[39]==0x58&&packet[115]==0x72&&packet[116]==0x0b,"E1.31 root/framing/DMP lengths match independently calculated PDU sizes");
            check(BitConverter.ToString(packet,22,16)=="00-11-22-33-44-55-66-77-88-99-AA-BB-CC-DD-EE-FF","E1.31 CID uses canonical UUID byte order");
            check(packet[111]==255&&packet[112]==0&&packet[113]==0x12&&packet[114]==0x34&&packet[119]==0&&packet[120]==0&&packet[121]==0&&packet[122]==1&&packet[123]==2&&packet[124]==1&&packet[125]==0&&packet[126]==0x7f&&packet[637]==0x80,"E1.31 sequence, universe, DMP addressing, property count and payload match standard");
            check(Encoding.UTF8.GetString(packet,44,6)=="A\U0001F600B"&&packet[50]==0&&ControllerOutput.EncodeE131(cid,"t",1,0,100,slots,true)[112]==64,"E1.31 UTF8 source name preserves surrogate pairs and termination bit");
            byte[] renard=ControllerOutput.EncodeRenard(new byte[]{0,0x7d,0x7e,0x7f,255});check(BitConverter.ToString(renard)=="7E-80-00-7F-2F-7F-30-7F-31-FF","Renard sync, start and all reserved-byte escapes match independent vector");
            Project serial=Small();serial.Output.Controllers.Clear();Controller sr=Route("Renard");sr.Port=0;sr.Priority=999;serial.Output.Controllers.Add(sr);
            check(ControllerOutput.Prepare(serial).Errors.Count==0,"Renard ignores E1.31 fields and prepares the configured24-byte route");
            List<OutputPacket> serialPackets=ControllerOutput.BuildPackets(serial,Model.RenderFrame(serial,100),cid,0,false);check(serialPackets.Count==1&&serialPackets[0].Data.Length==26&&serialPackets[0].Data[25]==0,"Renard zero-pads unused configured slots beyond the last raw channel");
            Controller dup=Route("Renard");dup.Name="Second route";serial.Output.Controllers.Add(dup);check(ControllerOutput.Prepare(serial).Errors.Count!=0,"duplicate enabled COM routes reject conflicting baud/capacity reuse");dup.Enabled=false;check(ControllerOutput.Prepare(serial).Errors.Count==0,"disabled serial routes do not claim a port");
            Project invalid=Small();invalid.Output.Controllers[0].Address="255.255.255.255";check(ControllerOutput.Prepare(invalid).Errors.Count>0,"E1.31 unicast rejects broadcast destinations");
            invalid=Small();invalid.Output.IntervalMs=10;check(ControllerOutput.Validate(invalid.Output).Count>0,"controller cadence cannot imply unsupported100fps E1.31 transmission");
            Spy spy=new Spy{Physical=true};using(OutputSession unarmed=new OutputSession(p,spy)){reject(delegate{unarmed.Start(false);},"physical start requires explicit arming before any write");check(spy.Writes==0,"unarmed physical session performs no transport writes");}check(spy.Disposals==1,"never-started session disposes its transport exactly once");
            RecordingOutputTransport recording=new RecordingOutputTransport();OutputSession session=new OutputSession(p,recording);session.Start(false);session.SendFrame(frame);session.Stop();session.Dispose();session.Dispose();
            List<OutputPacket> captured=recording.Packets;check(captured.Count==5&&captured[0].Data[126]==255&&captured[1].Data[126]==0&&captured.Skip(2).All(delegate(OutputPacket q){return q.Data[112]==64&&q.Data[126]==0;}),"recording lifecycle emits original frame, blackout and three stream-termination packets");
            reject(delegate{session.Start(false);},"stopped output session cannot restart a disposed transport");
            RecordingOutputTransport bounded=new RecordingOutputTransport();for(int i=0;i<4200;i++)bounded.Write(new OutputPacket{Data=new byte[]{1}});check(bounded.Packets.Count==4096&&bounded.DroppedPackets==104,"long dry runs retain a bounded4096-packet recording buffer");
            Project two=Small();Controller second=Route("E131");second.Name="Healthy route";second.Address="127.0.0.2";two.Output.Controllers.Add(second);Spy faulty=new Spy{FailId=two.Output.Controllers[0].Id};OutputSession failure=new OutputSession(two,faulty);failure.Start(false);reject(delegate{failure.Stop();},"blackout write failure is reported");failure.Dispose();check(faulty.Packets.Count==4&&faulty.Disposals==1&&!failure.IsRunning&&failure.LastError.Length>0,"failed route cannot prevent other-route blackout/termination and exactly-once cleanup");
            using(UdpClient receiver=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            {
                receiver.Client.ReceiveTimeout=1000;Project local=Small();local.Output.Controllers[0].Port=((IPEndPoint)receiver.Client.LocalEndPoint).Port;
                using(OutputSession network=new OutputSession(local,new NativeOutputTransport())){network.Start(true);network.SendFrame(Model.RenderFrame(local,100));network.Stop();}
                List<byte[]> received=new List<byte[]>();for(int i=0;i<5;i++){IPEndPoint from=new IPEndPoint(IPAddress.Any,0);received.Add(receiver.Receive(ref from));check(IPAddress.IsLoopback(from.Address),"native test packet"+i+" came only from owned loopback receiver");}
                check(received.Count==5&&received[0][126]==255&&received[4][112]==64,"native UDP sink delivers five exact loopback packets and releases its socket");
            }
            RecordingOutputTransport pumped=new RecordingOutputTransport();OutputSession pumpSession=new OutputSession(p,pumped);Stopwatch watch=Stopwatch.StartNew();
            using(OutputPump pump=new OutputPump(pumpSession,delegate{return(int)watch.ElapsedMilliseconds;},delegate(int time){Thread.Sleep(45);return frame;},25))
            {pump.Start(false);Thread.Sleep(180);pump.Stop();check(!pump.IsRunning&&pump.SkippedFrames>0&&pumpSession.FramesSent>=2,"output pump skips obsolete frames and joins its worker on stop");}
            using(ManualResetEvent release=new ManualResetEvent(false))using(ManualResetEvent entered=new ManualResetEvent(false))
            {
                OutputSession slowSession=new OutputSession(p,new RecordingOutputTransport());OutputPump slow=new OutputPump(slowSession,delegate{return 0;},delegate(int t){entered.Set();release.WaitOne();return frame;},25);
                try{slow.Start(false);check(entered.WaitOne(1000),"blocking fake provider entered owned output thread");reject(delegate{slow.Stop();},"timed-out output join reports retained resources");check(slow.IsRunning,"timeout never falsely marks live worker stopped");}
                finally{release.Set();slow.Stop();slow.Dispose();}check(!slow.IsRunning,"retained worker can be stopped and disposed after provider returns");
            }
            Project unpatched=Small();unpatched.Channels[0].Universe=0;Model.RequireValid(unpatched);OutputPlan unpatchedPlan=ControllerOutput.Prepare(unpatched);
            check(unpatchedPlan.Errors.Count==0&&unpatchedPlan.Warnings.Any(delegate(string s){return s.Contains("unpatched");}),"unpatched valid raw channels are diagnosed and excluded without breaking other routes");
            check(ControllerOutput.Prepare(p).Summary.Contains(p.Channels[0].Name),"Prepare report lists actual raw names, physical slots and covering routes");
            UiChecks(p,root,check,reject);
            BackgroundChecks(root,check);
            string show=CompiledShowIO.Compile(p,Path.Combine(root,"compiled.fseq"));check(CompiledShowIO.IsCurrent(p,show),"Compile for Show produces matching FSEQ hash and controller/project manifest");
            changed=Model.Clone(p);changed.Effects[0].ColorHtml="#112233";check(!CompiledShowIO.IsCurrent(changed,show),"effect changes invalidate compiled playback data");
            changed=Model.Clone(p);changed.Output.Controllers[0].Priority=99;check(!CompiledShowIO.IsCurrent(changed,show),"controller changes invalidate compiled playback data");
            Project replacement=Model.Clone(p);replacement.Effects[0].ColorHtml="#123456";
            using(FileStream locked=new FileStream(show,FileMode.Open,FileAccess.Read,FileShare.None))reject(delegate{CompiledShowIO.Compile(replacement,Path.Combine(root,"compiled.fseq"));},"locked manifest rejects a compile publication");
            check(CompiledShowIO.IsCurrent(p,show)&&!CompiledShowIO.IsCurrent(replacement,show),"failed manifest publication restores the previously current FSEQ/manifest pair");
            using(FileStream data=new FileStream(Path.Combine(root,"compiled.fseq"),FileMode.Open,FileAccess.Write)){data.Position=data.Length-1;data.WriteByte(123);}check(!CompiledShowIO.IsCurrent(p,show),"externally modified rendered data is detected by file hash");
            MakeDemo(root);
        }
        private static void UiChecks(Project source,string root,Action<bool,string> check,Action<Action,string> reject)
        {
            Project p=Model.Clone(source);PreviewElement high=p.Layout.Elements[0];high.Group=32768;high.Number=10000;high.PixelSize=1024;
            using(PreviewLayoutDialog dialog=new PreviewLayoutDialog(p))
            {
                Checks.PrepareHiddenControl(dialog);dialog.FlushElement();check(dialog.Result.Layout.Elements[0].Group==32768&&dialog.Result.Layout.Elements[0].Number==10000&&dialog.Result.Layout.Elements[0].PixelSize==1024,"preview editor preserves full valid group/number/pixel-size ranges");
                check(Convert.ToString(dialog.MappingGrid.Rows[0].Cells["Mapping"].Value).EndsWith(" / 1"),"mapping table displays one-based preview pixels");
            }
            using(PreviewLayoutDialog dialog=new PreviewLayoutDialog(source))
            {
                dialog.ElementName.Text="Edited roof";dialog.FlushElement();check(source.Layout.Elements[0].Name!="Edited roof"&&dialog.Result.Layout.Elements[0].Name=="Edited roof","layout edits remain isolated until Save");
                Paint(dialog,Path.Combine(root,"PreviewLayoutDialog.png"),1180,720);
            }
            Project two=Model.Clone(source);PreviewElement extra=Shape("Second object","Line",1,2,1,.1,.8,.9,.8);two.Layout.Elements.Add(extra);
            using(PreviewLayoutDialog dialog=new PreviewLayoutDialog(two))
            {
                Checks.PrepareHiddenControl(dialog);dialog.ElementName.Text="Pending first name";dialog.ElementList.SelectedIndex=1;
                check(dialog.Result.Layout.Elements[0].Name=="Pending first name"&&dialog.ElementName.Text=="Second object","switching objects flushes pending fields and loads the new canonical selection");
                dialog.ElementName.Text="";dialog.ElementList.SelectedIndex=0;
                check(dialog.ElementList.SelectedIndex==1&&dialog.ElementName.Text==""&&dialog.Result.Layout.Elements[1].Name=="Second object","invalid pending object edit retains selection and draft without partial mutation");
                dialog.ElementName.Text="Second object";dialog.FlushElement();dialog.ElementList.SelectedIndex=0;Press(dialog,"Down");
                check(dialog.Result.Layout.Elements[1].Name=="Pending first name","reordering after a property flush moves the canonical cloned object");
                Press(dialog,"Delete");check(dialog.Result.Layout.Elements.Count==1&&dialog.ElementName.Text=="Second object","delete after cloned edits removes the chosen object and loads the remaining properties");
                check(dialog.Canvas.Width>400&&dialog.Canvas.Width>dialog.ElementName.Parent.Width,"preview editor reserves the central canvas for actual drawing");
            }
            using(RawChannelManager dialog=new RawChannelManager(source))
            {
                dialog.rawGrid.Rows[0].Cells["PreviewGroup"].Value=2;dialog.rawGrid.Rows[0].Cells["PreviewNumber"].Value=3;dialog.rawGrid.Rows[0].Cells["PreviewPixel"].Value=1;dialog.ApplyGridEdits();
                check(dialog.Result.Channels[0].PreviewGroup==2&&dialog.Result.Channels[0].PreviewNumber==3&&dialog.Result.Channels[0].Address==source.Channels[0].Address,"raw manager exposes HLS preview group/number separately from controller patch");
            }
            Project editorSave=Model.Clone(source);editorSave.Layout.ControlFile=Path.Combine(root,"editor-saved.hlsrpreview");
            using(PreviewLayoutDialog dialog=new PreviewLayoutDialog(editorSave)){dialog.ElementName.Text="Saved editor roof";Accept(dialog);check(File.Exists(dialog.Result.Layout.ControlFile)&&!dialog.Result.Layout.Enabled&&dialog.Result.Layout.Elements[0].Name=="Saved editor roof","editor Save writes a compiled control and leaves explicit Identify for activation");}
            Project slow=Model.Clone(source);slow.Output.IntervalMs=60000;
            using(OutputSetupDialog dialog=new OutputSetupDialog(slow)){dialog.FlushControllers();check(dialog.Result.Output.IntervalMs==60000,"output editor preserves valid60000ms interval");}
            using(OutputSetupDialog dialog=new OutputSetupDialog(source))
            {
                dialog.ControllerGrid.Rows[0].Cells["Name"].Value="Edited controller";dialog.FlushControllers();check(source.Output.Controllers[0].Name!="Edited controller"&&dialog.Result.Output.Controllers[0].Name=="Edited controller","controller setup edits are cloned and perform no transport writes");
                dialog.ControllerGrid.Rows[0].Cells["FirstUniverse"].Value="invalid";reject(delegate{dialog.FlushControllers();},"invalid controller numeric entry rejects before committing the candidate");check(dialog.Result.Output.Controllers[0].FirstUniverse==1,"invalid output grid leaves last valid setup intact");dialog.ControllerGrid.Rows[0].Cells["FirstUniverse"].Value=1;
                Paint(dialog,Path.Combine(root,"OutputSetupDialog.png"),1160,680);
            }
            using(OutputTestDialog dialog=new OutputTestDialog(source))
            {
                Checks.PrepareHiddenControl(dialog);check(!dialog.PhysicalArm.Checked&&dialog.Pump==null,"test dialog defaults to dry run and constructs no output worker");dialog.PhysicalArm.Checked=true;check(dialog.StartButton.Text=="Start physical output"&&dialog.Pump==null,"arming changes the Start label without creating a transport or worker");dialog.PhysicalArm.Checked=false;
                reject(delegate{dialog.StartTest();},"test without checked raw channels rejects safely");dialog.RawList.SetItemChecked(0,true);dialog.StartTest();RecordingOutputTransport captured=dialog.Recording;Thread.Sleep(90);
                check(captured!=null&&dialog.Pump.IsRunning&&!dialog.Mode.Enabled,"dry test runs only recording transport with immutable control snapshots");dialog.AllOff();int stopped=captured.Packets.Count;Thread.Sleep(60);
                check(dialog.Pump==null&&!dialog.PhysicalArm.Checked&&captured.Packets.Count==stopped&&captured.Packets.Count>=5,"All Off stops and latches blackout without a later relighting frame");
                check(captured.Packets[captured.Packets.Count-1].Data[112]==64,"dry test shutdown sends final stream termination");Paint(dialog,Path.Combine(root,"OutputTestDialog.png"),690,480);
            }
            Project canvasProject=Model.Clone(source);canvasProject.Layout.Elements.Clear();PreviewElement single=Shape("Single vector","Line",1,1,1,.15,.5,.85,.5);canvasProject.Layout.Elements.Add(single);canvasProject=PreviewLayouts.Assign(canvasProject,single.Id,new List<string>{canvasProject.Channels[0].Id},false);
            canvasProject=PreviewLayouts.Compile(canvasProject,Path.Combine(root,"single.hlsrpreview"));canvasProject=PreviewLayouts.LoadInto(canvasProject,canvasProject.Layout.ControlFile);
            using(LayoutCanvas canvas=new LayoutCanvas{Project=canvasProject,EditMode=false,Frame=Model.RenderFrame(canvasProject,100),Size=new Size(816,594)})
            {
                Checks.PrepareHiddenControl(canvas);string selected=null;canvas.NodeSelected+=delegate(object s,PreviewNodeEventArgs e){selected=e.ElementId;};Mouse(canvas,"OnMouseDown",408,298,MouseButtons.Left);check(selected==single.Id,"runtime click anywhere on one-node vector reveals its channel");
                Paint(canvas,Path.Combine(root,"runtime-preview.png"),816,594);
                using(Bitmap lit=new Bitmap(816,594)){canvas.DrawToBitmap(lit,new Rectangle(0,0,816,594));check(lit.GetPixel(408,298).R>100&&lit.GetPixel(408,298).R>lit.GetPixel(408,298).B,"single-node vector actually lights from its mapped RGB frame");}
                canvas.Frame=new byte[Model.OutputSize(canvasProject)];using(Bitmap off=new Bitmap(816,594)){canvas.DrawToBitmap(off,new Rectangle(0,0,816,594));check(off.GetPixel(408,298).R<80,"runtime black frame leaves the string dark");}
                canvas.EditMode=true;canvas.Frame=null;Mouse(canvas,"OnMouseDown",408,298,MouseButtons.Left);Mouse(canvas,"OnMouseMove",800,298,MouseButtons.Left);Mouse(canvas,"OnMouseUp",800,298,MouseButtons.Left);
                PreviewElement moved=canvasProject.Layout.Elements[0];check(Near(moved.Points[1].X-moved.Points[0].X,.7)&&moved.Points[1].X<=1,"whole-object movement preserves geometry width at canvas boundary");
            }
            Project segmented=Model.Clone(source);Segment segment=new Segment{Id=Model.NewId(),Name="Combined string",Members=segmented.Channels.Select(delegate(RawChannel raw){return raw.Id;}).ToList()};segmented.Segments.Add(segment);segmented.Groups[0].Members=new List<string>{segment.Id};
            using(MainForm connected=new MainForm(new EditSession(segmented)))
            {
                connected.RecoveryEnabled=false;Checks.PrepareHiddenControl(connected);typeof(MainForm).GetMethod("RevealRaw",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(connected,new object[]{segmented.Channels[0].Id});
                check(Walk(connected).OfType<TimelineView>().First().HighlightTargetId==segment.Id,"preview light lookup reveals its containing Segment row when raw rows are not in a group");
            }
            using(MainForm form=new MainForm(new EditSession(source))){form.RecoveryEnabled=false;Paint(form,Path.Combine(root,"main-display-output.png"),1280,880);check(!form.IsDirty,"native main display/output integration renders without audio or controller activation");}
        }
        private static void BackgroundChecks(string root,Action<bool,string> check)
        {
            string path=Path.Combine(root,"background.bmp");Project p=Small();p.Layout.Elements.Clear();p.Layout.BackgroundImage=path;p.Layout.BackgroundOpacity=1;
            using(Bitmap file=new Bitmap(32,32)){using(Graphics g=Graphics.FromImage(file))g.Clear(Color.Red);file.Save(path,System.Drawing.Imaging.ImageFormat.Bmp);}
            using(LayoutCanvas canvas=new LayoutCanvas{Project=p,EditMode=true,Size=new Size(400,304)})
            {
                Checks.PrepareHiddenControl(canvas);using(Bitmap image=new Bitmap(400,304)){canvas.DrawToBitmap(image,new Rectangle(0,0,400,304));check(image.GetPixel(200,154).R>240,"local background image is rendered in the real preview canvas");}
                using(Bitmap file=new Bitmap(32,32)){using(Graphics g=Graphics.FromImage(file))g.Clear(Color.Blue);file.Save(path,System.Drawing.Imaging.ImageFormat.Bmp);}File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(1));
                using(Bitmap image=new Bitmap(400,304)){canvas.DrawToBitmap(image,new Rectangle(0,0,400,304));check(image.GetPixel(200,154).B>240&&image.GetPixel(200,154).R<20,"overwriting a background at the same path refreshes its unlocked bitmap");}
                File.Delete(path);canvas.RefreshBackgroundStatus();check(canvas.BackgroundWarning.Contains("missing"),"deleting an active background produces a visible diagnostic");
                using(Bitmap file=new Bitmap(8193,1))file.Save(path,System.Drawing.Imaging.ImageFormat.Bmp);canvas.RefreshBackgroundStatus();check(canvas.BackgroundWarning.Contains("8192"),"oversized background metadata is rejected before cloning into x86 preview memory");
            }
        }
        private static void MakeDemo(string root)
        {
            Project p=Model.Seed();p.Name="Handcrafted arches and roof";p.Layout.Elements.Clear();
            PreviewElement roof=Shape("Roof pixels","Polyline",6,1,1,.15,.38,.85,.38);roof.Points.Insert(1,new LayoutPoint{X=.5,Y=.12});
            PreviewElement arch=Shape("Yard arch pixels","Arch",6,1,2,.25,.48,.75,.85);p.Layout.Elements.Add(roof);p.Layout.Elements.Add(arch);
            p=PreviewLayouts.Assign(p,roof.Id,p.Channels.Take(6).Select(delegate(RawChannel r){return r.Id;}).ToList(),false);
            p=PreviewLayouts.Assign(p,arch.Id,p.Channels.Skip(6).Take(6).Select(delegate(RawChannel r){return r.Id;}).ToList(),false);
            p.Output.Controllers.Add(Route("E131"));p.Output.Controllers[0].Name="Local preview test route (set hardware IP before use)";
            p=PreviewLayouts.Compile(p,Path.Combine(root,"display-demo.hlsrpreview"));p=PreviewLayouts.LoadInto(p,p.Layout.ControlFile);p.AudioFile="demo.wav";
            ProjectIO.Save(p,Path.Combine(root,"display-demo.hlsr"));Model.ExportFseq(p,Path.Combine(root,"display-demo.fseq"));
            using(PreviewLayoutDialog dialog=new PreviewLayoutDialog(p))Paint(dialog,Path.Combine(root,"display-demo-layout.png"),1180,720);
            using(LayoutCanvas canvas=new LayoutCanvas{Project=p,Frame=Model.RenderFrame(p,1800),EditMode=false})Paint(canvas,Path.Combine(root,"display-demo-preview.png"),900,630);
        }
    }
}
