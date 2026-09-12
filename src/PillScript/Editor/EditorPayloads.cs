using System.Collections.Generic;
using System.Linq;
using PillScript.Components;
using PillScript.Scripting;

namespace PillScript.Editor
{
    /// <summary>Where the editor window sits relative to the Grasshopper canvas.</summary>
    internal readonly struct DockStatus
    {
        public DockStatus(bool docked, bool canDock, bool onLeft)
        {
            Docked = docked;
            CanDock = canDock;
            OnLeft = onLeft;
        }

        public bool Docked { get; }
        public bool CanDock { get; }
        public bool OnLeft { get; }
    }

    /// <summary>
    /// Turns what the component knows into the shapes the page reads. Keeping the wire format in
    /// one place means a field the page expects can be found without reading the whole editor.
    /// </summary>
    internal static class EditorPayloads
    {
        public static object Project(PillScriptComponent component)
            => new
            {
                type = "project",
                files = component.Project.Files.Select(file => new
                {
                    name = file.Name,
                    content = file.Content,
                    language = file.IsSource ? "csharp" : "xml",
                    locked = ScriptProject.IsProtected(file.Name)
                }),
                folder = component.Project.WorkingFolder,
                breakpoints = component.Breakpoints.ToDictionary(pair => pair.Key, pair => pair.Value),
                breakpointsEnabled = component.BreakpointsEnabled
            };

        public static object State(PillScriptComponent component, DockStatus dock)
            => new
            {
                type = "state",
                stale = component.IsStale,
                compiling = component.IsCompiling,
                title = component.OnPingDocument()?.DisplayName ?? "unsaved",
                build = component.LastBuild,
                runtime = "net7.0 · Roslyn",
                docked = dock.Docked,
                canDock = dock.CanDock,
                dockLeft = dock.OnLeft,
                references = component.References.Count,
                parameters = Parameters(component)
            };

        /// <summary>The parameter list the last successful build produced, for the sidebar.</summary>
        static object Parameters(PillScriptComponent component)
            => new
            {
                inputs = component.Params.Input
                    .Select(p => new { name = p.Name, type = p.TypeName, access = p.Access.ToString() }),
                outputs = component.Params.Output
                    .Select(p => new { name = p.Name, type = p.TypeName, access = p.Access.ToString() })
            };

        public static object Diagnostics(IEnumerable<CompileDiagnostic> diagnostics)
            => new { type = "diagnostics", items = diagnostics.Select(Diagnostic) };

        public static object Diagnostic(CompileDiagnostic diagnostic)
            => new
            {
                file = diagnostic.File,
                line = diagnostic.Line,
                column = diagnostic.Column,
                endLine = diagnostic.EndLine,
                endColumn = diagnostic.EndColumn,
                severity = diagnostic.Severity,
                id = diagnostic.Id,
                message = diagnostic.Message
            };

        public static object Paused(DebugStop stop)
            => new
            {
                type = "paused",
                file = stop.File,
                line = stop.Line,
                variables = stop.Variables.Select(v => new { name = v.Name, type = v.Type, value = v.Value })
            };

        public static object Resumed() => new { type = "resumed" };

        public static object Log(string text, string kind) => new { type = "log", text, kind };

        public static object Status(string text, string kind) => new { type = "status", text, kind };

        public static object References(
            IEnumerable<LocalReference> local,
            IEnumerable<PackageEntry> packages,
            IEnumerable<string> host)
            => new
            {
                local = local.Select(r => new { name = r.Name, path = r.Path, missing = r.Missing }),
                packages = packages.Select(r => new { id = r.Id, version = r.Version }),
                host
            };

        public static object Restore(ReferenceSet set)
            => new
            {
                ok = !set.Failed,
                names = set.Names,
                problems = set.Problems.Select(p => new { severity = p.Severity, message = p.Message })
            };

        /// <summary>Places in the script's own files, for going to one or listing them all.</summary>
        public static object Locations(IEnumerable<SourceSpan> spans)
            => spans.Select(span => new
            {
                file = span.File,
                line = span.Line,
                column = span.Column,
                endLine = span.EndLine,
                endColumn = span.EndColumn
            });

        /// <summary>The same, with what each place should be replaced by.</summary>
        public static object Edits(IEnumerable<SourceEdit> edits)
            => edits.Select(edit => new
            {
                file = edit.File,
                line = edit.Line,
                column = edit.Column,
                endLine = edit.EndLine,
                endColumn = edit.EndColumn,
                text = edit.Text
            });

        public static object Completions(IEnumerable<CompletionEntry> entries)
            => entries.Select(e => new
            {
                label = e.Label,
                kind = e.Kind,
                detail = e.Detail,
                insert = e.Insert,
                sort = e.Sort,
                filter = e.Filter,
                index = e.Index
            });

        public static object Signatures(IEnumerable<SignatureEntry> signatures, int active)
            => new
            {
                signatures = signatures.Select(s => new
                {
                    label = s.Label,
                    documentation = s.Documentation,
                    parameters = s.Parameters
                }),
                active
            };

        public static object Packages(IEnumerable<PackageHit> hits)
            => hits.Select(h => new
            {
                id = h.Id,
                version = h.Version,
                description = h.Description,
                downloads = h.Downloads
            });

        public static object Done(string error = null)
            => error == null ? new { ok = true, error = (string)null } : new { ok = false, error };
    }
}
