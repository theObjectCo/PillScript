using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PillScript.Scripting
{
    internal sealed class PackageResolution
    {
        /// <summary>Assemblies Roslyn compiles against.</summary>
        public List<string> CompileReferences = new List<string>();

        /// <summary>Assemblies the load context must be able to find while the script runs.</summary>
        public List<string> RuntimeAssemblies = new List<string>();

        public List<CompileDiagnostic> Problems = new List<CompileDiagnostic>();

        /// <summary>
        /// Records something the author has to fix. Everything resolving can complain about is
        /// declared in the project file, so that file is named in the report.
        /// </summary>
        public void Fault(string severity, string id, string message)
            => Problems.Add(new CompileDiagnostic
            {
                File = ScriptProject.ProjectFile,
                Line = 1,
                Column = 1,
                Severity = severity,
                Id = id,
                Message = message
            });
    }

    /// <summary>
    /// Resolves the references in the script's csproj to assembly paths. Plain DLLs come from
    /// their hint path, neighbouring projects are built, and NuGet packages are restored with the
    /// .NET SDK when the list has changed since the last restore.
    /// </summary>
    internal static class PackageResolver
    {
        const string StampFile = "packages.stamp";

        public static PackageResolution Resolve(ScriptProject project)
        {
            var resolution = new PackageResolution();

            var document = ReadProjectFile(project, resolution);
            if (document == null) return resolution;

            ReadDirectReferences(document, project, resolution);
            ReadProjectReferences(document, project, resolution);

            var packages = ReadPackageReferences(document);
            if (packages.Count == 0) return resolution;

            var folder = project.WorkingFolder;
            var assets = Path.Combine(folder, "obj", "project.assets.json");
            var stamp = Path.Combine(folder, "obj", StampFile);
            var wanted = string.Join(";", packages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

            // A restore takes seconds even with nothing to do, so the list it last ran for is
            // recorded and compared.
            if (!File.Exists(assets) || !File.Exists(stamp) || File.ReadAllText(stamp) != wanted)
            {
                if (!Restore(folder, resolution)) return resolution;

                Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                File.WriteAllText(stamp, wanted);
            }

            RestoreAssets.Read(assets, resolution);
            return resolution;
        }

        static XDocument ReadProjectFile(ScriptProject project, PackageResolution resolution)
        {
            var csproj = project.Find(ScriptProject.ProjectFile);
            if (csproj == null) return null;

            try
            {
                return XDocument.Parse(csproj.Content);
            }
            catch (Exception exception)
            {
                resolution.Fault("error", "SS0100",
                    "The project file could not be read: " + exception.Message);

                return null;
            }
        }

        /// <summary>
        /// Reference elements with a HintPath. This is how a script reaches a DLL that is not on
        /// NuGet, such as another plugin's assembly or a library beside the definition.
        /// </summary>
        static void ReadDirectReferences(
            XDocument document, ScriptProject project, PackageResolution resolution)
        {
            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "Reference"))
            {
                var hint = element.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value;
                if (string.IsNullOrWhiteSpace(hint)) continue;

                hint = hint.Trim();
                var path = Path.IsPathRooted(hint) ? hint : Path.Combine(project.WorkingFolder, hint);

                if (!File.Exists(path))
                {
                    resolution.Fault("warning", "SS0106",
                        "The reference " + hint + " was not found at " + path + ".");

                    continue;
                }

                resolution.CompileReferences.Add(path);
                resolution.RuntimeAssemblies.Add(path);
            }
        }

        /// <summary>
        /// ProjectReference entries, for a library kept alongside the script. Restore alone does
        /// not produce an assembly for them, so each one is built and its output referenced.
        /// </summary>
        static void ReadProjectReferences(
            XDocument document, ScriptProject project, PackageResolution resolution)
        {
            var references = document.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string)e.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .ToList();

            if (references.Count == 0) return;

            var dotnet = DotnetSdk.Find();
            if (dotnet == null)
            {
                resolution.Fault("error", "SS0107",
                    "ProjectReference needs the .NET SDK, which was not found on this machine.");

                return;
            }

            foreach (var include in references)
            {
                var path = Path.IsPathRooted(include)
                    ? include
                    : Path.GetFullPath(Path.Combine(project.WorkingFolder, include.Trim()));

                if (!File.Exists(path))
                {
                    resolution.Fault("error", "SS0108",
                        "The referenced project was not found at " + path + ".");

                    continue;
                }

                BuildReferencedProject(dotnet, path, resolution);
            }
        }

        static void BuildReferencedProject(string dotnet, string path, PackageResolution resolution)
        {
            var arguments = new[] { "build", path, "-c", "Release", "--nologo", "-getProperty:TargetPath" };

            if (!DotnetSdk.Run(dotnet, arguments, Path.GetDirectoryName(path), out var output))
            {
                resolution.Fault("error", "SS0109",
                    "Building " + Path.GetFileName(path) + " failed. " + DotnetSdk.Tail(output));

                return;
            }

            var target = DotnetSdk.Lines(output)
                .LastOrDefault(line => line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                resolution.Fault("error", "SS0110",
                    "Building " + Path.GetFileName(path) + " produced no assembly to reference.");

                return;
            }

            resolution.CompileReferences.Add(target);
            resolution.RuntimeAssemblies.Add(target);

            // Its own dependencies sit beside it, and the load context has to find those too.
            var folder = Path.GetDirectoryName(target);
            if (folder == null) return;

            foreach (var sibling in Directory.EnumerateFiles(folder, "*.dll"))
                resolution.RuntimeAssemblies.Add(sibling);
        }

        static List<string> ReadPackageReferences(XDocument document)
        {
            var packages = new List<string>();

            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var id = (string)element.Attribute("Include") ?? (string)element.Attribute("Update");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var version = (string)element.Attribute("Version")
                              ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

                packages.Add(string.IsNullOrWhiteSpace(version) ? id : id + "/" + version);
            }

            return packages;
        }

        static bool Restore(string folder, PackageResolution resolution)
        {
            var dotnet = DotnetSdk.Find();
            if (dotnet == null)
            {
                resolution.Fault("error", "SS0101",
                    "PackageReference needs the .NET SDK, which was not found on this machine. " +
                    "Install it from https://dotnet.microsoft.com/download or remove the package references.");

                return false;
            }

            var arguments = new[] { "restore", ScriptProject.ProjectFile, "--nologo" };

            if (!DotnetSdk.Run(dotnet, arguments, folder, out var output))
            {
                resolution.Fault("error", "SS0103", "Restoring packages failed. " + DotnetSdk.Tail(output));
                return false;
            }

            return true;
        }
    }
}
