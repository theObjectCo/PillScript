using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using SharpScript.Components;
using SharpScript.Scripting;

namespace SharpScript.Editor
{
    /// <summary>
    /// The part of the editor that only a window can do: move itself, dock, open a file dialog,
    /// drive the canvas. The bridge routes the messages that need it here.
    /// </summary>
    internal interface IEditorShell
    {
        DockStatus Dock { get; }

        void ApplyWindowCommand(string action);
        void ApplyDock(bool onLeft);
        void BeginSplit();
        void Split(int offset);
        void Locate();

        /// <summary>Asks for a DLL to reference. Empty when the dialog was closed without one.</summary>
        string PickAssembly();
    }

    /// <summary>
    /// Carries messages between the page and the rest of the editor, and decides which half of
    /// the editor each one belongs to: the view model for anything about the script, the shell
    /// for anything about the window.
    /// </summary>
    internal sealed class EditorBridge : IEditorChannel
    {
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        readonly SharpScriptComponent _component;
        readonly IEditorShell _shell;
        readonly EditorViewModel _model;

        CoreWebView2 _page;
        bool _ready;

        public EditorBridge(SharpScriptComponent component, IEditorShell shell)
        {
            _component = component;
            _shell = shell;
            _model = new EditorViewModel(component, this);
        }

        public void Attach(CoreWebView2 page)
        {
            _page = page;
            _page.WebMessageReceived += OnMessage;
        }

        /// <summary>Re-sends everything, for when the project changed behind the page's back.</summary>
        public void Refresh()
        {
            RefreshProject();
        }

        // ----- incoming ------------------------------------------------------------------------

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            EditorRequest request;

            try
            {
                request = EditorRequest.Parse(e.WebMessageAsJson);
            }
            catch (Exception)
            {
                return;
            }

            if (request?.Type == null) return;

            Handle(request);
        }

        async void Handle(EditorRequest request)
        {
            try
            {
                await Route(request);
            }
            catch (Exception exception)
            {
                // This runs on the UI thread with nothing above it to catch anything. An editor
                // that throws here would end the Rhino session, so every failure stops at this
                // line and is reported into the editor's own output pane instead.
                Post(EditorPayloads.Log("Editor: " + exception.Message, "bad"));
            }
        }

        async Task Route(EditorRequest request)
        {
            switch (request.Type)
            {
                case "ready":
                    _ready = true;
                    RefreshProject();
                    break;

                case "pageError":
                    Post(EditorPayloads.Log("Editor: " + request.Text, "bad"));
                    break;

                // ----- the window itself -----

                case "window": _shell.ApplyWindowCommand(request.Action); break;
                case "locate": _shell.Locate(); break;
                case "dockSplitStart": _shell.BeginSplit(); break;
                case "dockSplit": _shell.Split(request.Offset); break;

                case "dock":
                    _shell.ApplyDock(request.Action == "left");
                    RefreshState();
                    break;

                case "addLocalReference":
                {
                    var chosen = _shell.PickAssembly();

                    // Closing the file dialog without choosing is not a failure.
                    Reply(request.Id, string.IsNullOrEmpty(chosen)
                        ? EditorPayloads.Done()
                        : _model.AddLocalReference(chosen));

                    break;
                }

                // ----- the script -----

                case "save": _model.Save(request.Name, request.Content); break;
                case "compile": _model.Compile(); break;
                case "run": _model.Run(); break;
                case "addFile": _model.AddFile(request.Name); break;
                case "renameFile": _model.RenameFile(request.From, request.To); break;
                case "deleteFile": _model.DeleteFile(request.Name); break;
                case "openFolder": _model.OpenFolder(); break;
                case "reveal": _model.Reveal(request.Name); break;

                case "breakpoints": _model.SetBreakpoints(request.File, request.Lines); break;
                case "breakpointsEnabled": _model.SetBreakpointsEnabled(request.Enabled); break;

                case "debugContinue": DebugSession.Continue(); break;
                case "debugStop": DebugSession.Stop(); break;

                // ----- references -----

                case "references": Reply(request.Id, _model.ReadReferences()); break;
                case "removeLocalReference": Reply(request.Id, _model.RemoveLocalReference(request.Name)); break;
                case "addPackage": Reply(request.Id, _model.AddPackage(request.Name, request.Content)); break;
                case "removePackage": Reply(request.Id, _model.RemovePackage(request.Name)); break;
                case "restore": Reply(request.Id, await _model.RestoreAsync()); break;
                case "searchPackages": Reply(request.Id, await _model.SearchPackagesAsync(request.Name)); break;
                case "packageVersions": Reply(request.Id, await _model.PackageVersionsAsync(request.Name)); break;

                // ----- what the language service knows -----

                case "complete":
                    Reply(request.Id, await _model.CompleteAsync(
                        request.File, request.Text, request.Offset, request.Trigger));
                    break;

                case "describe":
                    Reply(request.Id, await _model.DescribeAsync(
                        request.File, request.Text, request.Offset, request.Trigger, request.Index));
                    break;

                case "hover":
                    Reply(request.Id, await _model.HoverAsync(request.File, request.Text, request.Offset));
                    break;

                case "signature":
                    Reply(request.Id, await _model.SignatureAsync(request.File, request.Text, request.Offset));
                    break;

                case "diagnose":
                    Reply(request.Id, await _model.DiagnoseAsync(request.File, request.Text));
                    break;

                case "format":
                    Reply(request.Id, await _model.FormatAsync(request.File, request.Text));
                    break;
            }
        }

        // ----- outgoing ------------------------------------------------------------------------

        public void RefreshProject()
        {
            Post(EditorPayloads.Project(_component));
            RefreshState();
        }

        public void RefreshState() => Post(EditorPayloads.State(_component, _shell.Dock));

        public void Reply(string id, object payload)
        {
            if (string.IsNullOrEmpty(id)) return;

            Post(new { type = "reply", id, payload });
        }

        public void Post(object payload)
        {
            if (!_ready || _page == null) return;

            try
            {
                _page.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json));
            }
            catch (Exception)
            {
                // The page has gone or is being torn down. Nothing above this can handle it, and
                // an unhandled exception on the UI thread would end the Rhino session.
            }
        }
    }
}
