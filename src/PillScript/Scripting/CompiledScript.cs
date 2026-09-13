using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PillScript.Scripting
{
    /// <summary>
    /// A script assembly that has been loaded and inspected: the signature it exposes plus
    /// the handle needed to throw it away again.
    /// </summary>
    internal sealed class CompiledScript : IDisposable
    {
        ScriptLoadContext _context;
        object _instance;

        CompiledScript(ScriptLoadContext context, Assembly assembly, ScriptSignature signature, string sourceHash)
        {
            _context = context;
            Assembly = assembly;
            Signature = signature;
            SourceHash = sourceHash;
        }

        public Assembly Assembly { get; }
        public ScriptSignature Signature { get; }

        /// <summary>Hash of the sources this was built from, used to spot a stale component.</summary>
        public string SourceHash { get; }

        public static CompiledScript Create(ScriptLoadContext context, Assembly assembly, string sourceHash)
            => new CompiledScript(context, assembly, ScriptSignature.Read(assembly), sourceHash);

        /// <summary>
        /// The one instance every call runs on. It is made once and kept, so a field on the script
        /// carries from one solve to the next the same way a static does, and a field initialiser
        /// runs once rather than on every iteration. Only compiling throws it away, along with
        /// everything else this build owns.
        /// </summary>
        public object Instance => _instance ??= Activator.CreateInstance(Signature.ScriptType);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose()
        {
            // Let go of the instance first: it is of a type from the context being unloaded, and
            // the context only comes apart once nothing holds anything belonging to it.
            _instance = null;

            _context?.Unload();
            _context = null;
        }
    }
}
