using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SharpScript.Scripting
{
    internal sealed class LocalReference
    {
        public string Name;
        public string Path;
        public bool Missing;
    }

    internal sealed class PackageEntry
    {
        public string Id;
        public string Version;
    }

    /// <summary>
    /// Reads and edits the references in the script's csproj: assemblies pointed at by path, and
    /// packages fetched from NuGet. This is the editing side of what PackageResolver reads when
    /// it builds, so both work on the one file the user can also open in an IDE.
    /// </summary>
    internal static class ProjectReferences
    {
        public static (List<LocalReference> Local, List<PackageEntry> Packages) Read(ScriptProject project)
        {
            var local = new List<LocalReference>();
            var packages = new List<PackageEntry>();

            var document = Load(project);
            if (document == null) return (local, packages);

            foreach (var element in Elements(document, "Reference"))
            {
                var hint = element.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value;
                if (string.IsNullOrWhiteSpace(hint)) continue;

                var path = Absolute(project, hint.Trim());

                local.Add(new LocalReference
                {
                    Name = (string)element.Attribute("Include") ?? System.IO.Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Missing = !File.Exists(path)
                });
            }

            foreach (var element in Elements(document, "PackageReference"))
            {
                var id = (string)element.Attribute("Include");
                if (string.IsNullOrWhiteSpace(id)) continue;

                packages.Add(new PackageEntry
                {
                    Id = id,
                    Version = (string)element.Attribute("Version")
                              ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                              ?? string.Empty
                });
            }

            return (local, packages);
        }

        public static string AddLocal(ScriptProject project, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "That file does not exist.";

            var document = Load(project);
            if (document == null) return "The project file could not be read.";

            var name = System.IO.Path.GetFileNameWithoutExtension(path);

            if (Elements(document, "Reference").Any(e =>
                    string.Equals((string)e.Attribute("Include"), name, StringComparison.OrdinalIgnoreCase)))
                return name + " is already referenced.";

            var reference = new XElement("Reference",
                new XAttribute("Include", name),
                new XElement("HintPath", path),
                new XElement("Private", "false"));

            Group(document).Add(reference);
            return Save(project, document);
        }

        public static string RemoveLocal(ScriptProject project, string name)
        {
            var document = Load(project);
            if (document == null) return "The project file could not be read.";

            var found = Elements(document, "Reference").FirstOrDefault(e =>
                string.Equals((string)e.Attribute("Include"), name, StringComparison.OrdinalIgnoreCase));

            if (found == null) return name + " is not referenced.";

            Remove(found);
            return Save(project, document);
        }

        public static string AddPackage(ScriptProject project, string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id)) return "A package needs a name.";

            var document = Load(project);
            if (document == null) return "The project file could not be read.";

            id = id.Trim();

            var existing = Elements(document, "PackageReference").FirstOrDefault(e =>
                string.Equals((string)e.Attribute("Include"), id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Naming a package that is already there is how its version gets changed.
                existing.SetAttributeValue("Version", Clean(version));
                return Save(project, document);
            }

            Group(document).Add(new XElement("PackageReference",
                new XAttribute("Include", id),
                new XAttribute("Version", Clean(version))));

            return Save(project, document);
        }

        public static string RemovePackage(ScriptProject project, string id)
        {
            var document = Load(project);
            if (document == null) return "The project file could not be read.";

            var found = Elements(document, "PackageReference").FirstOrDefault(e =>
                string.Equals((string)e.Attribute("Include"), id, StringComparison.OrdinalIgnoreCase));

            if (found == null) return id + " is not referenced.";

            Remove(found);
            return Save(project, document);
        }

        // ----- the file ------------------------------------------------------------------------

        static XDocument Load(ScriptProject project)
        {
            var file = project.Find(ScriptProject.ProjectFile);
            if (file == null) return null;

            try { return XDocument.Parse(file.Content); }
            catch (Exception) { return null; }
        }

        /// <summary>Writes the project back. Answers null when it went through, a reason when not.</summary>
        static string Save(ScriptProject project, XDocument document)
        {
            try
            {
                project.SetContent(ScriptProject.ProjectFile, document.ToString());
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        static IEnumerable<XElement> Elements(XDocument document, string name)
            => document.Descendants().Where(e => e.Name.LocalName == name).ToList();

        /// <summary>Takes the last ItemGroup, or starts one when the project has none.</summary>
        static XElement Group(XDocument document)
        {
            var group = document.Root.Elements().LastOrDefault(e => e.Name.LocalName == "ItemGroup");
            if (group != null) return group;

            group = new XElement("ItemGroup");
            document.Root.Add(group);

            return group;
        }

        /// <summary>Removes an entry, and the group with it when that leaves the group empty.</summary>
        static void Remove(XElement element)
        {
            var group = element.Parent;
            element.Remove();

            if (group != null && group.Name.LocalName == "ItemGroup" && !group.HasElements) group.Remove();
        }

        static string Absolute(ScriptProject project, string hint)
            => System.IO.Path.IsPathRooted(hint)
                ? hint
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(project.WorkingFolder, hint));

        static string Clean(string version)
            => string.IsNullOrWhiteSpace(version) ? "*" : version.Trim();
    }
}
