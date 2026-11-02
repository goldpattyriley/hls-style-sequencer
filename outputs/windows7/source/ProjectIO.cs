using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace HlsRecreation
{
    public static class ProjectIO
    {
        public static Project Load(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("Project file was not found.", path);
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.IgnoreWhitespace = true;
            // Sequencing data can legitimately exceed 16 MB. Stream XML instead of
            // rejecting by byte size; Model validation owns the structural limits.
            settings.MaxCharactersInDocument = 0;
            Project result;
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SequenceXml.Preflight(input, "Project");
                using (XmlReader reader = XmlReader.Create(input, settings)) result = (Project)new XmlSerializer(typeof(Project)).Deserialize(reader);
            }
            // Earlier native builds have no saved recipes or endpoint ramps. Upgrade
            // the in-memory copy; reading never rewrites the original file.
            if (result != null && (result.SchemaVersion == 2 || result.SchemaVersion == 3)) result.SchemaVersion = 4;
            Model.RequireValid(result);
            return result;
        }

        public static void Save(Project project, string path)
        {
            Model.RequireValid(project);
            string full = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(full);
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = new UTF8Encoding(false);
                settings.Indent = true;
                using (FileStream file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(file, settings))
                        new XmlSerializer(typeof(Project)).Serialize(writer, project);
                    // XmlWriter does not own the stream by default. Flush the file before replace.
                    file.Flush(true);
                }
                using (FileStream verify = File.OpenRead(temporary)) SequenceXml.Preflight(verify, "Project");
                if (File.Exists(full)) File.Replace(temporary, full, full + ".bak", true);
                else File.Move(temporary, full);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
