using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;

namespace SharpScript.Scripting
{
    /// <summary>
    /// A Roslyn workspace over one component's sources, answering the questions an editor asks
    /// between compiles: what can be typed here, what is this, what are the arguments, and what is
    /// already wrong. It holds the same references the compiler uses, so it never claims a type
    /// exists that the build will then reject.
    /// </summary>
    internal sealed class ScriptLanguageService : IDisposable
    {
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, DocumentId> _documents =
            new Dictionary<string, DocumentId>(StringComparer.OrdinalIgnoreCase);

        AdhocWorkspace _workspace;
        ProjectId _projectId;
        ScriptProject _project;
        bool _stale = true;

        /// <summary>
        /// Marks the workspace for a rebuild. Needed when the file set or the references change;
        /// an edit inside a file goes through Update instead, which keeps Roslyn's cached work.
        /// </summary>
        public void Invalidate(ScriptProject project)
        {
            _project = project;
            _stale = true;
        }

        /// <summary>Applies an edit made in one file, so queries in the others see it.</summary>
        public void Update(string file, string text)
        {
            if (!_gate.Wait(0)) return;

            try
            {
                if (_stale || _workspace == null) return;
                if (!_documents.TryGetValue(file, out var documentId)) return;

                _workspace.TryApplyChanges(_workspace.CurrentSolution
                    .WithDocumentText(documentId, SourceText.From(text ?? string.Empty)));
            }
            catch (Exception)
            {
                _stale = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        // ----- what the editor asks --------------------------------------------------------------

        public async Task<List<CompletionEntry>> CompleteAsync(
            string file, string text, int offset, string trigger)
        {
            return await WithDocument(file, text, async document =>
            {
                var service = CompletionService.GetService(document);
                if (service == null) return new List<CompletionEntry>();

                var completions = await service.GetCompletionsAsync(
                    document, offset, LanguageFormats.Trigger(trigger));

                if (completions == null) return new List<CompletionEntry>();

                var entries = new List<CompletionEntry>();
                var index = 0;

                foreach (var item in completions.ItemsList)
                {
                    entries.Add(new CompletionEntry
                    {
                        Label = item.DisplayText,
                        Kind = LanguageFormats.Kind(item.Tags),
                        Detail = item.InlineDescription,
                        Insert = item.DisplayText,
                        Sort = item.SortText,
                        Filter = item.FilterText,
                        Index = index
                    });

                    index++;
                }

                return entries;
            }, new List<CompletionEntry>());
        }

        /// <summary>The documentation for one item, fetched only when the list highlights it.</summary>
        public async Task<string> DescribeCompletionAsync(
            string file, string text, int offset, string trigger, int index)
        {
            return await WithDocument(file, text, async document =>
            {
                var service = CompletionService.GetService(document);
                if (service == null) return string.Empty;

                var completions = await service.GetCompletionsAsync(
                    document, offset, LanguageFormats.Trigger(trigger));

                if (completions == null || index < 0 || index >= completions.ItemsList.Count)
                    return string.Empty;

                var description = await service.GetDescriptionAsync(document, completions.ItemsList[index]);
                return description?.Text ?? string.Empty;
            }, string.Empty);
        }

        /// <summary>Re-indents and re-spaces the file the way Roslyn's own formatter would.</summary>
        public async Task<string> FormatAsync(string file, string text)
        {
            return await WithDocument(file, text, async document =>
            {
                var formatted = await Formatter.FormatAsync(document);
                var source = await formatted.GetTextAsync();

                return source.ToString();
            }, text);
        }

        public async Task<string> HoverAsync(string file, string text, int offset)
        {
            return await WithDocument(file, text, async document =>
            {
                var service = QuickInfoService.GetService(document);
                if (service == null) return string.Empty;

                var info = await service.GetQuickInfoAsync(document, offset);
                if (info == null) return string.Empty;

                var parts = info.Sections
                    .Select(section => section.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t));

                return string.Join(Environment.NewLine + Environment.NewLine, parts);
            }, string.Empty);
        }

        public async Task<List<CompileDiagnostic>> DiagnoseAsync(string file, string text)
        {
            return await WithDocument(file, text, async document =>
            {
                var model = await document.GetSemanticModelAsync();
                if (model == null) return new List<CompileDiagnostic>();

                return model.GetDiagnostics()
                    .Where(d => d.Severity != DiagnosticSeverity.Hidden)
                    .Select(LanguageFormats.Describe)
                    .ToList();
            }, new List<CompileDiagnostic>());
        }

        /// <summary>
        /// Roslyn keeps its signature help internal, so this reads the invocation under the caret
        /// and formats the candidate methods off the semantic model.
        /// </summary>
        public async Task<(List<SignatureEntry> Signatures, int Active)> SignatureAsync(
            string file, string text, int offset)
        {
            return await WithDocument(file, text, async document =>
            {
                var empty = (new List<SignatureEntry>(), 0);

                var root = await document.GetSyntaxRootAsync();
                var model = await document.GetSemanticModelAsync();
                if (root == null || model == null) return empty;

                var node = root.FindToken(Math.Max(0, offset - 1)).Parent;
                var arguments = node?.AncestorsAndSelf().OfType<ArgumentListSyntax>().FirstOrDefault();

                if (arguments == null || !arguments.Span.Contains(Math.Max(0, offset - 1)))
                    return empty;

                var info = model.GetSymbolInfo(arguments.Parent);

                var candidates = info.Symbol != null
                    ? ImmutableArray.Create(info.Symbol)
                    : info.CandidateSymbols;

                var methods = candidates.OfType<IMethodSymbol>().ToList();
                if (methods.Count == 0) return empty;

                var active = arguments.Arguments.SeparatorCount == 0
                    ? 0
                    : arguments.Arguments.GetSeparators().Count(s => s.SpanStart < offset);

                var signatures = methods.Select(method => new SignatureEntry
                {
                    Label = method.ToDisplayString(LanguageFormats.Signature),
                    Documentation = LanguageFormats.Summary(method),
                    Parameters = method.Parameters
                        .Select(p => p.ToDisplayString(LanguageFormats.Parameter))
                        .ToList()
                }).ToList();

                return (signatures, active);
            }, (new List<SignatureEntry>(), 0));
        }

        // ----- the workspace ---------------------------------------------------------------------

        /// <summary>
        /// Applies the editor's current text to the document and runs a query on it. Requests are
        /// serialised, because a Roslyn workspace is not built for concurrent mutation.
        /// </summary>
        async Task<T> WithDocument<T>(string file, string text, Func<Document, Task<T>> query, T fallback)
        {
            await _gate.WaitAsync();

            try
            {
                Rebuild();

                if (!_documents.TryGetValue(file, out var documentId)) return fallback;

                var solution = _workspace.CurrentSolution
                    .WithDocumentText(documentId, SourceText.From(text ?? string.Empty));

                if (!_workspace.TryApplyChanges(solution)) return fallback;

                var document = _workspace.CurrentSolution.GetDocument(documentId);
                if (document == null) return fallback;

                return await query(document);
            }
            catch (Exception)
            {
                // A language service that throws must not take the editor down with it.
                return fallback;
            }
            finally
            {
                _gate.Release();
            }
        }

        void Rebuild()
        {
            if (!_stale || _project == null) return;

            _stale = false;
            _documents.Clear();

            _workspace?.Dispose();
            _workspace = new AdhocWorkspace();

            var references = ReferenceSet.Build(_project);

            _projectId = ProjectId.CreateNewId();

            var project = ProjectInfo.Create(
                _projectId,
                VersionStamp.Create(),
                name: "Script",
                assemblyName: "Script",
                language: LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                metadataReferences: references.Compile);

            _workspace.AddProject(project);

            foreach (var file in _project.SourceFiles) Add(file.Name, file.Content);
        }

        void Add(string name, string content)
        {
            var document = _workspace.AddDocument(_projectId, name, SourceText.From(content ?? string.Empty));
            _documents[name] = document.Id;
        }

        public void Dispose()
        {
            _workspace?.Dispose();
            _workspace = null;
        }
    }
}
