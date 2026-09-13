using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PillScript.Scripting
{
    internal sealed class ScriptFile
    {
        public ScriptFile(string name, string content)
        {
            Name = name;
            Content = content ?? string.Empty;
        }

        public string Name { get; set; }
        public string Content { get; set; }

        /// <summary>Only files with these extensions are compiled. Notes and the csproj are not.</summary>
        public bool IsSource => Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Only C# is listed here, since the editor treats it specially. Monaco works the rest
        /// out from the extension, which saves maintaining a list.
        /// </summary>
        public string Language => IsSource ? "csharp" : string.Empty;
    }

    /// <summary>
    /// The sources belonging to one component instance, laid out as an SDK style project so an
    /// IDE can open the folder and give completion. The document is the source of truth: the files
    /// are stored in the .gh file and mirrored to a folder on disk. The mirroring is in
    /// ScriptProject.Disk.cs.
    /// </summary>
    internal sealed partial class ScriptProject
    {
        public const string DefaultEntry = "Script.cs";
        public const string ProjectFile = "Script.csproj";
        public const string UsingsFile = "GlobalUsings.cs";
        public const string ReadmeFile = "readme.md";

        const string GeneratedTargets = "Directory.Build.targets";

        static readonly HashSet<string> Generated = new HashSet<string>(
            new[] { GeneratedTargets }, StringComparer.OrdinalIgnoreCase);

        readonly List<ScriptFile> _files = new List<ScriptFile>();

        public ScriptProject()
        {
            Id = Guid.NewGuid();

            _files.Add(new ScriptFile(DefaultEntry, ProjectTemplates.Source));
            _files.Add(new ScriptFile(UsingsFile, ProjectTemplates.Usings));
            _files.Add(new ScriptFile(ProjectFile, ProjectTemplates.Project));

            // Not restored by EnsureProjectFile. The build does not need the readme, so a
            // deletion is taken at face value.
            _files.Add(new ScriptFile(ReadmeFile, ProjectTemplates.Readme));
        }

        public Guid Id { get; set; }

        public IReadOnlyList<ScriptFile> Files => _files;

        /// <summary>The files Roslyn compiles.</summary>
        public IEnumerable<ScriptFile> SourceFiles => _files.Where(f => f.IsSource);

        public ScriptFile EntryFile
            => Find(DefaultEntry) ?? SourceFiles.FirstOrDefault() ?? _files.FirstOrDefault();

        /// <summary>Identifies the exact source state, which is how a component detects staleness.</summary>
        public string Hash
        {
            get
            {
                using var sha = SHA256.Create();
                var builder = new StringBuilder();

                foreach (var file in _files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine(file.Name).AppendLine(file.Content);

                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
            }
        }

        /// <summary>The Rhino assemblies a script may use. ScriptCompiler is given the same list.</summary>
        public static IEnumerable<string> HostReferences() => ProjectTemplates.HostReferences();

        public ScriptFile Find(string name)
            => _files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Files the editor refuses to rename or delete, because the build needs them.</summary>
        public static bool IsProtected(string name)
            => string.Equals(name, DefaultEntry, StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, ProjectFile, StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, UsingsFile, StringComparison.OrdinalIgnoreCase);

        public void SetContent(string name, string content)
        {
            var file = Find(name);

            if (file == null) _files.Add(new ScriptFile(name, content));
            else file.Content = content ?? string.Empty;
        }

        /// <summary>
        /// Adds an empty file and returns it, or returns null with a reason. It returns the file
        /// because the resulting name can differ from the one asked for: a bare name gains an
        /// extension.
        /// </summary>
        public ScriptFile AddFile(string name, out string error)
        {
            error = null;
            name = Normalise(name);

            if (name == null) { error = "A file name cannot be a path, and cannot be a second .csproj."; return null; }
            if (Find(name) != null) { error = name + " already exists."; return null; }

            var seed = name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? "// " + name + Environment.NewLine
                : string.Empty;

            var file = new ScriptFile(name, seed);
            _files.Add(file);

            return file;
        }

        public bool RenameFile(string from, string to, out string error)
        {
            error = null;

            var file = Find(from);
            if (file == null) { error = from + " is not part of this script."; return false; }
            if (IsProtected(file.Name)) { error = file.Name + " cannot be renamed."; return false; }

            var name = Normalise(to);
            if (name == null) { error = "A file name must be a plain name ending in .cs"; return false; }
            if (Find(name) != null) { error = name + " already exists."; return false; }

            file.Name = name;
            return true;
        }

        public bool RemoveFile(string name, out string error)
        {
            error = null;

            var file = Find(name);
            if (file == null) { error = name + " is not part of this script."; return false; }
            if (IsProtected(file.Name)) { error = file.Name + " cannot be removed."; return false; }

            _files.Remove(file);
            return true;
        }

        /// <summary>
        /// A file may have any name, but not a path. Rejecting paths keeps the mirror flat, which
        /// is what lets a bare name identify a file unambiguously. A name with no extension at all
        /// gets .cs, which is what it usually meant.
        /// </summary>
        static string Normalise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (name != Path.GetFileName(name)) return null;

            // There is one project file and it already exists.
            if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;

            return Path.GetExtension(name).Length == 0 ? name + ".cs" : name;
        }

        /// <summary>A snapshot to compile or restore on another thread while editing continues.</summary>
        public ScriptProject Clone()
        {
            var clone = new ScriptProject { Id = Id };
            clone._files.Clear();

            foreach (var file in _files) clone._files.Add(new ScriptFile(file.Name, file.Content));

            return clone;
        }

        public void Load(Guid id, IEnumerable<ScriptFile> files)
        {
            Id = id;

            _files.Clear();
            _files.AddRange(files);

            if (!SourceFiles.Any()) _files.Add(new ScriptFile(DefaultEntry, ProjectTemplates.Source));

            EnsureProjectFile();
        }

        /// <summary>
        /// Restores the two files the build requires, for a document saved before they existed or
        /// a folder that was emptied.
        /// </summary>
        void EnsureProjectFile()
        {
            if (Find(ProjectFile) == null) _files.Add(new ScriptFile(ProjectFile, ProjectTemplates.Project));
            if (Find(UsingsFile) == null) _files.Add(new ScriptFile(UsingsFile, ProjectTemplates.Usings));
        }
    }
}
