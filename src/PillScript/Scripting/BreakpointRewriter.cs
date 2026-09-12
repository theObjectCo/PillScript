using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PillScript.Scripting
{
    /// <summary>
    /// Puts a call to the debug session in front of every statement the editor marked, carrying
    /// the locals that are readable at that point. This is what a breakpoint is here: there is no
    /// debugger attached to Rhino, so the pause is compiled into the script itself.
    /// </summary>
    internal sealed class BreakpointRewriter : CSharpSyntaxRewriter
    {
        readonly SemanticModel _model;
        readonly string _file;
        readonly HashSet<int> _lines;

        BreakpointRewriter(SemanticModel model, string file, HashSet<int> lines)
        {
            _model = model;
            _file = file;
            _lines = lines;
        }

        /// <summary>Answers the rewritten tree, or the original when nothing on it is marked.</summary>
        public static SyntaxTree Apply(
            SyntaxTree tree, SemanticModel model, string file, IEnumerable<int> lines)
        {
            var wanted = new HashSet<int>(lines);
            if (wanted.Count == 0) return tree;

            var root = tree.GetRoot();
            var rewritten = new BreakpointRewriter(model, file, wanted).Visit(root);

            return rewritten == root
                ? tree
                : tree.WithRootAndOptions(rewritten, tree.Options);
        }

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            var visited = (BlockSyntax)base.VisitBlock(node);
            var statements = new List<StatementSyntax>();
            var changed = false;

            // The original node is used for positions, because the visited one has moved.
            for (var i = 0; i < node.Statements.Count; i++)
            {
                var original = node.Statements[i];
                var line = original.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                if (_lines.Contains(line) && !(original is LocalFunctionStatementSyntax))
                {
                    statements.Add(BreakCall(original, line));
                    changed = true;
                }

                // The call sits on the same line as the statement it guards, so every line number
                // the compiler reports afterwards still matches the file the author is looking at.
                statements.Add(_lines.Contains(line)
                    ? visited.Statements[i].WithLeadingTrivia(SyntaxFactory.Space)
                    : visited.Statements[i]);
            }

            return changed ? visited.WithStatements(SyntaxFactory.List(statements)) : visited;
        }

        StatementSyntax BreakCall(StatementSyntax statement, int line)
        {
            var arguments = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(Literal(_file)),
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(line))),
                SyntaxFactory.Argument(Locals(statement))
            };

            var call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::PillScript.DebugSession.Break"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

            return SyntaxFactory.ExpressionStatement(call)
                .WithLeadingTrivia(statement.GetLeadingTrivia())
                .WithTrailingTrivia(SyntaxFactory.Space);
        }

        /// <summary>
        /// Builds the name and value pairs handed to the session. Only symbols that are certainly
        /// assigned here are included, because reading an unassigned local would not compile, and
        /// only ones that can be boxed, which leaves out spans and pointers.
        /// </summary>
        ExpressionSyntax Locals(StatementSyntax statement)
        {
            var items = new List<ExpressionSyntax>();

            foreach (var symbol in Readable(statement))
            {
                items.Add(Literal(symbol.Name));
                items.Add(SyntaxFactory.CastExpression(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                    SyntaxFactory.IdentifierName(symbol.Name)));
            }

            var type = SyntaxFactory.ArrayType(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                        SyntaxFactory.OmittedArraySizeExpression()))));

            return SyntaxFactory.ArrayCreationExpression(type,
                SyntaxFactory.InitializerExpression(
                    SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList(items)));
        }

        IEnumerable<ISymbol> Readable(StatementSyntax statement)
        {
            DataFlowAnalysis flow;

            try
            {
                flow = _model.AnalyzeDataFlow(statement);
            }
            catch (Exception)
            {
                return Enumerable.Empty<ISymbol>();
            }

            if (flow == null || !flow.Succeeded) return Enumerable.Empty<ISymbol>();

            return flow.DefinitelyAssignedOnEntry
                .Where(InScope)
                .Where(CanBox)
                .GroupBy(symbol => symbol.Name)
                .Select(group => group.First())
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal);
        }

        static bool InScope(ISymbol symbol)
        {
            if (symbol is ILocalSymbol) return true;

            // The implicit this is in the flow analysis but is not a name the generated code can
            // use. An out parameter is fine as long as it has been assigned, which the caller has
            // already checked by taking only what is definitely assigned here.
            return symbol is IParameterSymbol parameter
                   && !parameter.IsThis
                   && SyntaxFacts.IsValidIdentifier(parameter.Name);
        }

        static bool CanBox(ISymbol symbol)
        {
            var type = symbol is ILocalSymbol local ? local.Type : ((IParameterSymbol)symbol).Type;

            if (type == null) return false;
            if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer) return false;
            if (type.IsRefLikeType) return false;

            return true;
        }

        static LiteralExpressionSyntax Literal(string value)
            => SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));
    }
}
