using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PillScript.Scripting
{
    /// <summary>
    /// What a script is compiled under, kept in one place so that a squiggle in the editor and an
    /// error from Compile are the same thing said twice rather than two different opinions.
    /// </summary>
    internal static class CompilationRules
    {
        /// <summary>
        /// Warnings a script has no way to act on.
        ///
        /// CS1701 and CS1702 are about assembly versions not matching what something was built
        /// against. Here the references are whichever assemblies Rhino already has loaded, and
        /// the runtime settled the question before the script was written: there is no binding
        /// redirect to add and nothing to fix.
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
        /// Nullable annotations are on while their warnings stay off, which is the combination a
        /// script wants: writing <c>out Brep? brep</c> for an output that is sometimes empty is
        /// worth saying, and being asked to annotate the rest of the file for it is not.
        /// </summary>
        static CSharpCompilationOptions Shared(CSharpCompilationOptions options)
            => options
                .WithNullableContextOptions(NullableContextOptions.Annotations)
                .WithSpecificDiagnosticOptions(Quiet);
    }
}
