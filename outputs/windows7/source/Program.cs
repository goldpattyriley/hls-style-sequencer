using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: AssemblyTitle("HLS-style Sequencer")]
[assembly: AssemblyDescription("Original Windows 7 targeted HLS workflow recreation")]
[assembly: AssemblyVersion("0.4.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.0", FrameworkDisplayName = ".NET Framework 4")]

namespace HlsRecreation
{
    internal static class Program
    {
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
                Project project = args.Length == 1 ? ProjectIO.Load(args[0]) : Model.Seed();
                using (MainForm form = new MainForm(new EditSession(project)))
                { if (args.Length == 1) form.LoadProject(args[0]); Application.Run(form); }
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length == 2 && (args[0] == "--self-test" || args[0] == "--render"))
                    File.WriteAllText(args[1] + ".error.txt", error.ToString());
                else MessageBox.Show(error.Message, "HLS-style Sequencer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
