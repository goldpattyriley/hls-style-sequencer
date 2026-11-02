using System;
using System.IO;
using System.Reflection;

namespace HlsRecreation
{
    public static class BundledExamples
    {
        private const string ResourcePrefix = "HlsStyleSequencer.Examples.";
        private static readonly string[] AssetNames = new string[]
        {
            "demo.wav",
            "display-demo.hlsr",
            "display-demo.hlsrpreview",
            "custom-demo.hlsr",
            "handcrafted.hlsrlib"
        };

        public static string DefaultDirectory
        {
            get
            {
                string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (String.IsNullOrWhiteSpace(localApplicationData))
                    throw new InvalidOperationException("The local application-data folder is unavailable.");
                return Path.Combine(localApplicationData, "HlsStyleSequencer", "Examples", "0.4.1");
            }
        }

        public static string EnsureExamples(string targetDirectory)
        {
            string directory = ValidateTargetDirectory(targetDirectory);
            Directory.CreateDirectory(directory);
            foreach (string assetName in AssetNames) CopyIfMissing(directory, assetName);
            return Path.Combine(directory, "display-demo.hlsr");
        }

        private static string ValidateTargetDirectory(string targetDirectory)
        {
            string requested = String.IsNullOrWhiteSpace(targetDirectory) ? DefaultDirectory : targetDirectory;
            if (!Path.IsPathRooted(requested))
                throw new ArgumentException("Bundled examples require an absolute local directory.", "targetDirectory");
            if (requested.IndexOf("://", StringComparison.Ordinal) >= 0)
                throw new ArgumentException("Bundled examples cannot use a URL directory.", "targetDirectory");

            string full = Path.GetFullPath(requested);
            string root = Path.GetPathRoot(full);
            if (String.IsNullOrEmpty(root) || String.Equals(TrimTrailingSeparator(full), TrimTrailingSeparator(root), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A root directory cannot be used for bundled examples.", "targetDirectory");
            if (full.StartsWith("\\\\", StringComparison.Ordinal))
                throw new ArgumentException("Bundled examples cannot use a network directory.", "targetDirectory");
            return full;
        }

        private static string TrimTrailingSeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void CopyIfMissing(string directory, string assetName)
        {
            string destination = Path.Combine(directory, assetName);
            if (File.Exists(destination)) return;

            string temporary = Path.Combine(directory, "." + assetName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + assetName))
                {
                    if (source == null)
                        throw new InvalidOperationException("Bundled example resource '" + ResourcePrefix + assetName + "' is missing.");
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(output);
                        output.Flush(true);
                    }
                }

                try
                {
                    File.Move(temporary, destination);
                }
                catch (IOException)
                {
                    if (File.Exists(destination)) return;
                    throw;
                }
            }
            catch (Exception error)
            {
                if (error is InvalidOperationException) throw;
                throw new IOException("Unable to install bundled example '" + assetName + "'. " + error.Message, error);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception)
                {
                    // The next startup can retry this one file; do not hide the original failure.
                }
            }
        }
    }
}
