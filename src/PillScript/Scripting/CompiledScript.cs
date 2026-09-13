using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PillScript.Scripting
{
    /// <summary>
    /// A script assembly that has been loaded and inspected: the signature it exposes and the
    /// handle needed to unload it.
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
        /// The single instance every call runs on. It is created once and kept, so a field on the
        /// script survives from one solve to the next as a static would, and a field initialiser
        /// runs once instead of on every iteration. Only a compile discards it, together with the
        /// rest of this build.
        /// </summary>
        public object Instance => _instance ??= Activator.CreateInstance(Signature.ScriptType);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose()
        {
            // Release the instance first. Its type comes from the context being unloaded, and the
            // context is only collected once nothing holds anything belonging to it.
            _instance = null;

            _context?.Unload();
            _context = null;
        }
    }
}
