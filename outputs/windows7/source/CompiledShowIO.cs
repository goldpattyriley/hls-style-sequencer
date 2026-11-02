using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace HlsRecreation
{
    public sealed class ShowManifest
    {
        public int Version = 1, FrameMs, Channels, Frames;
        public string FseqFile = "", AudioFile = "", ProjectHash = "", FseqSha256 = "";
        public DateTime GeneratedUtc;
        public OutputSettings Output = new OutputSettings();
    }
    public static class CompiledShowIO
    {
        public static string Compile(Project project, string fseqPath)
        {
            Model.RequireValid(project); OutputPlan plan = ControllerOutput.Prepare(project);
            if (plan.Errors.Count != 0) throw new ArgumentException(String.Join(" ", plan.Errors.ToArray()));
            bool enabled = false; foreach (Controller controller in project.Output.Controllers) if (controller.Enabled) enabled = true;
            if (!enabled) throw new ArgumentException("Choose at least one enabled output method before compiling a show. File > Export FSEQ remains available without controllers.");
            string full = Path.GetFullPath(fseqPath), manifestPath = full + ".hlsrshow";
            if(!String.Equals(Path.GetExtension(full), ".fseq", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .fseq filename for compiled show data.");
            bool hadData = File.Exists(full);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string manifestTemporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Model.ExportFseq(project, temporary);
                ShowManifest manifest = new ShowManifest { FrameMs = project.FrameMs, Channels = Model.OutputSize(project), Frames = project.DurationMs / project.FrameMs, FseqFile = full, AudioFile = project.AudioFile, ProjectHash = Fingerprint(project), FseqSha256 = FileHash(temporary), GeneratedUtc = DateTime.UtcNow, Output = ControllerOutput.Clone(project.Output) };
                using (FileStream stream = new FileStream(manifestTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) new XmlSerializer(typeof(ShowManifest)).Serialize(writer, manifest);
                    stream.Flush(true);
                }
                Replace(temporary, full);
                try { Replace(manifestTemporary, manifestPath); }
                catch(Exception publishError)
                {
                    try { if(hadData) File.Replace(full + ".bak", full, null, true); else File.Delete(full); }
                    catch(Exception rollbackError) { throw new IOException("Compiled manifest could not be published and data rollback failed. Preserve the .bak files: " + rollbackError.Message, publishError); }
                    throw;
                }
                return manifestPath;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(manifestTemporary)) File.Delete(manifestTemporary); }
        }
        private static void Replace(string temporary, string destination) { if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak", true); else File.Move(temporary, destination); }
        public static string Fingerprint(Project project)
        {
            using (SHA256 hash = SHA256.Create())
            using (CryptoStream stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
            {
                using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = true, CloseOutput = false })) new XmlSerializer(typeof(Project)).Serialize(writer, project);
                string audio = project.AudioFile;
                if (!String.IsNullOrEmpty(audio) && File.Exists(audio)) { FileInfo file = new FileInfo(audio); byte[] identity = Encoding.UTF8.GetBytes(file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks); stream.Write(identity, 0, identity.Length); }
                stream.FlushFinalBlock(); return Hex(hash.Hash);
            }
        }
        public static bool IsCurrent(Project project, string manifestPath)
        {
            try
            {
                ShowManifest manifest;
                using (FileStream stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                { SequenceXml.Preflight(stream, "ShowManifest"); using (XmlReader reader = XmlReader.Create(stream, SequenceXml.Settings())) manifest = (ShowManifest)new XmlSerializer(typeof(ShowManifest)).Deserialize(reader); }
                return manifest.Version == 1 && !String.IsNullOrEmpty(manifest.FseqFile) && !manifest.FseqFile.StartsWith("\\\\", StringComparison.Ordinal) && File.Exists(manifest.FseqFile) && manifest.ProjectHash == Fingerprint(project) && manifest.FseqSha256 == FileHash(manifest.FseqFile);
            }
            catch { return false; }
        }
        private static string FileHash(string path) { using (SHA256 hash = SHA256.Create()) using (FileStream stream = File.OpenRead(path)) return Hex(hash.ComputeHash(stream)); }
        private static string Hex(byte[] data) { StringBuilder value = new StringBuilder(); foreach (byte b in data) value.Append(b.ToString("x2")); return value.ToString(); }
    }
}
