using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PillScript.Scripting
{
    /// <summary>
    /// The options a script is compiled under, kept in one place so that a squiggle in the editor
    /// and an error from Compile report the same diagnostic.
    /// </summary>
    internal static class CompilationRules
    {
        /// <summary>
        /// Warnings a script author has no way to act on.
        ///
        /// CS1701 and CS1702 report an assembly version that does not match what something was
        /// built against. The references here are the assemblies Rhino already has loaded, and the
        /// runtime resolved those versions before the script was written. There is no binding
        /// redirect to add.
        /// </summary>
        static readonly KeyValuePair<string, ReportDiagnostic>[] Quiet =
        {
            new KeyValuePair<string, ReportDiagnostic>("CS1701", ReportDiagnostic.Suppress),
            new KeyValuePair<string, ReportDiagnostic>("CS1702", ReportDiagnostic.Suppress)
        };

        /// <summary>What the component builds with.</summary>
        public static CSharpCompilationOptions Build()
            => Shared(new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                platform: Platform.X64,
                concurrentBuild: true));

        /// <summary>What the editor answers from between builds.</summary>
        public static CSharpCompilationOptions Edit()
            => Shared(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        /// <summary>
        /// Nullable annotations are on and their warnings are off. That combination allows
        /// <c>out Brep? brep</c> for an output that is sometimes empty, without requiring the rest
        /// of the file to be annotated.
        /// </summary>
        static CSharpCompilationOptions Shared(CSharpCompilationOptions options)
            => options
                .WithNullableContextOptions(NullableContextOptions.Annotations)
                .WithSpecificDiagnosticOptions(Quiet);
    }
}
