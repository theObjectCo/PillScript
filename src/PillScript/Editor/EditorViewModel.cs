using System;
using System.Diagnostics;
using System.Threading.Tasks;
using PillScript.Components;
using PillScript.Scripting;

namespace PillScript.Editor
{
    /// <summary>What the view model uses to reach the page. The bridge provides it.</summary>
    internal interface IEditorChannel
    {
        void Post(object payload);
        void Reply(string id, object payload);

        /// <summary>Re-sends the file list, and the state with it.</summary>
        void RefreshProject();

        /// <summary>Re-sends the state: staleness, parameters, where the window sits.</summary>
        void RefreshState();
    }

    /// <summary>
    /// Everything the editor can do to a component, with no window, no WebView2 and no Win32 in
    /// sight. The view calls in here; this calls into the scripting model and tells the page when
    /// something changed.
    /// </summary>
    internal sealed class EditorViewModel
    {
        readonly PillScriptComponent _component;
        readonly IEditorChannel _channel;

        public EditorViewModel(PillScriptComponent component, IEditorChannel channel)
        {
            _component = component;
            _channel = channel;
        }

        public PillScriptComponent Component => _component;

        ScriptProject Project => _component.Project;
        ScriptLanguageService Language => _component.Language;

        // ----- files ---------------------------------------------------------------------------

        public void Save(string name, string content)
        {
            Project.SetContent(name, content);
            Language.Update(name, content);

            _component.OnDisplayExpired(true);
            _channel.RefreshState();
        }

        public void AddFile(string name)
            => Mutate(Project.AddFile(name, out var error) ? null : error);

        public void RenameFile(string from, string to)
            => Mutate(Project.RenameFile(from, to, out var error) ? null : error);

        public void DeleteFile(string name)
            => Mutate(Project.RemoveFile(name, out var error) ? null : error);

        /// <summary>Applies a file operation and tells the page whether it took.</summary>
        void Mutate(string error)
        {
            if (error != null)
            {
                _channel.Post(EditorPayloads.Status(error, "error"));
                return;
            }

            Language.Invalidate(Project);
            _component.OnDisplayExpired(true);

            _channel.RefreshProject();
        }

        public void OpenFolder()
        {
            Project.MirrorToDisk();
            Start(Project.WorkingFolder);
        }

        /// <summary>Shows a file in Explorer, which is the quickest way to check what is referenced.</summary>
        public void Reveal(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

            var quote = '"';
            Start("explorer.exe", "/select," + quote + path + quote);
        }

        static void Start(string target, string arguments = null)
        {
            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            if (arguments != null) info.Arguments = arguments;

            Process.Start(info);
        }

        // ----- building and running --------------------------------------------------------------

        public void Compile()
        {
            _channel.RefreshState();

            _component.Compile(result =>
            {
                _channel.Post(EditorPayloads.Diagnostics(result.Diagnostics));
                _channel.RefreshState();
            });
        }

        public void Run() => _component.Run();

        public void SetBreakpoints(string file, int[] lines)
        {
            _component.SetBreakpoints(file, lines);
            _channel.RefreshState();
        }

        public void SetBreakpointsEnabled(bool enabled)
        {
            _component.SetBreakpointsEnabled(enabled);
            _channel.RefreshState();
        }

        // ----- references --------------------------------------------------------------------

        public object ReadReferences()
        {
            var (local, packages) = ProjectReferences.Read(Project);
            return EditorPayloads.References(local, packages, _component.References);
        }

        public object AddLocalReference(string path)
            => Change(() => ProjectReferences.AddLocal(Project, path));

        public object RemoveLocalReference(string name)
            => Change(() => ProjectReferences.RemoveLocal(Project, name));

        public object AddPackage(string id, string version)
            => Change(() => ProjectReferences.AddPackage(Project, id, version));

        public object RemovePackage(string id)
            => Change(() => ProjectReferences.RemovePackage(Project, id));

        /// <summary>
        /// Runs a change to the references and tells the component about it, so the editor, the
        /// mirror on disk and the next compile all see the same project file.
        /// </summary>
        object Change(Func<string> change)
        {
            var error = change();
            if (error != null) return EditorPayloads.Done(error);

            _component.AfterProjectChanged();
            _channel.RefreshState();

            return EditorPayloads.Done();
        }

        /// <summary>
        /// Resolves the references now rather than at the next compile, so the window can say
        /// whether a package actually came down. The work happens off the UI thread: restoring
        /// can take a while and the editor should stay usable while it does.
        /// </summary>
        public async Task<object> RestoreAsync()
        {
            var snapshot = Project.Clone();
            snapshot.MirrorToDisk();

            var set = await Task.Run(() => ReferenceSet.Build(snapshot));
            return EditorPayloads.Restore(set);
        }

        public Task<object> SearchPackagesAsync(string term)
            => NuGetCatalogue.SearchAsync(term).ContinueWith(t => EditorPayloads.Packages(t.Result));

        public Task<System.Collections.Generic.List<string>> PackageVersionsAsync(string id)
            => NuGetCatalogue.VersionsAsync(id);

        // ----- what the language service knows ------------------------------------------------

        public async Task<object> CompleteAsync(string file, string text, int offset, string trigger)
            => EditorPayloads.Completions(await Language.CompleteAsync(file, text, offset, trigger));

        public Task<string> DescribeAsync(string file, string text, int offset, string trigger, int index)
            => Language.DescribeCompletionAsync(file, text, offset, trigger, index);

        public Task<string> HoverAsync(string file, string text, int offset)
            => Language.HoverAsync(file, text, offset);

        public async Task<object> SignatureAsync(string file, string text, int offset)
        {
            var help = await Language.SignatureAsync(file, text, offset);
            return EditorPayloads.Signatures(help.Signatures, help.Active);
        }

        public async Task<object> DiagnoseAsync(string file, string text)
        {
            var found = await Language.DiagnoseAsync(file, text);
            return found.ConvertAll(d => EditorPayloads.Diagnostic(d));
        }

        public Task<string> FormatAsync(string file, string text) => Language.FormatAsync(file, text);

        public async Task<object> DefineAsync(string file, string text, int offset)
            => EditorPayloads.Locations(await Language.DefineAsync(file, text, offset));

        public async Task<object> ReferencesAsync(string file, string text, int offset)
            => EditorPayloads.Locations(await Language.ReferencesAsync(file, text, offset));

        public async Task<object> RenameAsync(string file, string text, int offset, string name)
            => EditorPayloads.Edits(await Language.RenameAsync(file, text, offset, name));
    }
}
