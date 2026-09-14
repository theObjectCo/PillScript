using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace PillScript.Scripting
{
    /// <summary>A stretch of one of the script's own files.</summary>
    internal class SourceSpan
    {
        public string File;
        public int Line;
        public int Column;
        public int EndLine;
        public int EndColumn;
    }

    /// <summary>A stretch of a file, and what should replace it.</summary>
    internal sealed class SourceEdit : SourceSpan
    {
        public string Text;
    }

    /// <summary>
    /// The requests that concern a symbol instead of a position: where it is declared, everywhere
    /// it is used, and what renaming it would change. All three run against the same workspace the
    /// completions come from, so completion and navigation see the same code.
    /// </summary>
    internal sealed partial class ScriptLanguageService
    {
        /// <summary>
        /// Where the symbol under the caret is declared. Only the script's own files can be
        /// resolved: a type from RhinoCommon is declared in an assembly, and there is no file to
        /// open for it.
        /// </summary>
        public Task<List<SourceSpan>> DefineAsync(string file, string text, int offset)
        {
            return WithDocument(file, text, async document =>
            {
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset);
                if (symbol == null) return new List<SourceSpan>();

                return Spans(document.Project.Solution, symbol.Locations);
            }, new List<SourceSpan>());
        }

        /// <summary>Everywhere the symbol under the caret is used, including its declaration.</summary>
        public Task<List<SourceSpan>> ReferencesAsync(string file, string text, int offset)
        {
            return WithDocument(file, text, async document =>
            {
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset);
                if (symbol == null) return new List<SourceSpan>();

                var solution = document.Project.Solution;
                var found = new List<SourceSpan>(Spans(solution, symbol.Locations));

                foreach (var reference in await SymbolFinder.FindReferencesAsync(symbol, solution))
                {
                    foreach (var at in reference.Locations)
                    {
                        var span = Span(solution, at.Location);
                        if (span != null) found.Add(span);
                    }
                }

                return found;
            }, new List<SourceSpan>());
        }

        /// <summary>
        /// What renaming the symbol under the caret would change, as edits for the page to apply.
        /// Roslyn produces a new solution, and the difference against the current one is what is
        /// returned, which leaves the editor as the only writer of files.
        /// </summary>
        public Task<List<SourceEdit>> RenameAsync(string file, string text, int offset, string name)
        {
            return WithDocument(file, text, async document =>
            {
                var edits = new List<SourceEdit>();
                if (string.IsNullOrWhiteSpace(name)) return edits;

                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset);
                if (symbol == null) return edits;

                var solution = document.Project.Solution;
                var renamed = await Renamer.RenameSymbolAsync(
                    solution, symbol, new SymbolRenameOptions(), name);

                var touched = renamed.GetChanges(solution)
                    .GetProjectChanges()
                    .SelectMany(project => project.GetChangedDocuments());

                foreach (var id in touched)
                {
                    var before = solution.GetDocument(id);
                    var after = renamed.GetDocument(id);
                    if (before == null || after == null) continue;

                    var source = await before.GetTextAsync();

                    foreach (var change in await after.GetTextChangesAsync(before))
                        edits.Add(Edit(before.Name, source, change));
                }

                return edits;
            }, new List<SourceEdit>());
        }

        // ----- Roslyn positions as the page reads them -------------------------------------------

        static List<SourceSpan> Spans(Solution solution, IEnumerable<Location> locations)
            => locations.Select(location => Span(solution, location))
                .Where(span => span != null)
                .ToList();

        /// <summary>
        /// Finds the file a location belongs to by asking the solution which document holds the
        /// tree. The path on the location is empty here, since these documents were added to the
        /// workspace by name and never had one.
        /// </summary>
        static SourceSpan Span(Solution solution, Location location)
        {
            if (location == null || !location.IsInSource) return null;

            var id = solution.GetDocumentId(location.SourceTree);
            var document = id == null ? null : solution.GetDocument(id);
            if (document == null) return null;

            var span = location.GetLineSpan();

            return new SourceSpan
            {
                File = document.Name,
                Line = span.StartLinePosition.Line + 1,
                Column = span.StartLinePosition.Character + 1,
                EndLine = span.EndLinePosition.Line + 1,
                EndColumn = span.EndLinePosition.Character + 1
            };
        }

        static SourceEdit Edit(string file, SourceText source, TextChange change)
        {
            var span = source.Lines.GetLinePositionSpan(change.Span);

            return new SourceEdit
            {
                File = file,
                Line = span.Start.Line + 1,
                Column = span.Start.Character + 1,
                EndLine = span.End.Line + 1,
                EndColumn = span.End.Character + 1,
                Text = change.NewText ?? string.Empty
            };
        }
    }
}
