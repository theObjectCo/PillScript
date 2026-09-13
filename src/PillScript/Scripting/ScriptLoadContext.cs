using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace PillScript.Scripting
{
    /// <summary>
    /// A collectible context for one compiled script. Every recompile gets a fresh one and the
    /// previous one is unloaded, which is what stops a long editing session from accumulating a
    /// copy of the script assembly per Compile.
    /// </summary>
    internal sealed class ScriptLoadContext : AssemblyLoadContext
    {
        readonly Dictionary<string, string> _probing =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ScriptLoadContext(string name) : base($"PillScript.{name}", isCollectible: true) { }

        /// <summary>
        /// Registers the runtime assemblies of the NuGet packages the script asked for. Without
        /// this a script that compiles against a package would still fail to load it.
        /// </summary>
        public void AddProbingPaths(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                _probing[Path.GetFileNameWithoutExtension(path)] = path;
            }
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Returning null sends the lookup to the default context, which already holds Rhino,
            // Grasshopper and the framework. A second copy loaded here would make the RhinoCommon
            // types coming out of the script incompatible with the host's.
            if (_probing.TryGetValue(assemblyName.Name ?? string.Empty, out var path))
            {
                try { return LoadFromAssemblyPath(path); }
                catch (Exception) { return null; }
            }

            return null;
        }
    }
}
