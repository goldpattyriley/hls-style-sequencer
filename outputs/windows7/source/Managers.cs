using System;
using System.Windows.Forms;
namespace HlsRecreation {
 public abstract class ProjectDialog:Form {
  public static bool SuppressErrorDialogs;
  protected Project working;
  public Project Result { get { return working; } }
  protected ProjectDialog(Project project) { working=Model.Clone(project); StartPosition=FormStartPosition.CenterParent; Width=900; Height=560; MinimizeBox=false; ShowInTaskbar=false; }
  protected Panel Buttons() { Panel p=new Panel();p.Dock=DockStyle.Bottom;p.Height=42;FlowLayoutPanel right=new FlowLayoutPanel();right.Dock=DockStyle.Right;right.Width=180;right.FlowDirection=FlowDirection.RightToLeft;right.Padding=new Padding(4);Button cancel=new Button();cancel.Text="Cancel";cancel.DialogResult=DialogResult.Cancel;Button save=new Button();save.Text="Save";save.Click+=delegate{try{Accept();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);}};right.Controls.Add(cancel);right.Controls.Add(save);p.Controls.Add(right);AcceptButton=save;CancelButton=cancel;return p; }
  protected virtual void Accept(){Model.RequireValid(working);DialogResult=DialogResult.OK;Close();}
  protected static int Int(object value,int fallback){int n;return Int32.TryParse(Convert.ToString(value),out n)?n:fallback;}
  protected static string Target(Project project,string id){return Model.TargetName(project,id)+"  ["+Model.TargetKind(project,id)+"]";}
  protected static TableLayoutPanel Three(string a,string b,string c,ListBox x,ListBox y,ListBox z){return Lists(a,b,c,x,y,z);}
  protected static Button B(string text,EventHandler click){Button b=new Button();b.Text=text;b.AutoSize=true;b.Click+=delegate(object s,EventArgs e){try{click(s,e);}catch(Exception error){if(SuppressErrorDialogs)throw;Form owner=b.FindForm();MessageBox.Show(owner,error.Message,owner==null?"Channel editor":owner.Text);}};return b;}
  protected string NextGroupName(){int n=1;while(working.Groups.Exists(delegate(DisplayGroup g){return g.Name=="Display Group "+n;}))n++;return "Display Group "+n;}
  protected string NextSegmentName(){int n=1;while(working.Segments.Exists(delegate(Segment s){return s.Name=="Segment "+n;}))n++;return "Segment "+n;}
  protected static void Fmt(ListBox box,Func<object,string> text){box.FormattingEnabled=true;box.Format+=delegate(object sender,ListControlConvertEventArgs e){e.Value=text(e.ListItem);};}
  protected static TableLayoutPanel Lists(string a,string b,string c,ListBox x,ListBox y,ListBox z){TableLayoutPanel t=new TableLayoutPanel(){Dock=DockStyle.Fill,ColumnCount=3,RowCount=2};for(int i=0;i<3;i++)t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33));t.Controls.Add(new Label(){Text=a,Dock=DockStyle.Fill},0,0);t.Controls.Add(new Label(){Text=b,Dock=DockStyle.Fill},1,0);t.Controls.Add(new Label(){Text=c,Dock=DockStyle.Fill},2,0);x.Dock=y.Dock=z.Dock=DockStyle.Fill;t.Controls.Add(x,0,1);t.Controls.Add(y,1,1);t.Controls.Add(z,2,1);return t;}
 }
}
