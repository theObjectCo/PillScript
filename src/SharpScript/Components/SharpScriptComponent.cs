using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using SharpScript.Editor;
using SharpScript.Scripting;

namespace SharpScript.Components
{
    /// <summary>
    /// A C# script component whose inputs and outputs come from the RunScript signature and whose
    /// code is only compiled when asked. Between an edit and the next compile the component keeps
    /// running the previous build and shows itself as stale.
    ///
    /// This file is the Grasshopper surface: parameters, solving, the menu and what is saved in
    /// the document. Building the script is next door in SharpScriptComponent.Build.cs.
    /// </summary>
    public partial class SharpScriptComponent : GH_Component, IGH_VariableParameterComponent
    {
        /// <summary>The number of outputs that belong to the component rather than to the script.</summary>
        const int FixedOutputs = 1;

        /// <summary>How many printed lines are pushed to the editor before the rest are counted.</summary>
        const int ReportLimit = 40;

        internal ScriptProject Project { get; } = new ScriptProject();

        /// <summary>Roslyn over the same sources, answering the editor between compiles.</summary>
        internal ScriptLanguageService Language { get; } = new ScriptLanguageService();

        readonly Dictionary<string, IReadOnlyList<int>> _breakpoints =
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        readonly ProjectWatcher _watcher;

        CompiledScript _compiled;
        List<CompileDiagnostic> _diagnostics = new List<CompileDiagnostic>();
        bool _compiling;
        ScriptEditorWindow _editor;

        public SharpScriptComponent()
            : base("C# Script", "C#", "Runs a C# script whose parameters are declared in the code.",
                   "Maths", "Script")
        {
            _watcher = new ProjectWatcher(Project, OnProjectPulled);

            Language.Invalidate(Project);
        }

        public override Guid ComponentGuid => new Guid("7F4C0F1B-9E51-4A3E-9B1D-6C1A2E3D4F50");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => ComponentIcon.Bitmap;

        /// <summary>True while this component's editor window is up, which the canvas draws.</summary>
        internal bool IsEditorOpen => _editor != null;

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

        // ----- solving -----------------------------------------------------------------------

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
                var said = ScriptFault.Describe(ScriptFault.Unwrap(exception));

                lines.Add(said);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, said);
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

        // ----- the editor window --------------------------------------------------------------

        internal void OpenEditor()
        {
            if (_editor != null && _editor.IsLoaded)
            {
                _editor.Activate();
                return;
            }

            _watcher.Mirror();
            _watcher.Start();

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
            _watcher.Dispose();
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
                _watcher.Mirror();
                Process.Start(new ProcessStartInfo(Project.WorkingFolder) { UseShellExecute = true });
            });
        }

        // ----- persistence ---------------------------------------------------------------------

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
