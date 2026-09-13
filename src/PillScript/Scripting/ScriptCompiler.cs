using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace PillScript.Scripting
{
    internal sealed class CompileDiagnostic
    {
        public string File;
        public int Line;
        public int Column;
        public int EndLine;
        public int EndColumn;
        public string Severity;
        public string Id;
        public string Message;
    }

    internal sealed class CompileResult
    {
        public bool Success;
        public CompiledScript Script;

        /// <summary>What the script was compiled against, for the editor's reference list.</summary>
        public List<string> References = new List<string>();

        public TimeSpan Elapsed;
        public List<CompileDiagnostic> Diagnostics = new List<CompileDiagnostic>();
    }

    /// <summary>
    /// Compiles the project's sources into one collectible assembly with Roslyn. Compilation is
    /// explicit, and nothing here runs as part of a Grasshopper solution.
    /// </summary>
    internal static class ScriptCompiler
    {

        public static CompileResult Compile(
            ScriptProject project, IReadOnlyDictionary<string, IReadOnlyList<int>> breakpoints = null)
        {
            var result = new CompileResult();

            // The restore and any external IDE read the mirror, so it is written first.
            project.MirrorToDisk();

            var started = System.Diagnostics.Stopwatch.StartNew();

            var references = ReferenceSet.Build(project);
            result.Diagnostics.AddRange(references.Problems);
            result.References.AddRange(references.Names);

            if (references.Failed) return result;

            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            // GlobalUsings.cs is an ordinary source file and arrives with the rest.
            var trees = new List<SyntaxTree>();

            foreach (var file in project.SourceFiles)
                trees.Add(CSharpSyntaxTree.ParseText(Source(file.Content), parseOptions, file.Name));

            var options = CompilationRules.Build();

            var assemblyName = "Script_" + project.Id.ToString("N") + "_" + DateTime.UtcNow.Ticks.ToString("x");
            var compilation = CSharpCompilation.Create(
                assemblyName, trees, references.Compile, options);

            compilation = Instrument(compilation, breakpoints);

            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();

            EmitResult emit = compilation.Emit(peStream, pdbStream,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

            foreach (var diagnostic in emit.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Hidden) continue;
                result.Diagnostics.Add(Describe(diagnostic));
            }

            result.Elapsed = started.Elapsed;

            if (!emit.Success) return result;

            peStream.Position = 0;
            pdbStream.Position = 0;

            var context = new ScriptLoadContext(assemblyName);
            context.AddProbingPaths(references.RuntimeAssemblies);

            try
            {
                var assembly = context.LoadFromStream(peStream, pdbStream);
                result.Script = CompiledScript.Create(context, assembly, project.Hash);
                result.Success = true;
                result.Elapsed = started.Elapsed;
            }
            catch (Exception exception)
            {
                context.Unload();
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    File = project.EntryFile?.Name ?? string.Empty,
                    Line = 1,
                    Column = 1,
                    Severity = "error",
                    Id = "SS0001",
                    Message = Flatten(exception)
                });
            }

            return result;
        }

        /// <summary>
        /// Compiles the breakpoints into the script. It runs on the finished compilation because
        /// the rewriter needs a semantic model to know which locals it may read.
        /// </summary>
        static CSharpCompilation Instrument(
            CSharpCompilation compilation, IReadOnlyDictionary<string, IReadOnlyList<int>> breakpoints)
        {
            if (breakpoints == null || breakpoints.Count == 0) return compilation;

            foreach (var tree in compilation.SyntaxTrees.ToList())
            {
                if (!breakpoints.TryGetValue(tree.FilePath ?? string.Empty, out var lines)) continue;
                if (lines.Count == 0) continue;

                var model = compilation.GetSemanticModel(tree);
                var rewritten = BreakpointRewriter.Apply(tree, model, tree.FilePath, lines);

                if (rewritten != tree) compilation = compilation.ReplaceSyntaxTree(tree, rewritten);
            }

            return compilation;
        }

        static Microsoft.CodeAnalysis.Text.SourceText Source(string text)
            => Microsoft.CodeAnalysis.Text.SourceText.From(text ?? string.Empty, Encoding.UTF8);

        static CompileDiagnostic Describe(Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetLineSpan();
            return new CompileDiagnostic
            {
                File = span.Path ?? string.Empty,
                Line = span.StartLinePosition.Line + 1,
                Column = span.StartLinePosition.Character + 1,
                EndLine = span.EndLinePosition.Line + 1,
                EndColumn = span.EndLinePosition.Character + 1,
                Severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning",
                Id = diagnostic.Id,
                Message = diagnostic.GetMessage()
            };
        }

        static string Flatten(Exception exception)
        {
            if (exception is ReflectionTypeLoadException load)
            {
                var inner = load.LoaderExceptions.Where(e => e != null).Select(e => e.Message).Distinct();
                return load.Message + " " + string.Join(" ", inner);
            }

            return exception.Message;
        }

    }
}
