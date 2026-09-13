using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PillScript.Scripting
{
    /// <summary>
    /// Reads project.assets.json, the file a restore leaves behind naming which assembly from
    /// each package applies here. A package usually ships several builds of the same library, and
    /// the SDK has already chosen between them in this file.
    /// </summary>
    internal static class RestoreAssets
    {
        public static void Read(string assetsPath, PackageResolution resolution)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            }
            catch (Exception exception)
            {
                resolution.Fault("error", "SS0105",
                    "The restore output could not be read: " + exception.Message);
                return;
            }

            using (document) Read(document.RootElement, resolution);
        }

        static void Read(JsonElement root, PackageResolution resolution)
        {
            // The packages are somewhere under one of these, usually the global cache.
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
                // This is how a package declares that a framework is supported with nothing to add.
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
    }
}
