using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PillScript.Scripting
{
    /// <summary>
    /// The mirror between the document and a folder on disk. It exists so an IDE can open the
    /// project and so the SDK has something to restore against; the document stays the source of
    /// truth, and anything written outside is read back in.
    /// </summary>
    internal sealed partial class ScriptProject
    {
        /// <summary>Past this a file belongs on disk, not inside the document.</summary>
        const long LargestFile = 1024 * 1024;

        /// <summary>Folder the project is mirrored into, and where packages are restored.</summary>
        public string WorkingFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillScript", "projects", Id.ToString("N"));

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

        /// <summary>Reads the working folder back in, for edits made outside the built in editor.</summary>
        public bool PullFromDisk()
        {
            var folder = WorkingFolder;
            if (!Directory.Exists(folder)) return false;

            var before = Hash;
            var onDisk = EnumerateProjectFiles(folder)
                .ToDictionary(Path.GetFileName, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

            // An empty folder is somebody else's doing, not an instruction to empty the script.
            if (onDisk.Count == 0) return false;

            _files.RemoveAll(f => !onDisk.ContainsKey(f.Name));
            foreach (var pair in onDisk) SetContent(pair.Key, pair.Value);

            EnsureProjectFile();
            return Hash != before;
        }

        /// <summary>
        /// The project's own files: whatever sits in the folder itself, less the generated ones.
        /// Build output is left out by only looking at the top level, since that all lives in
        /// obj and bin.
        /// </summary>
        static IEnumerable<string> EnumerateProjectFiles(string folder)
            => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !Generated.Contains(Path.GetFileName(path)))
                .Where(CanCarry);

        /// <summary>
        /// Whether a file can live inside the document. Everything here is held and mirrored as
        /// text, so something that is not text would come back mangled, and a referenced DLL put
        /// beside the project is exactly the kind of thing that would be. Anything this refuses is
        /// left alone on disk rather than read in or deleted.
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

                // A zero byte this early is what tells an assembly from a source file.
                for (var i = 0; i < read; i++)
                {
                    if (block[i] == 0) return false;
                }

                return true;
            }
            catch (Exception)
            {
                // Locked, or gone between listing and opening. Either way, not ours to carry.
                return false;
            }
        }

        /// <summary>
        /// Leaves a file alone when it already says this, so the timestamps an IDE and the SDK
        /// both watch only move when something actually changed.
        /// </summary>
        static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content) return;

            File.WriteAllText(path, content, Encoding.UTF8);
        }
    }
}
