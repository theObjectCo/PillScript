using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PillScript.Scripting
{
    /// <summary>
    /// The mirror between the document and a folder on disk. It exists so an IDE can open the
    /// project and so the SDK has something to restore against. The document remains the source of
    /// truth, and anything written to the folder from outside is read back in.
    /// </summary>
    internal sealed partial class ScriptProject
    {
        /// <summary>A file larger than this stays on disk and is not held in the document.</summary>
        const long LargestFile = 1024 * 1024;

        /// <summary>Folder the project is mirrored into, and where packages are restored.</summary>
        public string WorkingFolder => ProjectCache.FolderFor(Id);

        /// <summary>
        /// Writes the project to the working folder, clears out files that were deleted and
        /// regenerates the import pointing the IDE at this machine's Rhino installation.
        /// </summary>
        public void MirrorToDisk()
        {
            var folder = WorkingFolder;
            Directory.CreateDirectory(folder);

            var expected = new HashSet<string>(_files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var path in EnumerateProjectFiles(folder))
            {
                if (!expected.Contains(Path.GetFileName(path))) File.Delete(path);
            }

            foreach (var file in _files) WriteIfChanged(Path.Combine(folder, file.Name), file.Content);

            WriteIfChanged(Path.Combine(folder, GeneratedTargets), ProjectTemplates.BuildTargets());
        }

        /// <summary>Reads the working folder back in, picking up edits made outside the editor.</summary>
        public bool PullFromDisk()
        {
            var folder = WorkingFolder;
            if (!Directory.Exists(folder)) return false;

            var before = Hash;
            var onDisk = EnumerateProjectFiles(folder)
                .ToDictionary(Path.GetFileName, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

            // An empty folder means something else removed the files, not that the script should
            // be emptied.
            if (onDisk.Count == 0) return false;

            _files.RemoveAll(f => !onDisk.ContainsKey(f.Name));
            foreach (var pair in onDisk) SetContent(pair.Key, pair.Value);

            EnsureProjectFile();
            return Hash != before;
        }

        /// <summary>
        /// The project's own files: the contents of the folder itself, minus the generated ones.
        /// Only the top level is listed, which leaves out build output, since that all sits in obj
        /// and bin.
        /// </summary>
        static IEnumerable<string> EnumerateProjectFiles(string folder)
            => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !Generated.Contains(Path.GetFileName(path)))
                .Where(CanCarry);

        /// <summary>
        /// Whether a file can be held inside the document. Files are stored and mirrored as text,
        /// so a binary would come back mangled; a referenced DLL sitting beside the project is the
        /// usual case. A file that fails this check is left alone on disk, neither read in nor
        /// deleted.
        /// </summary>
        static bool CanCarry(string path)
        {
            try
            {
                var length = new FileInfo(path).Length;
                if (length > LargestFile) return false;

                using var stream = File.OpenRead(path);

                var block = new byte[(int)Math.Min(length, 4096)];
                var read = stream.Read(block, 0, block.Length);

                // A zero byte in the first block distinguishes an assembly from a source file.
                for (var i = 0; i < read; i++)
                {
                    if (block[i] == 0) return false;
                }

                return true;
            }
            catch (Exception)
            {
                // Locked, or removed between the listing and the open. Not a file to take in.
                return false;
            }
        }

        /// <summary>
        /// Skips the write when the file already holds this text, so the timestamps an IDE and
        /// the SDK watch only move on a real change.
        /// </summary>
        static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content) return;

            File.WriteAllText(path, content, Encoding.UTF8);
        }
    }
}
