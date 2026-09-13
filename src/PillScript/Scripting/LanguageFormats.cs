using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;

namespace PillScript.Scripting
{
    internal sealed class CompletionEntry
    {
        public string Label;
        public string Kind;
        public string Detail;
        public string Insert;
        public string Sort;
        public string Filter;
        public int Index;
    }

    internal sealed class SignatureEntry
    {
        public string Label;
        public string Documentation;
        public List<string> Parameters = new List<string>();
    }

    /// <summary>
    /// Converts Roslyn's output into the shapes the editor reads: the icon names Monaco knows, a
    /// readable method signature, the summary from a documentation comment.
    /// </summary>
    internal static class LanguageFormats
    {
        public static readonly SymbolDisplayFormat Signature = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters
                           | SymbolDisplayMemberOptions.IncludeType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType
                              | SymbolDisplayParameterOptions.IncludeName
                              | SymbolDisplayParameterOptions.IncludeParamsRefOut
                              | SymbolDisplayParameterOptions.IncludeDefaultValue,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static readonly SymbolDisplayFormat Parameter = new SymbolDisplayFormat(
            parameterOptions: SymbolDisplayParameterOptions.IncludeType
                              | SymbolDisplayParameterOptions.IncludeName
                              | SymbolDisplayParameterOptions.IncludeParamsRefOut
                              | SymbolDisplayParameterOptions.IncludeDefaultValue,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        /// <summary>
        /// A completion triggered by typing a character is a different request from one triggered
        /// by Ctrl-Space, and Roslyn returns different results for each.
        /// </summary>
        public static CompletionTrigger Trigger(string trigger)
            => string.IsNullOrEmpty(trigger)
                ? CompletionTrigger.Invoke
                : CompletionTrigger.CreateInsertionTrigger(trigger[0]);

        public static CompileDiagnostic Describe(Diagnostic diagnostic)
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

        /// <summary>
        /// The summary line from a documentation comment, flattened. Parsing the XML by hand is
        /// enough for one element shown as a single line of text.
        /// </summary>
        public static string Summary(ISymbol symbol)
        {
            var xml = symbol.GetDocumentationCommentXml();
            if (string.IsNullOrWhiteSpace(xml)) return string.Empty;

            var start = xml.IndexOf("<summary>", StringComparison.Ordinal);
            var end = xml.IndexOf("</summary>", StringComparison.Ordinal);
            if (start < 0 || end < start) return string.Empty;

            start += "<summary>".Length;

            return string.Join(" ", xml.Substring(start, end - start)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));
        }

        /// <summary>Maps Roslyn's tags onto the names Monaco uses for completion icons.</summary>
        public static string Kind(ImmutableArray<string> tags)
        {
            foreach (var tag in tags)
            {
                switch (tag)
                {
                    case "Class": return "Class";
                    case "Structure": return "Struct";
                    case "Interface": return "Interface";
                    case "Enum": return "Enum";
                    case "EnumMember": return "EnumMember";
                    case "Delegate": return "Function";
                    case "Method":
                    case "ExtensionMethod": return "Method";
                    case "Property": return "Property";
                    case "Field": return "Field";
                    case "Event": return "Event";
                    case "Namespace": return "Module";
                    case "Keyword": return "Keyword";
                    case "Local":
                    case "Parameter":
                    case "RangeVariable": return "Variable";
                    case "Constant": return "Constant";
                    case "TypeParameter": return "TypeParameter";
                    case "Snippet": return "Snippet";
                    case "Operator": return "Operator";
                }
            }

            return "Text";
        }
    }
}
