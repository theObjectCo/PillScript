using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PillScript.Scripting
{
    /// <summary>
    /// The assemblies a script is compiled and completed against. The compiler and the language
    /// service share this so that what the editor understands and what the component builds are
    /// the same set, down to the package version.
    /// </summary>
    internal sealed class ReferenceSet
    {
        static readonly ConcurrentDictionary<string, (DateTime Stamp, MetadataReference Reference)> Cache
            = new ConcurrentDictionary<string, (DateTime, MetadataReference)>();

        public List<MetadataReference> Compile = new List<MetadataReference>();
        public List<string> RuntimeAssemblies = new List<string>();
        public List<CompileDiagnostic> Problems = new List<CompileDiagnostic>();

        public bool Failed => Problems.Any(p => p.Severity == "error");

        /// <summary>
        /// The references worth naming: Rhino, the script API and whatever the project asked for.
        /// The framework is left out because a list of three hundred entries tells nobody anything.
        /// </summary>
        public List<string> Names = new List<string>();

        /// <summary>
        /// Resolves the project's references: the framework, the Rhino side assemblies the
        /// generated import points at, and whatever restore produced. Sweeping in every assembly
        /// the process happens to have loaded would pull in a second copy of the Rhino geometry
        /// types from whichever other plugin brought its own.
        /// </summary>
        public static ReferenceSet Build(ScriptProject project)
        {
            var set = new ReferenceSet();
            var packages = PackageResolver.Resolve(project);

            set.Problems.AddRange(packages.Problems);
            set.RuntimeAssemblies.AddRange(packages.RuntimeAssemblies);

            if (set.Failed) return set;

            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Offer(string path, bool replace)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                var key = Path.GetFileNameWithoutExtension(path);
                if (replace || !paths.ContainsKey(key)) paths[key] = path;
                if (replace) named.Add(key);
            }

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            {
                foreach (var path in trusted.Split(Path.PathSeparator))
                    Offer(path, replace: false);
            }

            foreach (var path in ScriptProject.HostReferences())
                Offer(path, replace: true);

            foreach (var path in packages.CompileReferences)
                Offer(path, replace: true);

            set.Compile.AddRange(paths.Values.Select(Read).Where(reference => reference != null));
            set.Names.AddRange(named.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            return set;
        }

        /// <summary>
        /// Reads metadata once per file and keeps it until the file changes. A compile touches a
        /// few hundred assemblies and the editor asks again on every keystroke.
        /// </summary>
        static MetadataReference Read(string path)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);

                if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                    return cached.Reference;

                var reference = MetadataReference.CreateFromFile(path);
                Cache[path] = (stamp, reference);

                return reference;
            }
            catch (Exception)
            {
                // An assembly that cannot be read is one the script simply will not see.
                return null;
            }
        }
    }
}
