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

        /// <summary>Creates the instance a single solve iteration runs on.</summary>
        public object NewInstance() => Activator.CreateInstance(Signature.ScriptType);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose()
        {
            _context?.Unload();
            _context = null;
        }
    }
}
