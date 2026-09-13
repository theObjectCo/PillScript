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

        /// <summary>Only these are compiled. A note or the project file is not.</summary>
        public bool IsSource => Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Only C# is named, because that one is ours. Everything else is left for Monaco to work
        /// out from the extension, which saves keeping a list of them here.
        /// </summary>
        public string Language => IsSource ? "csharp" : string.Empty;
    }

    /// <summary>
    /// The sources belonging to one component instance, shaped as an SDK style project so an IDE
    /// can open the folder and give real completion. The document is the source of truth: files
    /// live inside the .gh file and are mirrored to a folder on disk, which is next door in
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

            // Not put back by EnsureProjectFile: the build does not need it, so somebody who
            // deletes it meant to.
            _files.Add(new ScriptFile(ReadmeFile, ProjectTemplates.Readme));
        }

        public Guid Id { get; set; }

        public IReadOnlyList<ScriptFile> Files => _files;

        /// <summary>The files Roslyn compiles.</summary>
        public IEnumerable<ScriptFile> SourceFiles => _files.Where(f => f.IsSource);

        public ScriptFile EntryFile
            => Find(DefaultEntry) ?? SourceFiles.FirstOrDefault() ?? _files.FirstOrDefault();

        /// <summary>Identifies the exact source state, so a component knows when it is stale.</summary>
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

        /// <summary>The Rhino side assemblies a script may use, which the compiler also takes.</summary>
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
        /// Adds an empty file and answers it, or null with a reason. It answers the file rather
        /// than a flag because the name it ends up with need not be the name that was asked for:
        /// a bare name gains an extension.
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
        /// A file may be anything; what it may not be is somewhere else. Rejecting a path is what
        /// keeps the mirror flat, which is what lets a name stand for a file without ambiguity.
        /// A bare name with no extension at all becomes C#, since that is what it usually meant.
        /// </summary>
        static string Normalise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (name != Path.GetFileName(name)) return null;

            // One project file, and it is the one already here.
            if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;

            return Path.GetExtension(name).Length == 0 ? name + ".cs" : name;
        }

        /// <summary>A copy to compile or restore on another thread while editing carries on here.</summary>
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
        /// Puts back the two files the build cannot do without, for a document saved before they
        /// existed or a folder somebody emptied.
        /// </summary>
        void EnsureProjectFile()
        {
            if (Find(ProjectFile) == null) _files.Add(new ScriptFile(ProjectFile, ProjectTemplates.Project));
            if (Find(UsingsFile) == null) _files.Add(new ScriptFile(UsingsFile, ProjectTemplates.Usings));
        }
    }
}
