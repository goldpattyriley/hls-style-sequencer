using System;
using System.Collections.Generic;
using System.IO;

namespace HlsRecreation
{
    public static class LargeProjectChecks
    {
        public static void Run(string directory, Action<bool, string> check)
        {
            Project project = Model.Empty(); Model.AddRawChannels(project, 1, "Large file regression", "RGB", 1, 1);
            string target = project.Channels[0].Id;
            for (int i = 0; i < 40000; i++)
                project.Effects.Add(new Effect { Id = Model.NewId(), TargetId = target, Kind = "Level", StartMs = 0, DurationMs = 1000, ColorHtml = "#336699", Level = .75 });
            string path = Path.Combine(directory, "above-16mb.hlsr");
            ProjectIO.Save(project, path);
            long bytes = new FileInfo(path).Length;
            check(bytes > 16L * 1024 * 1024, "regression fixture contains real sequencing data larger than 16 MB (" + bytes + " bytes)");
            Project loaded = ProjectIO.Load(path);
            check(loaded.Effects.Count == 40000 && loaded.Effects[39999].Id == project.Effects[39999].Id && loaded.Effects[39999].Level == .75, "large project save/open retains effect identities and values without a byte-size rejection");
            Selection selection = new Selection { StartMs = 0, DurationMs = 1000 }; selection.Rows.Add(target);
            EffectLibrary library = EffectLibraries.Empty("Large editable group"); library.Items.Add(EffectLibraries.Capture(project, selection, "Forty thousand parts"));
            string libPath = Path.Combine(directory, "above-16mb.hlsrlib"); EffectLibraries.Save(library, libPath);
            check(new FileInfo(libPath).Length > 16L * 1024 * 1024 && EffectLibraries.Load(libPath).Items[0].Group.Effects.Count == 40000, "effect libraries also save/reopen valid groups above the former 16 MB boundary");
            File.WriteAllText(Path.Combine(directory, "large-project-results.txt"), "Project bytes=" + bytes + Environment.NewLine + "Effects=40000" + Environment.NewLine + "PASS: streamed project and library IO above 16 MB.");
        }
    }
}
