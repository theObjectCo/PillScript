using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace SharpScript.Scripting
{
    internal sealed class PackageResolution
    {
        /// <summary>Assemblies Roslyn compiles against.</summary>
        public List<string> CompileReferences = new List<string>();

        /// <summary>Assemblies the load context must be able to find while the script runs.</summary>
        public List<string> RuntimeAssemblies = new List<string>();

        public List<CompileDiagnostic> Problems = new List<CompileDiagnostic>();
    }

    /// <summary>
    /// Turns the PackageReference entries in the script's csproj into assembly paths, restoring
    /// them with the .NET SDK when the list has changed since the last time.
    /// </summary>
    internal static class PackageResolver
    {
        const string StampFile = "packages.stamp";
        static readonly TimeSpan SdkTimeout = TimeSpan.FromMinutes(3);

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

            if (!File.Exists(assets) || !File.Exists(stamp) || File.ReadAllText(stamp) != wanted)
            {
                if (!Restore(project, folder, resolution)) return resolution;

                Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                File.WriteAllText(stamp, wanted);
            }

            ReadAssets(assets, resolution);
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
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0100",
                    "The project file could not be read: " + exception.Message));
                return null;
            }
        }

        /// <summary>
        /// Reference elements with a HintPath, which is how a script reaches a DLL that is not on
        /// NuGet: another plugin's assembly, or a library sitting next to the definition.
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
                    resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "warning", "SS0106",
                        "The reference " + hint + " was not found at " + path + "."));
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

            var dotnet = FindDotnet();
            if (dotnet == null)
            {
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0107",
                    "ProjectReference needs the .NET SDK, which was not found on this machine."));
                return;
            }

            foreach (var include in references)
            {
                var path = Path.IsPathRooted(include)
                    ? include
                    : Path.GetFullPath(Path.Combine(project.WorkingFolder, include.Trim()));

                if (!File.Exists(path))
                {
                    resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0108",
                        "The referenced project was not found at " + path + "."));
                    continue;
                }

                BuildReferencedProject(dotnet, path, resolution);
            }
        }

        static void BuildReferencedProject(string dotnet, string path, PackageResolution resolution)
        {
            var arguments = new[]
            {
                "build", path, "-c", "Release", "--nologo", "-getProperty:TargetPath"
            };

            if (!Run(dotnet, arguments, Path.GetDirectoryName(path), out var output))
            {
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0109",
                    "Building " + Path.GetFileName(path) + " failed. " + Tail(output)));
                return;
            }

            var target = Lines(output)
                .LastOrDefault(line => line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0110",
                    "Building " + Path.GetFileName(path) + " produced no assembly to reference."));
                return;
            }

            resolution.CompileReferences.Add(target);
            resolution.RuntimeAssemblies.Add(target);

            // Its own dependencies sit next to it, and the load context needs to find them too.
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

        static bool Restore(ScriptProject project, string folder, PackageResolution resolution)
        {
            var dotnet = FindDotnet();
            if (dotnet == null)
            {
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0101",
                    "PackageReference needs the .NET SDK, which was not found on this machine. " +
                    "Install it from https://dotnet.microsoft.com/download or remove the package references."));
                return false;
            }

            if (!Run(dotnet, new[] { "restore", ScriptProject.ProjectFile, "--nologo" }, folder, out var output))
            {
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0103",
                    "Restoring packages failed. " + Tail(output)));
                return false;
            }

            return true;
        }

        /// <summary>Runs an SDK command and answers whether it succeeded, with its log.</summary>
        static bool Run(string dotnet, IReadOnlyList<string> arguments, string folder, out string output)
        {
            var startInfo = new ProcessStartInfo(dotnet)
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            var log = new StringBuilder();

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit((int)SdkTimeout.TotalMilliseconds))
                {
                    try { process.Kill(true); } catch (Exception) { }

                    output = "The command timed out.";
                    return false;
                }

                output = log.ToString();
                return process.ExitCode == 0;
            }
            catch (Exception exception)
            {
                output = exception.Message;
                return false;
            }
        }

        /// <summary>Reads the restore output and picks the compile and runtime assets per package.</summary>
        static void ReadAssets(string assetsPath, PackageResolution resolution)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            }
            catch (Exception exception)
            {
                resolution.Problems.Add(Problem(ScriptProject.ProjectFile, "error", "SS0105",
                    "The restore output could not be read: " + exception.Message));
                return;
            }

            using (document)
            {
                var root = document.RootElement;

                var folders = root.TryGetProperty("packageFolders", out var packageFolders)
                    ? packageFolders.EnumerateObject().Select(p => p.Name).ToList()
                    : new List<string>();

                var libraries = root.TryGetProperty("libraries", out var libraryElement)
                    ? libraryElement
                    : default;

                if (!root.TryGetProperty("targets", out var targets)) return;

                var target = targets.EnumerateObject()
                    .OrderByDescending(t => t.Name.StartsWith("net7.0", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (target.Value.ValueKind != JsonValueKind.Object) return;

                foreach (var entry in target.Value.EnumerateObject())
                {
                    var path = LibraryPath(libraries, entry.Name);
                    if (path == null) continue;

                    Collect(entry.Value, "compile", folders, path, resolution.CompileReferences);
                    Collect(entry.Value, "runtime", folders, path, resolution.RuntimeAssemblies);
                }
            }
        }

        static string LibraryPath(JsonElement libraries, string id)
        {
            if (libraries.ValueKind != JsonValueKind.Object) return null;
            if (!libraries.TryGetProperty(id, out var library)) return null;
            if (!library.TryGetProperty("path", out var path)) return null;

            return path.GetString();
        }

        static void Collect(
            JsonElement entry, string section, List<string> folders, string libraryPath, List<string> into)
        {
            if (!entry.TryGetProperty(section, out var assets)) return;

            foreach (var asset in assets.EnumerateObject())
            {
                if (asset.Name.EndsWith("_._", StringComparison.Ordinal)) continue;

                var relative = asset.Name.Replace('/', Path.DirectorySeparatorChar);

                foreach (var folder in folders)
                {
                    var candidate = Path.Combine(folder, libraryPath, relative);
                    if (!File.Exists(candidate)) continue;

                    into.Add(candidate);
                    break;
                }
            }
        }

        static string FindDotnet()
        {
            var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")
            };

            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            candidates.AddRange(pathVariable.Split(Path.PathSeparator)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => Path.Combine(folder.Trim(), "dotnet.exe")));

            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Keeps the last few lines of a tool log, which is where its complaint usually is.</summary>
        static string Tail(string text, int lines = 6)
        {
            var all = Lines(text).ToList();
            return string.Join(" ", all.Skip(Math.Max(0, all.Count - lines)));
        }

        static IEnumerable<string> Lines(string text)
            => (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);

        static CompileDiagnostic Problem(string file, string severity, string id, string message)
            => new CompileDiagnostic
            {
                File = file,
                Line = 1,
                Column = 1,
                Severity = severity,
                Id = id,
                Message = message
            };
    }
}
