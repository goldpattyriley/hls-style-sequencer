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
    public static class CustomLibraryChecks
    {
        private static CustomDefinition Recipe()
        {
            CustomDefinition definition = new CustomDefinition { Id = Model.NewId(), Name = "Handcrafted three-part recipe" };
            definition.Parts.Add(new CustomPart { Kind = "Level", LengthCells = 2, StartLevel = .4, EndLevel = .4, ColorHtml = "#FF0000" });
            definition.Parts.Add(new CustomPart { Kind = "RampUp", LengthCells = 3, StartLevel = .35, EndLevel = 1, ColorHtml = "#00FF00" });
            definition.Parts.Add(new CustomPart { Kind = "RampDown", LengthCells = 2, StartLevel = 1, EndLevel = 0, ColorHtml = "#0000FF" });
            return definition;
        }
        private static Project EmptyShow()
        {
            Project p = Model.Empty(); Model.AddRawChannels(p, 4, "Manual RGB", "RGB", 1, 1);
            foreach (RawChannel raw in p.Channels) p.Groups[0].Members.Add(raw.Id); return p;
        }
        private static Selection Box(Project p, int rows, int start, int duration)
        { Selection result = new Selection { StartMs = start, DurationMs = duration }; for (int i = 0; i < rows; i++) result.Rows.Add(p.Channels[i].Id); return result; }
        private static string EffectsKey(Project p)
        {
            StringBuilder key = new StringBuilder();
            foreach (Effect f in p.Effects) key.Append(f.TargetId).Append('|').Append(f.StartMs).Append('|').Append(f.DurationMs).Append('|').Append(f.Kind).Append('|').Append(f.ColorHtml).Append('|').Append(f.Level.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(f.StartLevel.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(f.EndLevel.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            return key.ToString();
        }
        public static void Run(string directory, Action<bool, string> check, Action<Action, string> reject)
        {
            Project source = EmptyShow(); CustomDefinition definition = Recipe(); source.CustomEffects.Add(definition);
            check(CustomEffects.Validate(definition).Count == 0 && CustomEffects.TotalCells(definition) == 7, "custom definition is an ordered seven-cell serial recipe");
            Selection region = Box(source, 3, 0, 2000);
            CustomPlacement placement = new CustomPlacement { Mode = "Chase", ColorMode = "Embedded", RepeatCount = 2, PeriodCells = 10, StaggerCells = 1, Seed = 42 };
            Project placed = CustomEffects.Place(source, definition, region, placement);
            check(source.Effects.Count == 0 && placed.Effects.Count == 18, "custom placement creates independent primitive copies without changing the source");
            check(placed.Effects[0].StartMs == 0 && placed.Effects[1].StartMs == 100 && placed.Effects[2].StartMs == 250 && placed.Effects[3].StartMs == 500 && placed.Effects[6].StartMs == 50, "recipe lengths, repeat period and per-row chase offset are preserved");
            check(placed.Effects[1].Kind == "RampUp" && placed.Effects[1].UseEndpoints && placed.Effects[1].StartLevel == .35 && placed.Effects[1].EndLevel == 1, "35-to-100 intensity endpoints remain authored ramp data");
            check(Model.RenderFrame(placed, 100)[1] == 89 && Model.RenderFrame(placed, 150)[1] == 172 && Model.RenderFrame(placed, 200)[1] == 255, "three-cell endpoint ramp renders requested start, middle and last-cell end intensities");
            check(Model.RenderFrame(placed, 250)[2] == 255 && Model.RenderFrame(placed, 300)[2] == 0, "two-cell down ramp reaches zero on its final selected cell");
            placement.ColorMode = "Selected"; placement.SelectedColor = "#FF00FF";
            Project selected = CustomEffects.Place(source, definition, region, placement);
            check(selected.Effects.All(delegate(Effect f) { return f.ColorHtml == "#FF00FF"; }), "placement color replaces stored line colors in Selected mode");
            placement.ColorMode = "Embedded";
            check(placed.Effects[0].ColorHtml == "#FF0000" && placed.Effects[1].ColorHtml == "#00FF00", "Embedded mode retains each authored line color");
            placement.ColorMode = "Random"; placement.Mode = "Random"; placement.RandomizePartOrder = true; placement.RandomIntensity = true; placement.MinIntensityPercent = placement.MaxIntensityPercent = 50; placement.PeriodCells = 4;
            Project randomA = CustomEffects.Place(source, definition, region, placement), randomB = CustomEffects.Place(source, definition, region, placement);
            check(EffectsKey(randomA) == EffectsKey(randomB) && randomA.Effects.Select(delegate(Effect f) { return f.ColorHtml; }).Distinct().Count() > 1, "seeded placement realizes reproducible random position/order/color/intensity data");
            check(randomA.Effects.Where(delegate(Effect f) { return f.UseEndpoints; }).All(delegate(Effect f) { return f.StartLevel <= .5 && f.EndLevel <= .5; }), "random-intensity bounds are applied to ramp endpoints");
            check(randomA.Effects.Where(delegate(Effect f, int i) { return i % 3 == 0; }).All(delegate(Effect f) { return f.StartMs % 200 == 0; }), "random starts honor an explicitly selected period grid");
            placement.CoveragePercent = 0;
            check(CustomEffects.Place(source, definition, region, placement).Effects.Count == 0, "zero coverage produces no stamps"); placement.CoveragePercent = 100;
            CustomDefinition malformed = CustomEffects.Clone(definition); malformed.Parts[0].StartLevel = Double.NaN;
            check(CustomEffects.Validate(malformed).Count > 0, "non-finite recipe intensities are rejected");
            malformed = CustomEffects.Clone(definition); malformed.Parts = null;
            check(CustomEffects.Validate(malformed).Count > 0, "null recipe collections return validation errors");
            reject(delegate { CustomEffects.Place(source, definition, Box(source, 1, 0, 100), placement); }, "partial recipe placement is rejected instead of silently cut");
            Selection ppSelection = new Selection { StartMs = 0, DurationMs = 2000 }; Project seed = Model.Seed(); ppSelection.Rows.Add(seed.PixelPlanes[0].Id);
            reject(delegate { CustomEffects.Place(seed, definition, ppSelection, placement); }, "serial time-domain recipes do not silently replace PixelPlane spatial clips");
            Project overflow = EmptyShow(); overflow.FrameMs = 10; overflow.DurationMs = 60000; Model.AddRawChannels(overflow, 36, "Budget RGB", "RGB", 2, 1);
            CustomDefinition manyParts = new CustomDefinition { Id = Model.NewId(), Name = "Bounded placement" };
            for (int i = 0; i < 256; i++) manyParts.Parts.Add(new CustomPart { Kind = "Level", LengthCells = 2 });
            CustomPlacement manyCopies = new CustomPlacement { Mode = "Chase", RepeatCount = 20, StaggerCells = 0 };
            reject(delegate { CustomEffects.Place(overflow, manyParts, Box(overflow, 40, 0, 60000), manyCopies); }, "placement budget is rejected before generating an oversized primitive graph");

            EffectLibrary library = EffectLibraries.Empty("Original editable work");
            Selection capture = Box(placed, 4, 0, 1000); LibraryItem group = EffectLibraries.Capture(placed, capture, "Three-part arch block");
            library.Items.Add(group); library.Items.Add(EffectLibraries.FromCustom(definition, "Authored serial recipe")); EffectLibraries.RequireValid(library);
            check(group.Group.Rows.Count == 4 && group.Group.Effects.Count == 18 && group.Group.DurationMs == 1000, "library capture retains blank rows, full primitive data and rectangular width");
            reject(delegate { EffectLibraries.Capture(placed, Box(placed, 3, 50, 1000), "Partial"); }, "library capture requires complete edge clips and preserves source effects");
            Project inserted = EffectLibraries.Insert(source, group, source.Groups[0].Members, 0, 2000);
            bool equal = true; for (int t = 0; t < 1000; t += 50) if (!Model.RenderFrame(placed, t).SequenceEqual(Model.RenderFrame(inserted, 2000 + t))) equal = false;
            check(equal && inserted.Effects.All(delegate(Effect f) { return !placed.Effects.Exists(delegate(Effect old) { return old.Id == f.Id; }); }), "upper-left insertion preserves every sampled output value with fresh effect identities");
            Project changed = Model.Clone(inserted); changed.Effects[0].ColorHtml = "#FFFFFF";
            check(group.Group.Effects[0].ColorHtml == "#FF0000" && placed.Effects[0].ColorHtml == "#FF0000", "editing an inserted effect cannot change its library source or prior placements");
            definition.Parts[0].ColorHtml = "#112233";
            check(library.Items[1].Definition.Parts[0].ColorHtml == "#FF0000" && placed.Effects[0].ColorHtml == "#FF0000", "library recipes and committed stamps remain independent of later definition edits");
            definition.Parts[0].ColorHtml = "#FF0000";
            reject(delegate { EffectLibraries.Insert(source, group, source.Groups[0].Members, 1, 2000); }, "library insertion does not silently drop rows below the target view");
            Project mismatched = Model.Clone(source); mismatched.FrameMs = 100;
            reject(delegate { EffectLibraries.Insert(mismatched, group, mismatched.Groups[0].Members, 0, 2000); }, "library insertion rejects a frame-step mismatch instead of changing column geometry");
            EffectLibrary badLibrary = EffectLibraries.Clone(library); badLibrary.Items[0].Group.Effects[0].TargetId = null;
            check(EffectLibraries.Validate(badLibrary).Count > 0, "malformed null-target library effects return validation errors without throwing");
            string path = Path.Combine(directory, "handcrafted.hlsrlib"); EffectLibraries.Save(library, path); EffectLibraries.Save(library, path);
            EffectLibrary reopened = EffectLibraries.Load(path);
            check(reopened.Items.Count == 2 && File.Exists(path + ".bak") && reopened.Items[0].Group.Effects[1].UseEndpoints, "standalone library save/open and backup retain endpoint primitives and recipe definitions");
            Project ppOnly = Model.Seed(); ppOnly.Effects.RemoveAll(delegate(Effect f) { return f.Kind != "PixelPlane"; });
            Selection ppBox = new Selection { StartMs = 5000, DurationMs = 3000 }; ppBox.Rows.Add(ppOnly.PixelPlanes[0].Id);
            LibraryItem ppGroup = EffectLibraries.Capture(ppOnly, ppBox, "Painted matrix excerpt"); Project ppDest = Model.Clone(ppOnly); ppDest.Effects.Clear();
            Project ppInserted = EffectLibraries.Insert(ppDest, ppGroup, new List<string> { ppDest.PixelPlanes[0].Id }, 0, 0);
            check(Model.RenderFrame(ppOnly, 6100).SequenceEqual(Model.RenderFrame(ppInserted, 1100)), "library insertion preserves PixelPlane keys, speed, fade and mapping semantics");
            string projectPath = Path.Combine(directory, "recipes.hlsr"); ProjectIO.Save(placed, projectPath); Project reloaded = ProjectIO.Load(projectPath);
            check(reloaded.CustomEffects.Count == 1 && reloaded.CustomEffects[0].Parts.Count == 3 && Model.RenderFrame(reloaded, 200).SequenceEqual(Model.RenderFrame(placed, 200)), "project round trip retains editable definitions and exact committed ramp output");
            string renderedPath = Path.Combine(directory, "custom-demo.fseq"); Model.ExportFseq(reloaded, renderedPath);
            using (BinaryReader reader = new BinaryReader(File.OpenRead(renderedPath)))
            {
                reader.BaseStream.Position = 28 + 4 * Model.OutputSize(reloaded);
                check(reader.ReadBytes(Model.OutputSize(reloaded)).SequenceEqual(Model.RenderFrame(reloaded, 200)), "exported custom-effect frame retains the exact authored endpoint output");
            }
            string quotaPath = Path.Combine(directory, "oversized-collection.hlsr");
            using (StreamWriter writer = new StreamWriter(quotaPath)) { writer.Write("<Project><CustomEffects><CustomDefinition><Parts>"); for (int i = 0; i < 257; i++) writer.Write("<CustomPart/>"); writer.Write("</Parts></CustomDefinition></CustomEffects></Project>"); }
            reject(delegate { ProjectIO.Load(quotaPath); }, "XML collection quotas are enforced while streaming before project graph allocation");
            string longText = Path.Combine(directory, "oversized-scalar.hlsr"); File.WriteAllText(longText, "<Project><Name>" + new String('x', 1024) + "</Name></Project>");
            reject(delegate { ProjectIO.Load(longText); }, "streaming scalar quotas reject malformed metadata without restoring the old file-size cap");
            string legacyPath = Path.Combine(directory, "legacy-schema2.hlsr");
            File.WriteAllText(legacyPath, File.ReadAllText(projectPath).Replace("<SchemaVersion>4</SchemaVersion>", "<SchemaVersion>2</SchemaVersion>"));
            check(ProjectIO.Load(legacyPath).SchemaVersion == 4 && File.ReadAllText(legacyPath).Contains("<SchemaVersion>2</SchemaVersion>"), "schema-2 Open upgrades only the in-memory project");
            CheckDialogs(source, definition, group, library, directory, check, reject);
        }
        private static void CheckDialogs(Project project, CustomDefinition definition, LibraryItem group, EffectLibrary library, string directory, Action<bool, string> check, Action<Action, string> reject)
        {
            Project editable = Model.Clone(project);
            using (CustomEffectsDialog dialog = new CustomEffectsDialog(editable, Box(editable, 2, 0, 2000), Color.Red))
            {
                Checks.PrepareHiddenControl(dialog); dialog.partGrid.Rows[0].Cells["StartPercent"].Value = "50"; dialog.FlushDefinition(); dialog.colorMode.SelectedItem = "Embedded"; dialog.PlaceSelected();
                check(dialog.Result.Effects.Count == 6 && editable.Effects.Count == 0 && dialog.PlacementRequested, "custom dialog flushes its grid and commits a materialized placement only to Result");
            }
            using (CustomEffectsDialog dialog = new CustomEffectsDialog(editable, new Selection(), Color.White))
            {
                Checks.PrepareHiddenControl(dialog); dialog.definitionName.Text = "Recipe without a selection"; dialog.FlushDefinition();
                check(dialog.Result.CustomEffects[0].Name == "Recipe without a selection", "custom definition management works before a grid rectangle is chosen");
                reject(delegate { dialog.PlaceSelected(); }, "placement without a rectangle reports an error without affecting the source");
                InvokeButton(dialog, "Delete");
                check(dialog.Result.CustomEffects.Count == 0, "definition manager can delete its last recipe without injecting an unwanted replacement");
            }
            using (CustomEffectsDialog blank = new CustomEffectsDialog(Model.Empty(), new Selection(), Color.White))
            {
                Checks.PrepareHiddenControl(blank);
                check(blank.Result.CustomEffects.Count == 0, "an empty project opens an empty Custom manager instead of silently adding a preset");
                InvokeButton(blank, "New"); check(blank.Result.CustomEffects.Count == 1 && blank.partGrid.Rows.Count > 0, "New explicitly creates a named editable serial recipe");
            }
            string clean = Path.Combine(directory, "ui-library.hlsrlib"); EffectLibraries.Save(library, clean);
            using (EffectLibraryDialog dialog = new EffectLibraryDialog(editable, Box(editable, 1, 0, 1000)))
            {
                Checks.PrepareHiddenControl(dialog); dialog.OpenLibrary(clean); dialog.libraryItems.SelectedIndex = 1;
                InvokeButton(dialog, "Import Custom");
                check(dialog.ProjectChanged && dialog.Result.CustomEffects.Count == editable.CustomEffects.Count + 1 && editable.CustomEffects.Count == 1, "library import produces an isolated project definition copy");
                InvokeButton(dialog, "Close"); check(dialog.DialogResult == DialogResult.OK, "Import Custom then Close returns an accepting result for MainForm to commit");
            }
            using (EffectLibraryDialog dialog = new EffectLibraryDialog(editable, Box(editable, 1, 0, 1000)))
            {
                Checks.PrepareHiddenControl(dialog); dialog.OpenLibrary(clean); dialog.libraryItems.SelectedIndex = 1; InvokeButton(dialog, "Import Custom"); InvokeButton(dialog, "Cancel");
                check(dialog.DialogResult == DialogResult.Cancel && editable.CustomEffects.Count == 1, "Cancel leaves imported definitions out of the caller project");
            }
            using (EffectLibraryDialog dialog = new EffectLibraryDialog(editable, new Selection()))
            {
                Checks.PrepareHiddenControl(dialog); dialog.OpenLibrary(clean); dialog.libraryItems.SelectedIndex = 1;
                dialog.partGrid.Rows[0].Cells["LengthCells"].Value = "invalid";
                reject(delegate { dialog.libraryItems.SelectedIndex = 0; }, "invalid stored-custom edits are guarded during cross-item selection");
                check(dialog.libraryItems.SelectedIndex == 1, "failed library item selection restores the previous editor/entry pairing");
            }
            using (MainForm main = new MainForm(new EditSession(project)))
            {
                main.RecoveryEnabled = false; main.ArmLibraryInsertion(group); main.InsertPendingAt(0, 2000);
                check(main.Session.Project.Effects.Count == group.Group.Effects.Count && main.Session.CanUndo, "main library anchor commits the entire insert as one undoable edit");
                main.Session.Undo(); check(main.Session.Project.Effects.Count == 0, "undo removes the complete library insertion");
            }
            Project legacy = Model.Seed(); Effect ramp = legacy.Effects.Find(delegate(Effect f) { return f.Kind == "RampUp"; });
            byte[] oldFrame = Model.RenderFrame(legacy, ramp.StartMs + ramp.DurationMs - legacy.FrameMs);
            using (BasicEffectEditor dialog = new BasicEffectEditor(legacy, ramp.Id))
            {
                typeof(BasicEffectEditor).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[0]);
                check(!dialog.Result.Effects.Find(delegate(Effect f) { return f.Id == ramp.Id; }).UseEndpoints && oldFrame.SequenceEqual(Model.RenderFrame(dialog.Result, ramp.StartMs + ramp.DurationMs - legacy.FrameMs)), "editing a legacy ramp without authoring endpoints preserves its previous rendering");
            }
            Render(project, directory);
        }
        private static void InvokeButton(Control root, string text) { Button button = Descendants(root).OfType<Button>().First(delegate(Button b) { return b.Text == text; }); typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty }); }
        private static List<Control> Descendants(Control root) { List<Control> result = new List<Control>(); foreach (Control child in root.Controls) { result.Add(child); result.AddRange(Descendants(child)); } return result; }
        private static void Render(Project project, string directory)
        {
            using (CustomEffectsDialog form = new CustomEffectsDialog(project, Box(project, 2, 0, 2000), Color.Orange))
            using (Bitmap image = new Bitmap(form.Width, form.Height)) { Checks.PrepareHiddenControl(form); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "CustomEffectsDialog.png"), System.Drawing.Imaging.ImageFormat.Png); }
            using (EffectLibraryDialog form = new EffectLibraryDialog(project, new Selection()))
            using (Bitmap image = new Bitmap(form.Width, form.Height)) { Checks.PrepareHiddenControl(form); form.OpenLibrary(Path.Combine(directory, "handcrafted.hlsrlib")); form.libraryItems.SelectedIndex = 1; form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "EffectLibraryDialog.png"), System.Drawing.Imaging.ImageFormat.Png); }
        }
    }
}
