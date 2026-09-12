using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace SharpScript.Scripting
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

        public bool IsSource => Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The sources belonging to one component instance, shaped as an SDK style project so an IDE
    /// can open the folder and give real completion. The document is the source of truth: files
    /// live inside the .gh file and are mirrored to a folder on disk to be edited and restored.
    /// </summary>
    internal sealed class ScriptProject
    {
        public const string DefaultEntry = "Script.cs";
        public const string ProjectFile = "Script.csproj";
        const string GeneratedTargets = "Directory.Build.targets";
        public const string UsingsFile = "GlobalUsings.cs";

        static readonly HashSet<string> Generated = new HashSet<string>(
            new[] { GeneratedTargets }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The usings every file in the script starts with. This is an ordinary project file, so
        /// it can be edited: adding a line here is how a script reaches a namespace it uses often
        /// without repeating the using in each file.
        /// </summary>
        public const string UsingsTemplate = @"// Namespaces every file in this script can use.
global using System;
global using System.Collections;
global using System.Collections.Generic;
global using System.Linq;
global using Rhino;
global using Rhino.Geometry;
global using Grasshopper;
global using Grasshopper.Kernel;
global using Grasshopper.Kernel.Data;
global using Grasshopper.Kernel.Types;
global using SharpScript;
";

        readonly List<ScriptFile> _files = new List<ScriptFile>();

        public ScriptProject()
        {
            Id = Guid.NewGuid();
            _files.Add(new ScriptFile(DefaultEntry, SourceTemplate));
            _files.Add(new ScriptFile(UsingsFile, UsingsTemplate));
            _files.Add(new ScriptFile(ProjectFile, ProjectTemplate));
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

        /// <summary>Folder the project is mirrored into, and where packages are restored.</summary>
        public string WorkingFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SharpScript", "projects", Id.ToString("N"));

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

        /// <summary>Adds an empty source file. Returns false when the name is taken or unusable.</summary>
        public bool AddFile(string name, out string error)
        {
            error = null;
            name = Normalise(name);

            if (name == null) { error = "A file name must be a plain name ending in .cs"; return false; }
            if (Find(name) != null) { error = name + " already exists."; return false; }

            _files.Add(new ScriptFile(name, "// " + name + Environment.NewLine));
            return true;
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

        /// <summary>Rejects paths and anything that is not a C# file, so the mirror stays flat.</summary>
        static string Normalise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (name != Path.GetFileName(name)) return null;
            if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;
            if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) name += ".cs";

            return name;
        }

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
                if (!expected.Contains(Path.GetFileName(path)))
                    File.Delete(path);
            }

            foreach (var file in _files) WriteIfChanged(Path.Combine(folder, file.Name), file.Content);

            WriteIfChanged(Path.Combine(folder, GeneratedTargets), BuildTargets());
        }

        /// <summary>Reads the working folder back in, for edits made outside the built in editor.</summary>
        public bool PullFromDisk()
        {
            var folder = WorkingFolder;
            if (!Directory.Exists(folder)) return false;

            var before = Hash;
            var onDisk = EnumerateProjectFiles(folder)
                .ToDictionary(Path.GetFileName, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

            if (onDisk.Count == 0) return false;

            _files.RemoveAll(f => !onDisk.ContainsKey(f.Name));
            foreach (var pair in onDisk) SetContent(pair.Key, pair.Value);

            EnsureProjectFile();
            return Hash != before;
        }

        /// <summary>The project's own files, leaving out generated ones and build output.</summary>
        static IEnumerable<string> EnumerateProjectFiles(string folder)
            => Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(folder, "*.csproj", SearchOption.TopDirectoryOnly))
                .Where(path => !Generated.Contains(Path.GetFileName(path)));

        static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            File.WriteAllText(path, content, Encoding.UTF8);
        }

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

            if (!SourceFiles.Any()) _files.Add(new ScriptFile(DefaultEntry, SourceTemplate));
            EnsureProjectFile();
        }

        /// <summary>
        /// Puts back the two files the build cannot do without, for a document saved before they
        /// existed or a folder somebody emptied.
        /// </summary>
        void EnsureProjectFile()
        {
            if (Find(ProjectFile) == null) _files.Add(new ScriptFile(ProjectFile, ProjectTemplate));
            if (Find(UsingsFile) == null) _files.Add(new ScriptFile(UsingsFile, UsingsTemplate));
        }

        /// <summary>
        /// Points the project at the Rhino that is running right now rather than at a NuGet
        /// package of some other version. Regenerated on every mirror, which is what keeps the
        /// csproj the user edits free of machine specific paths.
        /// </summary>
        static string BuildTargets()
        {
            var references = HostReferences().Select(path => new XElement("Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                new XElement("HintPath", path),
                new XElement("Private", "false")));

            var document = new XDocument(
                new XComment(" Generated by SharpScript. Edits here are overwritten. "),
                new XElement("Project", new XElement("ItemGroup", references)));

            return document.ToString();
        }

        /// <summary>
        /// The Rhino side assemblies a script may use. The compiler takes the same list, so what
        /// the IDE resolves from the csproj and what the component compiles against stay equal.
        /// </summary>
        internal static IEnumerable<string> HostReferences()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Candidates())
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                if (seen.Add(Path.GetFileNameWithoutExtension(path))) yield return path;
            }
        }

        static IEnumerable<string> Candidates()
        {
            var rhinoCommon = typeof(Rhino.RhinoDoc).Assembly.Location;
            var grasshopper = typeof(Grasshopper.Kernel.GH_Component).Assembly.Location;

            yield return rhinoCommon;
            yield return grasshopper;
            yield return typeof(ScriptProject).Assembly.Location;

            var grasshopperFolder = Path.GetDirectoryName(grasshopper);
            if (grasshopperFolder != null) yield return Path.Combine(grasshopperFolder, "GH_IO.dll");

            var rhinoFolder = Path.GetDirectoryName(rhinoCommon);
            if (rhinoFolder == null) yield break;

            yield return Path.Combine(rhinoFolder, "Rhino.UI.dll");
            yield return Path.Combine(rhinoFolder, "Eto.dll");
        }

        public static string ProjectTemplate => @"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net7.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>Script</AssemblyName>
  </PropertyGroup>

  <!--
    Rhino, Grasshopper and the script API arrive through Directory.Build.targets, which
    SharpScript regenerates for whichever Rhino is running. Add NuGet packages below and press
    Compile: they are restored and used by the component as well as by the IDE.
  -->
  <ItemGroup>
    <!-- <PackageReference Include=""MathNet.Numerics"" Version=""5.0.0"" /> -->
  </ItemGroup>

</Project>
";

        public static string SourceTemplate => @"public class Script : ScriptBase
{
    public void RunScript(
        [Description(""Radius of the circle"")] [Default(10.0)] double radius,
        [Description(""Plane the circle sits on"")] [Optional] Plane plane,
        out Circle result)
    {
        if (!plane.IsValid) plane = Plane.WorldXY;

        result = new Circle(plane, radius);
        Print(""Circumference: {0:F2}"", result.Circumference);
    }
}
";
    }
}
