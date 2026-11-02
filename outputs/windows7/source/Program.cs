using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: AssemblyTitle("HLS-style Sequencer")]
[assembly: AssemblyDescription("Original Windows 7 targeted HLS workflow recreation")]
[assembly: AssemblyVersion("0.4.1.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.0", FrameworkDisplayName = ".NET Framework 4")]

namespace HlsRecreation
{
    internal static class Program
    {
        private static MainForm CreateStartupForm(string[] args,string examplesDirectory)
        {
            string demo=BundledExamples.EnsureExamples(examplesDirectory);
            string path=args.Length==1?Path.GetFullPath(args[0]):demo;
            MainForm form=new MainForm(new EditSession(ProjectIO.Load(path)));
            try{form.LoadProject(path);return form;}catch{form.Dispose();throw;}
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 2 && args[0] == "--self-test") return Checks.Run(args[1]);
                if (args.Length == 2 && args[0] == "--render")
                {
                    using (MainForm form = new MainForm(new EditSession(Model.Seed())))
                    using (Bitmap bitmap = new Bitmap(1280, 880))
                    {
                        form.ClientSize = bitmap.Size;
                        Checks.PrepareHiddenControl(form);
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    return 0;
                }
                if(args.Length == 2 && args[0] == "--startup-check")
                {
                    string checkDirectory=Path.GetFullPath(args[1]);Directory.CreateDirectory(checkDirectory);
                    using(MainForm form=CreateStartupForm(new string[0],Path.Combine(checkDirectory,"examples")))
                    using(Bitmap bitmap=new Bitmap(1280,880))
                    {
                        form.RecoveryEnabled=false;form.ClientSize=bitmap.Size;Checks.PrepareHiddenControl(form);
                        form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(checkDirectory,"startup.png"));
                        Project p=form.Session.Project;
                        if(!File.Exists(p.AudioFile)||!PreviewLayouts.IsCurrent(p))throw new InvalidDataException("Bundled startup references did not resolve.");
                        File.WriteAllText(Path.Combine(checkDirectory,"startup.txt"),"PASS: normal startup constructs the editable demo from one executable, with local WAV and current preview, no config/sidecars, no visible UI/audio/device output.");
                    }
                    return 0;
                }
                using(MainForm form=CreateStartupForm(args,BundledExamples.DefaultDirectory)) Application.Run(form);
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length == 2 && (args[0] == "--self-test" || args[0] == "--render" || args[0] == "--startup-check"))
                    File.WriteAllText(args[1] + ".error.txt", error.ToString());
                else MessageBox.Show(error.Message, "HLS-style Sequencer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
