using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino;
using SharpScript.Editor;
using SharpScript.Scripting;

namespace SharpScript.Components
{
    /// <summary>
    /// A C# script component whose inputs and outputs come from the RunScript signature and whose
    /// code is only compiled when asked. Between an edit and the next compile the component keeps
    /// running the previous build and shows itself as stale.
    /// </summary>
    public class SharpScriptComponent : GH_Component, IGH_VariableParameterComponent
    {
        internal ScriptProject Project { get; private set; } = new ScriptProject();

        /// <summary>Roslyn over the same sources, answering the editor between compiles.</summary>
        internal ScriptLanguageService Language { get; } = new ScriptLanguageService();

        readonly Dictionary<string, IReadOnlyList<int>> _breakpoints =
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        CompiledScript _compiled;
        List<CompileDiagnostic> _diagnostics = new List<CompileDiagnostic>();
        bool _compiling;
        ScriptEditorWindow _editor;
        FileSystemWatcher _watcher;
        CancellationTokenSource _pull;
        DateTime _mirrored = DateTime.MinValue;

        public SharpScriptComponent()
            : base("C# Script", "C#", "Runs a C# script whose parameters are declared in the code.",
                   "Maths", "Script")
        {
            Language.Invalidate(Project);
        }

        public override Guid ComponentGuid => new Guid("7F4C0F1B-9E51-4A3E-9B1D-6C1A2E3D4F50");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => ComponentIcon.Bitmap;

        /// <summary>True when the sources have moved on from the build that is loaded.</summary>
        internal bool IsStale => _compiled == null || _compiled.SourceHash != Project.Hash;

        internal bool IsCompiling => _compiling;

        /// <summary>Lines the editor has marked, by file. Compiled into the script as pauses.</summary>
        internal IReadOnlyDictionary<string, IReadOnlyList<int>> Breakpoints => _breakpoints;

        /// <summary>
        /// Whether the marks are compiled in. Turning them off leaves them on screen but builds a
        /// script that runs straight through, which is what one wants after finding the bug.
        /// </summary>
        internal bool BreakpointsEnabled { get; private set; } = true;

        /// <summary>True while this component's editor window is up, which the canvas draws.</summary>
        internal bool IsEditorOpen => _editor != null;

        /// <summary>Assemblies the last build was compiled against.</summary>
        internal IReadOnlyList<string> References { get; private set; } = new List<string>();

        /// <summary>How the last build went, for the editor's status bar.</summary>
        internal string LastBuild { get; private set; } = "Not compiled";

        /// <summary>Raised with progress worth showing in the editor's output pane.</summary>
        internal event Action<string, string> Logged;

        /// <summary>Recompile as soon as the component joins a document, so a reopened file runs.</summary>
        internal bool CompileOnLoad { get; set; } = true;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Inputs are whatever the compiled signature says they are.
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("out", "out",
                "Print output and runtime messages.", GH_ParamAccess.list);
        }

        /// <summary>The number of outputs that belong to the component rather than to the script.</summary>
        const int FixedOutputs = 1;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Compile errors are raised outside any solution, so they are re-stated here; a
            // component that only complained inside the editor would look merely stale.
            foreach (var error in _diagnostics.Where(d => d.Severity == "error").Take(5))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    error.File + " (" + error.Line + "): " + error.Id + ": " + error.Message);
            }

            if (_compiled == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "This script has not been compiled yet. Open the editor and press Compile.");
                return;
            }

            var lines = new List<string>();

            try
            {
                ScriptRunner.Run(_compiled, DA, this, DA.Iteration, FixedOutputs, lines.Add);
            }
            catch (Exception exception)
            {
                var actual = Unwrap(exception);
                lines.Add(Describe(actual));
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Describe(actual));
            }

            DA.SetDataList(0, lines);
            Report(lines);
        }

        /// <summary>
        /// Sends what the script printed to an open editor. Only worth doing while somebody is
        /// looking, and capped, because a solve over a long list would otherwise bury the pane.
        /// </summary>
        void Report(IReadOnlyList<string> lines)
        {
            if (Logged == null || lines.Count == 0) return;

            foreach (var line in lines.Take(ReportLimit)) Logged.Invoke(line, "loud");

            if (lines.Count > ReportLimit)
                Logged.Invoke("and " + (lines.Count - ReportLimit) + " more lines in out", null);
        }

        const int ReportLimit = 40;

        static Exception Unwrap(Exception exception)
            => exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;

        /// <summary>Names the exception and, when the script's own frames are in the trace, where it happened.</summary>
        static string Describe(Exception exception)
        {
            var message = exception.GetType().Name + ": " + exception.Message;

            var frame = new StackTrace(exception, true).GetFrames()?
                .FirstOrDefault(f => f.GetFileLineNumber() > 0);

            if (frame == null) return message;

            var file = System.IO.Path.GetFileName(frame.GetFileName());
            return message + " (" + file + ", line " + frame.GetFileLineNumber() + ")";
        }

        // ----- compiling -------------------------------------------------------------------

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
                        ? Failed(Unwrap(task.Exception))
                        : task.Result;

                    Apply(result);
                    then?.Invoke(result);
                })));
        }

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
            var milliseconds = (int)result.Elapsed.TotalMilliseconds;

            if (!result.Success)
            {
                LastBuild = "Build failed";
                Logged?.Invoke("Build failed with " + errors + (errors == 1 ? " error" : " errors"), "bad");

                ExpireSolution(true);
                return;
            }

            LastBuild = "Build " + milliseconds + " ms";
            Logged?.Invoke("Build succeeded in " + milliseconds + " ms", "ok");

            _compiled?.Dispose();
            _compiled = result.Script;

            // A compile can have restored packages, so the language service needs the new
            // reference set as much as the next build does.
            Language.Invalidate(Project);
            WatchProjectFolder();

            ApplySignature(_compiled.Signature);
            ClearRuntimeMessages();
            ExpireSolution(true);
        }

        /// <summary>
        /// Rebuilds the parameter list from the signature, reconnecting wires whose parameter
        /// kept its name and shape. Does nothing when the layout already matches.
        /// </summary>
        void ApplySignature(ScriptSignature signature)
        {
            if (LayoutMatches(signature)) return;

            RecordUndoEvent("Script parameters");

            var inputSources = Params.Input.ToDictionary(
                p => p.Name, p => p.Sources.ToList(), StringComparer.OrdinalIgnoreCase);

            var outputRecipients = Params.Output.Skip(FixedOutputs).ToDictionary(
                p => p.Name, p => p.Recipients.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var param in Params.Input.ToList())
                Params.UnregisterInputParameter(param);

            foreach (var param in Params.Output.Skip(FixedOutputs).ToList())
                Params.UnregisterOutputParameter(param);

            foreach (var declared in signature.Inputs)
            {
                var param = Build(declared);
                Params.RegisterInputParam(param);

                if (!inputSources.TryGetValue(declared.Name, out var sources)) continue;
                foreach (var source in sources) param.AddSource(source);
            }

            foreach (var declared in signature.Outputs)
            {
                var param = Build(declared);
                Params.RegisterOutputParam(param);

                if (!outputRecipients.TryGetValue(declared.Name, out var recipients)) continue;
                foreach (var recipient in recipients) recipient.AddSource(param);
            }

            Params.OnParametersChanged();
        }

        bool LayoutMatches(ScriptSignature signature)
        {
            if (Params.Input.Count != signature.Inputs.Count) return false;
            if (Params.Output.Count != signature.Outputs.Count + FixedOutputs) return false;

            for (var i = 0; i < signature.Inputs.Count; i++)
            {
                if (!Describes(Params.Input[i], signature.Inputs[i])) return false;
            }

            for (var i = 0; i < signature.Outputs.Count; i++)
            {
                if (!Describes(Params.Output[i + FixedOutputs], signature.Outputs[i])) return false;
            }

            return true;
        }

        static bool Describes(IGH_Param param, ScriptParam declared)
            => string.Equals(param.Name, declared.Name, StringComparison.Ordinal)
               && param.Access == declared.GhAccess
               && param.GetType() == ParamFactory.Create(declared.ElementType).GetType();

        static IGH_Param Build(ScriptParam declared)
        {
            var param = ParamFactory.Create(declared.ElementType);

            param.Name = declared.Name;
            param.NickName = declared.Nickname;
            param.Description = declared.Description;
            param.Access = declared.GhAccess;
            param.Optional = declared.Optional || declared.Default != null;

            if (!declared.IsOutput) ParamFactory.ApplyDefault(param, declared.Default);

            return param;
        }

        // ----- editor ----------------------------------------------------------------------

        /// <summary>
        /// Follows the mirrored folder, so editing the project in an IDE puts the component into
        /// the same stale state as editing it here. The watcher starts once the folder exists.
        /// </summary>
        void WatchProjectFolder()
        {
            if (_watcher != null || !Directory.Exists(Project.WorkingFolder)) return;

            _watcher = new FileSystemWatcher(Project.WorkingFolder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFolderChanged;
            _watcher.Created += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
        }

        void OnFolderChanged(object sender, FileSystemEventArgs e)
        {
            // An editor writing a file raises several events; only the last one is worth reading.
            _pull?.Cancel();
            _pull = new CancellationTokenSource();
            var token = _pull.Token;

            var seen = DateTime.UtcNow;

            Task.Delay(250, token).ContinueWith(_ =>
            {
                if (token.IsCancellationRequested) return;

                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    // Mirroring the project trips the watcher, and pulling the result back in
                    // would reload the files, rebuild the language service and redraw for no
                    // change at all. An edit made elsewhere arrives after the write and still
                    // gets through.
                    if (seen <= _mirrored) return;

                    if (!Project.PullFromDisk()) return;

                    Language.Invalidate(Project);
                    OnDisplayExpired(true);
                    _editor?.Refresh();
                }));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>Replaces the marks for one file, and makes the build that carries them stale.</summary>
        internal void SetBreakpoints(string file, IReadOnlyList<int> lines)
        {
            if (lines == null || lines.Count == 0) _breakpoints.Remove(file);
            else _breakpoints[file] = lines;

            Invalidate();
        }

        /// <summary>
        /// Called after something outside the editor has changed the project, so the component and
        /// any open editor catch up with it.
        /// </summary>
        internal void AfterProjectChanged()
        {
            Language.Invalidate(Project);
            Mirror();

            OnDisplayExpired(true);
            _editor?.Refresh();
        }

        /// <summary>
        /// Writes the project out and notes when, so the watcher can tell our own writes from
        /// somebody else's and skip reading back what we just wrote.
        /// </summary>
        void Mirror()
        {
            Project.MirrorToDisk();
            _mirrored = DateTime.UtcNow;
        }

        /// <summary>Throws the build away, because the pauses are compiled into it.</summary>
        void Invalidate()
        {
            _compiled?.Dispose();
            _compiled = null;

            OnDisplayExpired(true);
        }

        /// <summary>Turns every mark on or off at once, without losing where they are.</summary>
        internal void SetBreakpointsEnabled(bool enabled)
        {
            if (BreakpointsEnabled == enabled) return;

            BreakpointsEnabled = enabled;
            Invalidate();
        }

        /// <summary>Recomputes the component without rebuilding it.</summary>
        internal void Run()
        {
            ExpireSolution(true);
        }

        internal void OpenEditor()
        {
            if (_editor != null && _editor.IsLoaded)
            {
                _editor.Activate();
                return;
            }

            Mirror();
            WatchProjectFolder();

            _editor = new ScriptEditorWindow(this);
            _editor.Closed += (_, __) =>
            {
                _editor = null;
                OnDisplayExpired(true);
            };

            _editor.Show();
            OnDisplayExpired(true);
        }

        public override void CreateAttributes()
            => m_attributes = new SharpScriptAttributes(this);

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);

            if (CompileOnLoad && _compiled == null) Compile();
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _watcher?.Dispose();
            _watcher = null;
            _pull?.Cancel();
            Language.Dispose();

            _editor?.Close();
            _compiled?.Dispose();
            _compiled = null;

            base.RemovedFromDocument(document);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            var edit = Menu_AppendItem(menu, "Edit script...", (_, __) => OpenEditor());
            edit.Font = new System.Drawing.Font(edit.Font, System.Drawing.FontStyle.Bold);

            var compile = Menu_AppendItem(menu, "Compile", (_, __) => Compile(), !_compiling);
            compile.ToolTipText = IsStale
                ? "The sources have changed since the last build."
                : "The loaded build is up to date.";

            Menu_AppendSeparator(menu);

            Menu_AppendItem(menu, "Compile when the document opens", (_, __) =>
            {
                RecordUndoEvent("Compile on load");
                CompileOnLoad = !CompileOnLoad;
            }, true, CompileOnLoad);

            Menu_AppendItem(menu, "Open project folder", (_, __) =>
            {
                Mirror();
                Process.Start(new ProcessStartInfo(Project.WorkingFolder) { UseShellExecute = true });
            });
        }

        // ----- persistence -----------------------------------------------------------------

        public override bool Write(GH_IWriter writer)
        {
            writer.SetGuid("ProjectId", Project.Id);
            writer.SetBoolean("CompileOnLoad", CompileOnLoad);
            writer.SetInt32("FileCount", Project.Files.Count);

            for (var i = 0; i < Project.Files.Count; i++)
            {
                writer.SetString("FileName", i, Project.Files[i].Name);
                writer.SetString("FileContent", i, Project.Files[i].Content);
            }

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            var id = reader.ItemExists("ProjectId") ? reader.GetGuid("ProjectId") : Guid.NewGuid();

            if (reader.ItemExists("CompileOnLoad"))
                CompileOnLoad = reader.GetBoolean("CompileOnLoad");

            var files = new List<ScriptFile>();
            var count = reader.ItemExists("FileCount") ? reader.GetInt32("FileCount") : 0;

            for (var i = 0; i < count; i++)
            {
                files.Add(new ScriptFile(
                    reader.GetString("FileName", i),
                    reader.GetString("FileContent", i)));
            }

            Project.Load(id, files);
            Language.Invalidate(Project);

            return base.Read(reader);
        }

        // ----- IGH_VariableParameterComponent ----------------------------------------------
        // The signature owns the parameter list, so the zoomable interface offers no plus and
        // minus and nothing is maintained by hand.

        public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

        public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

        public IGH_Param CreateParameter(GH_ParameterSide side, int index) => null;

        public bool DestroyParameter(GH_ParameterSide side, int index) => false;

        public void VariableParameterMaintenance() { }
    }
}
