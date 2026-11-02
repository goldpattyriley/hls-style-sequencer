using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace HlsRecreation
{
    public static class BundledExampleChecks
    {
        private static string Hash(Stream input){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input));}
        public static void Run(string directory,Action<bool,string> check,Action<Action,string> reject)
        {
            string root=Path.Combine(directory,"single executable examples");
            string project=BundledExamples.EnsureExamples(root);
            string[] assets={"demo.wav","display-demo.hlsr","display-demo.hlsrpreview","custom-demo.hlsr","handcrafted.hlsrlib"};
            Assembly assembly=typeof(BundledExamples).Assembly;
            check(assets.All(delegate(string name){return assembly.GetManifestResourceNames().Contains("HlsStyleSequencer.Examples."+name);}),"one executable contains demo audio, both editable samples, preview and library");
            check(Path.GetFullPath(project)==Path.Combine(Path.GetFullPath(root),"display-demo.hlsr"),"bundled startup project resolves inside the requested writable examples folder");
            foreach(string name in assets)
                using(Stream embedded=assembly.GetManifestResourceStream("HlsStyleSequencer.Examples."+name))
                using(Stream extracted=File.OpenRead(Path.Combine(root,name)))check(Hash(embedded)==Hash(extracted),"bundled "+name+" extracts byte-for-byte without sidecars");
            using(MainForm form=new MainForm(new EditSession(Model.Empty())))
            {
                form.RecoveryEnabled=false;form.LoadProject(project);Project loaded=form.Session.Project;
                check(File.Exists(loaded.AudioFile)&&PreviewLayouts.IsCurrent(loaded),"automatically created editable demo resolves its audio and active preview");
            }
            Project edited=ProjectIO.Load(project);edited.Name="My edited startup demo";ProjectIO.Save(edited,project);
            byte[] before=File.ReadAllBytes(project);BundledExamples.EnsureExamples(root);
            check(before.SequenceEqual(File.ReadAllBytes(project)),"relaunch never overwrites a user's edited example project");
            File.Delete(Path.Combine(root,"demo.wav"));BundledExamples.EnsureExamples(root);
            check(File.Exists(Path.Combine(root,"demo.wav"))&&before.SequenceEqual(File.ReadAllBytes(project)),"relaunch repairs a missing bundled asset while preserving existing user data");
            string concurrentRoot=Path.Combine(directory,"parallel example startup");Exception first=null,second=null;
            Thread a=new Thread(delegate(){try{BundledExamples.EnsureExamples(concurrentRoot);}catch(Exception error){first=error;}});
            Thread b=new Thread(delegate(){try{BundledExamples.EnsureExamples(concurrentRoot);}catch(Exception error){second=error;}});
            a.Start();b.Start();bool joinedA=a.Join(5000),joinedB=b.Join(5000);
            check(joinedA&&joinedB&&first==null&&second==null,"simultaneous startups publish examples without clobbering or partial files");
            check(Directory.GetFiles(concurrentRoot).Length==5,"successful parallel extraction leaves only the five complete example files");
            string blocked=Path.Combine(directory,"examples-is-a-file");File.WriteAllText(blocked,"keep me");
            reject(delegate{BundledExamples.EnsureExamples(blocked);},"invalid extraction target reports an error without removing existing data");
            check(File.ReadAllText(blocked)=="keep me","failed startup preserves an existing non-directory target");
            reject(delegate{BundledExamples.EnsureExamples(Path.GetPathRoot(directory));},"example extraction rejects a drive root");
        }
    }
}
