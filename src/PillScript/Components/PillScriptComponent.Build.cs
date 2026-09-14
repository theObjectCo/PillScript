using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhino;
using PillScript.Scripting;

namespace PillScript.Components
{
    /// <summary>
    /// The part of the component that compiles sources into a running build: how staleness is
    /// detected, what compiling does to the canvas, and where the script pauses.
    /// </summary>
    public partial class PillScriptComponent
    {
        /// <summary>True when the sources have changed since the loaded build was compiled.</summary>
        internal bool IsStale => _compiled == null || _compiled.SourceHash != Project.Hash;

        internal bool IsCompiling => _compiling;

        /// <summary>Lines the editor has marked, by file. Compiled into the script as pauses.</summary>
        internal IReadOnlyDictionary<string, IReadOnlyList<int>> Breakpoints => _breakpoints;

        /// <summary>
        /// Whether the marks are compiled in. Turning them off keeps them visible in the editor
        /// and builds a script that runs through without pausing.
        /// </summary>
        internal bool BreakpointsEnabled { get; private set; } = true;

        /// <summary>Assemblies the last build was compiled against.</summary>
        internal IReadOnlyList<string> References { get; private set; } = new List<string>();

        /// <summary>How the last build went, for the editor's status bar.</summary>
        internal string LastBuild { get; private set; } = "Not compiled";

        /// <summary>Raised with progress messages for the editor's output pane.</summary>
        internal event Action<string, string> Logged;

        /// <summary>
        /// Compiles off the UI thread, because restoring packages can take a while, then applies
        /// the result back on it.
        /// </summary>
        internal void Compile(Action<CompileResult> then = null)
        {
            if (_compiling) return;

            _compiling = true;
            OnDisplayExpired(true);

            var snapshot = Project.Clone();
            var breakpoints = BreakpointsEnabled
                ? new Dictionary<string, IReadOnlyList<int>>(_breakpoints, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, IReadOnlyList<int>>();

            Task.Run(() => ScriptCompiler.Compile(snapshot, breakpoints))
                .ContinueWith(task => RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    _compiling = false;

                    var result = task.IsFaulted
                        ? Failed(ScriptFault.Unwrap(task.Exception))
                        : task.Result;

                    Apply(result);
                    then?.Invoke(result);
                })));
        }

        /// <summary>An exception from the compiler is reported like code that did not build.</summary>
        static CompileResult Failed(Exception exception)
        {
            var result = new CompileResult();

            result.Diagnostics.Add(new CompileDiagnostic
            {
                File = ScriptProject.DefaultEntry,
                Line = 1,
                Column = 1,
                Severity = "error",
                Id = "SS0002",
                Message = exception.Message
            });

            return result;
        }

        void Apply(CompileResult result)
        {
            _diagnostics = result.Diagnostics;

            if (result.References.Count > 0) References = result.References;

            var errors = result.Diagnostics.Count(d => d.Severity == "error");

            if (!result.Success)
            {
                LastBuild = "Build failed";
                Logged?.Invoke("Build failed with " + errors + (errors == 1 ? " error" : " errors"), "bad");

                ExpireSolution(true);
                return;
            }

            var milliseconds = (int)result.Elapsed.TotalMilliseconds;

            LastBuild = "Build " + milliseconds + " ms";
            Logged?.Invoke("Build succeeded in " + milliseconds + " ms", "ok");

            _compiled?.Dispose();
            _compiled = result.Script;

            // A compile may have restored packages, so the language service is given the new
            // reference set as well.
            Language.Invalidate(Project);
            _watcher.Start();

            ParameterLayout.Apply(this, _compiled.Signature, FixedOutputs);
            ClearRuntimeMessages();

            // The controls come out of the build, so a new build can change the panel.
            if (IsPublished) AnnouncePublished();

            ExpireSolution(true);
        }

        /// <summary>Recomputes the component without rebuilding it.</summary>
        internal void Run() => ExpireSolution(true);

        // ----- breakpoints ---------------------------------------------------------------------

        /// <summary>Replaces the marks for one file, and makes the build that carries them stale.</summary>
        internal void SetBreakpoints(string file, IReadOnlyList<int> lines)
        {
            if (lines == null || lines.Count == 0) _breakpoints.Remove(file);
            else _breakpoints[file] = lines;

            Discard();
        }

        /// <summary>Turns every mark on or off at once, keeping their positions.</summary>
        internal void SetBreakpointsEnabled(bool enabled)
        {
            if (BreakpointsEnabled == enabled) return;

            BreakpointsEnabled = enabled;
            Discard();
        }

        /// <summary>Discards the build, since the pauses are compiled into it.</summary>
        void Discard()
        {
            _compiled?.Dispose();
            _compiled = null;

            OnDisplayExpired(true);
        }

        // ----- changes from elsewhere ------------------------------------------------------------

        /// <summary>
        /// Called after something outside the editor has changed the project, so the component and
        /// any open editor catch up with it.
        /// </summary>
        internal void AfterProjectChanged()
        {
            Language.Invalidate(Project);
            _watcher.Mirror();

            OnDisplayExpired(true);
            _editor?.Refresh();
        }

        /// <summary>The project read new sources off disk, put there by an IDE or another tool.</summary>
        void OnProjectPulled()
        {
            Language.Invalidate(Project);

            OnDisplayExpired(true);
            _editor?.Refresh();
        }
    }
}
