using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using SharpScript.Components;
using SharpScript.Scripting;

namespace SharpScript.Editor
{
    /// <summary>
    /// One message from the page. Commands carry a name or some text; language queries carry a
    /// caret and an id the answer is sent back under.
    /// </summary>
    internal sealed class EditorRequest
    {
        public string Type;
        public string Id;
        public string Name;
        public string Content;
        public string From;
        public string To;
        public string File;
        public string Text;
        public string Trigger;
        public string Action;
        public bool Enabled;
        public int Offset;
        public int Index;
        public int Cols;
        public int Rows;
        public int[] Lines = new int[0];

        public static EditorRequest Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type)) return null;

            return new EditorRequest
            {
                Type = type.GetString(),
                Id = Text_(root, "id"),
                Name = Text_(root, "name"),
                Content = Text_(root, "content"),
                From = Text_(root, "from"),
                To = Text_(root, "to"),
                File = Text_(root, "file"),
                Text = Text_(root, "text"),
                Trigger = Text_(root, "trigger"),
                Action = Text_(root, "action"),
                Enabled = Flag(root, "enabled"),
                Offset = Number(root, "offset"),
                Index = Number(root, "index"),
                Cols = Number(root, "cols"),
                Rows = Number(root, "rows"),
                Lines = Numbers(root, "lines")
            };
        }

        static string Text_(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : string.Empty;

        static bool Flag(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        static int[] Numbers(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return new int[0];

            return value.EnumerateArray()
                .Where(item => item.TryGetInt32(out _))
                .Select(item => item.GetInt32())
                .ToArray();
        }

        static int Number(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
                ? number
                : 0;
    }

    /// <summary>
    /// Hosts the Monaco editor in a WebView2 and keeps it in step with one component's project.
    /// All UI lives in the web page; this class moves files, commands, language answers and
    /// diagnostics across.
    /// </summary>
    internal sealed class ScriptEditorWindow : Window
    {
        const string VirtualHost = "sharpscript.local";

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // The page covers the window edge to edge. Resizing is still possible because the page
        // reports a press near an edge and the window hands that to the system resize loop.
        const int ResizeBand = 0;

        readonly SharpScriptComponent _component;
        readonly WebView2 _web = new WebView2();
        Border _frame;
        ShellSession _terminal;
        DockHost _dock;
        DispatcherTimer _glide;
        int _dragWidth;
        bool _ready;

        public ScriptEditorWindow(SharpScriptComponent component)
        {
            _component = component;

            Title = "C# Script - " + component.NickName;
            Width = 1180;
            Height = 780;
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Owned by Rhino rather than topmost: an owned window sits above its owner and only
            // its owner, so it stays over the canvas, hides when Rhino is minimised, and does not
            // float over whatever else is on the screen.
            var owner = Rhino.RhinoApp.MainWindowHandle();
            if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;

            // The page is inset so a band of the window is left uncovered. WebView2 is a child
            // window and swallows the mouse, so without the band the resize frame never sees it.
            _frame = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(ResizeBand),
                Child = _web
            };

            Content = _frame;

            // The page draws its own title bar, so the system one is taken away and only the
            // resize border is kept.
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;

            WindowChrome.SetWindowChrome(this, Chrome());

            StateChanged += (_, __) => _frame.Padding =
                new Thickness(WindowState == WindowState.Maximized ? 0 : ResizeBand);

            _dock = new DockHost(this);

            component.Logged += OnComponentLog;
            DebugSession.Paused += OnPaused;
            DebugSession.Resumed += OnResumed;

            Closing += (_, __) =>
            {
                // Detaching here rather than on Closed, because by then the window handle is gone
                // and Grasshopper would be left with a child that never tidied itself away.
                try { _dock.Release(); }
                catch (Exception) { }
            };

            Closed += (_, __) => Detach();

            Loaded += (_, __) => Start();
        }

        void Detach()
        {

            _component.Logged -= OnComponentLog;
            DebugSession.Paused -= OnPaused;
            DebugSession.Resumed -= OnResumed;

            _glide?.Stop();
            _glide = null;

            _terminal?.Dispose();
            _terminal = null;
        }

        void OnComponentLog(string text, string kind) => Post(new { type = "log", text, kind });

        void OnPaused(DebugStop stop)
            => Post(new
            {
                type = "paused",
                file = stop.File,
                line = stop.Line,
                variables = stop.Variables.Select(v => new { name = v.Name, type = v.Type, value = v.Value })
            });

        void OnResumed() => Post(new { type = "resumed" });

        async void Start()
        {
            var webFolder = Path.Combine(
                Path.GetDirectoryName(typeof(ScriptEditorWindow).Assembly.Location) ?? string.Empty, "web");

            if (!Directory.Exists(webFolder))
            {
                MessageBox.Show(this,
                    "The editor resources are missing from " + webFolder + ".",
                    "C# Script", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SharpScript", "webview");

            Directory.CreateDirectory(userData);

            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this,
                    "The editor needs the WebView2 runtime, which could not be started: " + exception.Message,
                    "C# Script", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            try
            {
                var core = _web.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping(
                    VirtualHost, webFolder, CoreWebView2HostResourceAccessKind.Allow);

                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = true;

                core.WebMessageReceived += OnWebMessage;
                core.Navigate("https://" + VirtualHost + "/index.html");
            }
            catch (Exception exception)
            {
                MessageBox.Show(this,
                    "The editor could not be set up: " + exception.Message,
                    "C# Script", MessageBoxButton.OK, MessageBoxImage.Error);

                Close();
            }
        }

        // ----- incoming ---------------------------------------------------------------------

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                await Dispatch(request);
            }
            catch (Exception exception)
            {
                // This runs on the UI thread with nothing above it to catch anything. An editor
                // that throws here would end the Rhino session, so every failure stops at this
                // line and is reported into the editor's own output pane instead.
                Post(new { type = "log", text = "Editor: " + exception.Message, kind = "bad" });
            }
        }

        async System.Threading.Tasks.Task Dispatch(EditorRequest request)
        {
            var language = _component.Language;
            var project = _component.Project;

            switch (request.Type)
            {
                case "ready":
                    _ready = true;
                    SendProject();
                    SendState();
                    break;

                case "save":
                    project.SetContent(request.Name, request.Content);
                    language.Update(request.Name, request.Content);
                    _component.OnDisplayExpired(true);
                    SendState();
                    break;

                case "compile":
                    Compile();
                    break;

                case "addFile":
                    Mutate(name => project.AddFile(name, out var addError) ? null : addError, request.Name);
                    break;

                case "renameFile":
                    Mutate(from => project.RenameFile(from, request.To, out var renameError) ? null : renameError,
                        request.From);
                    break;

                case "deleteFile":
                    Mutate(name => project.RemoveFile(name, out var removeError) ? null : removeError,
                        request.Name);
                    break;

                case "openFolder":
                    project.MirrorToDisk();
                    Process.Start(new ProcessStartInfo(project.WorkingFolder) { UseShellExecute = true });
                    break;

                case "pageError":
                    Post(new { type = "log", text = "Editor: " + request.Text, kind = "bad" });
                    break;

                case "run":
                    _component.Run();
                    break;

                case "window":
                    ApplyWindowCommand(request.Action);
                    break;

                case "dock":
                    ApplyDock(request.Action == "left");
                    SendState();
                    break;

                case "locate":
                    Locate();
                    break;

                case "dockSplitStart":
                    _dragWidth = _dock.CurrentWidth;
                    break;

                case "dockSplit":
                    if (_dock.IsDocked && _dock.HostWidth > 0)
                        _dock.SetFraction((_dragWidth + request.Offset) / (double)_dock.HostWidth);

                    break;

                case "breakpoints":
                    _component.SetBreakpoints(request.File, request.Lines);
                    SendState();
                    break;

                case "breakpointsEnabled":
                    _component.SetBreakpointsEnabled(request.Enabled);
                    SendState();
                    break;

                case "references":
                {
                    var (local, packages) = ProjectReferences.Read(project);

                    Reply(request.Id, new
                    {
                        local = local.Select(r => new { name = r.Name, path = r.Path, missing = r.Missing }),
                        packages = packages.Select(r => new { id = r.Id, version = r.Version }),
                        host = _component.References
                    });

                    break;
                }

                case "addLocalReference":
                {
                    var chosen = PickAssembly();

                    // Closing the file dialog without choosing is not a failure.
                    Reply(request.Id, string.IsNullOrEmpty(chosen)
                        ? new { ok = true, error = (string)null }
                        : ChangeReferences(() => ProjectReferences.AddLocal(project, chosen)));

                    break;
                }

                case "removeLocalReference":
                    Reply(request.Id, ChangeReferences(() => ProjectReferences.RemoveLocal(project, request.Name)));
                    break;

                case "addPackage":
                    Reply(request.Id, ChangeReferences(
                        () => ProjectReferences.AddPackage(project, request.Name, request.Content)));
                    break;

                case "searchPackages":
                {
                    var hits = await NuGetCatalogue.SearchAsync(request.Name);

                    Reply(request.Id, hits.Select(h => new
                    {
                        id = h.Id,
                        version = h.Version,
                        description = h.Description,
                        downloads = h.Downloads
                    }));

                    break;
                }

                case "packageVersions":
                    Reply(request.Id, await NuGetCatalogue.VersionsAsync(request.Name));
                    break;

                case "restore":
                    Reply(request.Id, await Restore());
                    break;

                case "reveal":
                    Reveal(request.Name);
                    break;

                case "removePackage":
                    Reply(request.Id, ChangeReferences(() => ProjectReferences.RemovePackage(project, request.Name)));
                    break;

                case "format":
                    Reply(request.Id, await language.FormatAsync(request.File, request.Text));
                    break;

                case "terminalStart":
                    StartTerminal(request.Cols, request.Rows);
                    break;

                case "terminalInput":
                    _terminal?.Write(request.Text);
                    break;

                case "terminalResize":
                    _terminal?.Resize(request.Cols, request.Rows);
                    break;

                case "debugContinue":
                    DebugSession.Continue();
                    break;

                case "debugStop":
                    DebugSession.Stop();
                    break;

                case "complete":
                    Reply(request.Id, await language.CompleteAsync(
                        request.File, request.Text, request.Offset, request.Trigger));
                    break;

                case "describe":
                    Reply(request.Id, await language.DescribeCompletionAsync(
                        request.File, request.Text, request.Offset, request.Trigger, request.Index));
                    break;

                case "hover":
                    Reply(request.Id, await language.HoverAsync(
                        request.File, request.Text, request.Offset));
                    break;

                case "signature":
                    var help = await language.SignatureAsync(request.File, request.Text, request.Offset);
                    Reply(request.Id, new { signatures = help.Signatures, active = help.Active });
                    break;

                case "diagnose":
                    Reply(request.Id, await language.DiagnoseAsync(request.File, request.Text));
                    break;
            }
        }

        /// <summary>
        /// Runs a change to the references and tells the component about it, so the editor, the
        /// mirror on disk and the next compile all see the same project file.
        /// </summary>
        object ChangeReferences(Func<string> change)
        {
            var error = change();
            if (error != null) return new { ok = false, error };

            _component.AfterProjectChanged();
            SendState();

            return new { ok = true };
        }

        /// <summary>
        /// Resolves the references now rather than at the next compile, so the window can say
        /// whether a package actually came down.
        /// </summary>
        async System.Threading.Tasks.Task<object> Restore()
        {
            var project = _component.Project.Clone();
            project.MirrorToDisk();

            var set = await System.Threading.Tasks.Task.Run(() => ReferenceSet.Build(project));

            return new
            {
                ok = !set.Failed,
                names = set.Names,
                problems = set.Problems.Select(p => new { severity = p.Severity, message = p.Message })
            };
        }

        /// <summary>Shows a file in Explorer, which is the quickest way to check what is referenced.</summary>
        static void Reveal(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
        }

        /// <summary>Asks for a DLL to reference. Returns an empty string when nothing was chosen.</summary>
        string PickAssembly()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Reference an assembly",
                Filter = "Assemblies (*.dll)|*.dll|All files (*.*)|*.*",
                CheckFileExists = true
            };

            return dialog.ShowDialog(this) == true ? dialog.FileName : string.Empty;
        }

        void ApplyWindowCommand(string action)
        {
            switch (action)
            {
                case "minimize":
                    WindowState = WindowState.Minimized;
                    break;

                case "maximize":
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    break;

                case "close":
                    Close();
                    break;

                case "drag":
                    SystemDrag(HitTestCaption);
                    break;

                case "resize-left": SystemDrag(10); break;
                case "resize-right": SystemDrag(11); break;
                case "resize-top": SystemDrag(12); break;
                case "resize-topleft": SystemDrag(13); break;
                case "resize-topright": SystemDrag(14); break;
                case "resize-bottom": SystemDrag(15); break;
                case "resize-bottomleft": SystemDrag(16); break;
                case "resize-bottomright": SystemDrag(17); break;
            }
        }

        /// <summary>
        /// Hands the window to the system's own move or resize loop, told which part of the frame
        /// was grabbed. DragMove cannot be used here: by the time the page's message arrives WPF
        /// no longer sees the press that started it, and it cannot resize at all.
        /// </summary>
        void SystemDrag(int part)
        {
            if (_dock.IsDocked) return;

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            ReleaseCapture();
            SendMessage(handle, WmNcLeftButtonDown, (IntPtr)part, IntPtr.Zero);
        }

        const int WmNcLeftButtonDown = 0x00A1;
        const int HitTestCaption = 2;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Docks and undocks in the order the window frame can survive. WindowChrome talks to the
        /// desktop compositor about the frame, and a child window has no frame to talk about, so
        /// the chrome comes off before the window becomes a child and goes back on only once it
        /// is a window in its own right again. Doing it the other way round throws inside WPF.
        /// </summary>
        void ApplyDock(bool onLeft)
        {
            if (_dock.IsDocked)
            {
                // Asking for the side it is already on means asking for it back as a window.
                if (_dock.OnLeft == onLeft) Float();
                else _dock.SetSide(onLeft);

                return;
            }

            WindowChrome.SetWindowChrome(this, null);
            _frame.Padding = new Thickness(0);

            _dock.Dock(onLeft);
        }

        /// <summary>
        /// Brings the canvas to the component this editor belongs to, at full zoom, and selects
        /// it. The viewport has its own way of being moved: setting its target leaves the
        /// projection untouched, so nothing appears to happen. Focus is the method meant for it.
        /// </summary>
        void Locate()
        {
            var document = _component.OnPingDocument();
            var canvas = Grasshopper.Instances.ActiveCanvas;

            if (document == null || canvas == null || _component.Attributes == null) return;

            document.DeselectAll();
            _component.Attributes.Selected = true;

            var viewport = canvas.Viewport;
            var bounds = _component.Attributes.Bounds;

            var to = new System.Drawing.PointF(
                bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

            // Centred on the whole canvas the component would sit behind a docked editor, so the
            // view is nudged by half the width the editor covers. That nudge is in screen pixels,
            // which is also the scale Focus wants its point in, so it survives the zoom changing.
            var offset = 0f;
            if (_dock.IsDocked) offset = _dock.OnLeft ? -_dock.CurrentWidth / 2f : _dock.CurrentWidth / 2f;

            var region = viewport.VisibleRegion;
            var from = new System.Drawing.PointF(
                region.X + region.Width / 2f, region.Y + region.Height / 2f);

            Glide(canvas, from, viewport.Zoom, to, 1f, offset);
        }

        /// <summary>
        /// Walks the viewport from where it is to where it should be over half a second. A jump
        /// leaves you wondering which way the canvas went; a short move shows you.
        /// </summary>
        void Glide(
            Grasshopper.GUI.Canvas.GH_Canvas canvas,
            System.Drawing.PointF from, float fromZoom,
            System.Drawing.PointF to, float toZoom, float offset)
        {
            _glide?.Stop();

            var started = DateTime.UtcNow;
            var span = TimeSpan.FromMilliseconds(500);

            _glide = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };

            _glide.Tick += (_, __) =>
            {
                var t = Math.Min(1.0, (DateTime.UtcNow - started).TotalMilliseconds / span.TotalMilliseconds);
                var eased = t * t * (3.0 - 2.0 * t);

                var zoom = (float)(fromZoom + (toZoom - fromZoom) * eased);
                var x = (float)(from.X + (to.X - from.X) * eased);
                var y = (float)(from.Y + (to.Y - from.Y) * eased);

                canvas.Viewport.Zoom = zoom;
                canvas.Viewport.Focus(new System.Drawing.PointF(x * zoom + offset, y * zoom));
                canvas.Refresh();

                if (t < 1.0) return;

                _glide.Stop();
                _glide = null;
            };

            _glide.Start();
        }

        void Float()
        {
            _dock.Undock();

            _frame.Padding = new Thickness(ResizeBand);
            WindowChrome.SetWindowChrome(this, Chrome());
        }

        static WindowChrome Chrome() => new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(ResizeBand),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };

        void StartTerminal(int columns, int rows)
        {
            if (_terminal != null) return;

            _component.Project.MirrorToDisk();

            _terminal = new ShellSession();
            _terminal.Output += text => Dispatcher.BeginInvoke(new Action(
                () => Post(new { type = "terminal", text })));

            _terminal.Exited += () => Dispatcher.BeginInvoke(new Action(
                () => Post(new { type = "terminalExit" })));

            try
            {
                _terminal.Start(_component.Project.WorkingFolder);
            }
            catch (Exception exception)
            {
                Post(new { type = "terminal", text = "Could not start a shell: " + exception.Message });

                _terminal.Dispose();
                _terminal = null;
            }
        }

        /// <summary>Applies a file operation and tells the page whether it took.</summary>
        void Mutate(Func<string, string> operation, string argument)
        {
            var error = operation(argument);

            if (error != null)
            {
                Post(new { type = "status", text = error, kind = "error" });
                return;
            }

            _component.Language.Invalidate(_component.Project);
            _component.OnDisplayExpired(true);

            SendProject();
            SendState();
        }

        void Compile()
        {
            SendState();

            _component.Compile(result =>
            {
                Post(new
                {
                    type = "diagnostics",
                    items = result.Diagnostics.Select(Describe)
                });

                SendState();
            });
        }

        // ----- outgoing ---------------------------------------------------------------------

        /// <summary>Re-sends the project after it changed underneath the page, on disk or by menu.</summary>
        internal void Refresh()
        {
            SendProject();
            SendState();
        }

        void SendProject()
        {
            Post(new
            {
                type = "project",
                files = _component.Project.Files.Select(f => new
                {
                    name = f.Name,
                    content = f.Content,
                    language = f.IsSource ? "csharp" : "xml",
                    locked = ScriptProject.IsProtected(f.Name)
                }),
                folder = _component.Project.WorkingFolder,
                breakpoints = _component.Breakpoints.ToDictionary(pair => pair.Key, pair => pair.Value),
                breakpointsEnabled = _component.BreakpointsEnabled
            });
        }

        void SendState()
        {
            Post(new
            {
                type = "state",
                stale = _component.IsStale,
                compiling = _component.IsCompiling,
                title = _component.OnPingDocument()?.DisplayName ?? "unsaved",
                build = _component.LastBuild,
                runtime = "net7.0 · Roslyn",
                docked = _dock.IsDocked,
                canDock = _dock.CanDock,
                dockLeft = _dock.OnLeft,
                references = _component.References.Count,
                parameters = Parameters()
            });
        }

        /// <summary>The parameter list the last successful build produced, for the sidebar.</summary>
        object Parameters()
        {
            var inputs = _component.Params.Input
                .Select(p => new { name = p.Name, type = p.TypeName, access = p.Access.ToString() });

            var outputs = _component.Params.Output
                .Select(p => new { name = p.Name, type = p.TypeName, access = p.Access.ToString() });

            return new { inputs, outputs };
        }

        static object Describe(CompileDiagnostic diagnostic)
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

        void Reply(string id, object payload)
        {
            if (string.IsNullOrEmpty(id)) return;

            Post(new { type = "reply", id, payload });
        }

        void Post(object payload)
        {
            if (!_ready || _web.CoreWebView2 == null) return;

            try
            {
                _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json));
            }
            catch (Exception)
            {
                // The page has gone or is being torn down. Nothing above this can handle it, and
                // an unhandled exception on the UI thread would end the Rhino session.
            }
        }
    }
}
