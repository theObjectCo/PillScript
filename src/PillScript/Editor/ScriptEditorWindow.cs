using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PillScript.Components;

namespace PillScript.Editor
{
    /// <summary>
    /// The window the editor runs in. It hosts the page, places it on the screen or inside the
    /// Grasshopper window, and handles the few requests that need a window. Everything about the
    /// script itself is in the view model, which the bridge calls into.
    /// </summary>
    internal sealed class ScriptEditorWindow : Window, IEditorShell
    {
        const string VirtualHost = "pillscript.local";

        // The page reaches the window edge. Resizing still works because the page reports a press
        // near an edge and the frame hands that press to the system.
        const int ResizeBand = 0;

        readonly PillScriptComponent _component;
        readonly WebView2 _web = new WebView2();
        readonly Border _surface;
        readonly DockHost _dock;
        readonly WindowFrame _frame;
        readonly CanvasNavigator _canvas;
        readonly EditorBridge _bridge;

        int _splitFrom;

        public ScriptEditorWindow(PillScriptComponent component)
        {
            _component = component;

            Title = "C# Script - " + component.NickName;
            Width = 1180;
            Height = 780;
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Owned by Rhino instead of topmost. An owned window sits above its owner and
            // nothing else, so it stays over the canvas, hides when Rhino is minimised and does
            // not float over other applications.
            var owner = Rhino.RhinoApp.MainWindowHandle();
            if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;

            _surface = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(ResizeBand),
                Child = _web
            };

            Content = _surface;

            // The page draws its own title bar, so the system one is removed.
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, Chrome());

            _dock = new DockHost(this);
            _frame = new WindowFrame(this);
            _canvas = new CanvasNavigator(component);
            _bridge = new EditorBridge(component, this);

            component.Logged += OnComponentLog;
            DebugSession.Paused += OnPaused;
            DebugSession.Resumed += OnResumed;

            Closing += (_, __) =>
            {
                // Leaving the host here and not on Closed: by then the window handle is gone and
                // Grasshopper would be left holding a child that was never detached.
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

            _canvas.Stop();
        }

        void OnComponentLog(string text, string kind) => _bridge.Post(EditorPayloads.Log(text, kind));
        void OnPaused(DebugStop stop) => _bridge.Post(EditorPayloads.Paused(stop));
        void OnResumed() => _bridge.Post(EditorPayloads.Resumed());

        /// <summary>Re-sends the project after it changed underneath the page, on disk or by menu.</summary>
        internal void Refresh() => _bridge.Refresh();

        // ----- starting the page ---------------------------------------------------------------

        async void Start()
        {
            var webFolder = Path.Combine(
                Path.GetDirectoryName(typeof(ScriptEditorWindow).Assembly.Location) ?? string.Empty, "web");

            if (!Directory.Exists(webFolder))
            {
                Fail("The editor resources are missing from " + webFolder + ".");
                return;
            }

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PillScript", "webview");

            Directory.CreateDirectory(userData);

            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception exception)
            {
                Fail("The editor needs the WebView2 runtime, which could not be started: " + exception.Message);
                return;
            }

            try
            {
                var page = _web.CoreWebView2;
                page.SetVirtualHostNameToFolderMapping(
                    VirtualHost, webFolder, CoreWebView2HostResourceAccessKind.Allow);

                page.Settings.AreDefaultContextMenusEnabled = false;
                page.Settings.IsStatusBarEnabled = false;
                page.Settings.AreDevToolsEnabled = true;

                _bridge.Attach(page);
                page.Navigate("https://" + VirtualHost + "/index.html");
            }
            catch (Exception exception)
            {
                Fail("The editor could not be set up: " + exception.Message);
            }
        }

        void Fail(string message)
        {
            MessageBox.Show(this, message, "C# Script", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }

        // ----- what only a window can do --------------------------------------------------------

        public DockStatus Dock => new DockStatus(_dock.IsDocked, _dock.CanDock, _dock.OnLeft);

        public void ApplyWindowCommand(string action) => _frame.Apply(action);

        public void BeginSplit() => _splitFrom = _dock.CurrentWidth;

        public void Split(int offset)
        {
            if (!_dock.IsDocked || _dock.HostWidth <= 0) return;

            _dock.SetFraction((_splitFrom + offset) / (double)_dock.HostWidth);
        }

        /// <summary>
        /// Centring on the whole canvas would put the component behind a docked editor, so the
        /// view is offset by half the width the editor covers.
        /// </summary>
        public void Locate()
        {
            var covered = 0f;

            if (_dock.IsDocked)
                covered = _dock.OnLeft ? -_dock.CurrentWidth / 2f : _dock.CurrentWidth / 2f;

            _canvas.Locate(covered);
        }

        public string PickAssembly()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Reference an assembly",
                Filter = "Assemblies (*.dll)|*.dll|All files (*.*)|*.*",
                CheckFileExists = true
            };

            return dialog.ShowDialog(this) == true ? dialog.FileName : string.Empty;
        }

        /// <summary>
        /// Docks and undocks in the order the window frame survives. WindowChrome negotiates the
        /// frame with the desktop compositor, and a child window has no frame, so the chrome is
        /// removed before the window becomes a child and restored only once it is a top level
        /// window again. The other order throws inside WPF.
        /// </summary>
        public void ApplyDock(bool onLeft)
        {
            if (_dock.IsDocked)
            {
                // Choosing the side it is already docked to undocks it back into a window.
                if (_dock.OnLeft == onLeft) Float();
                else _dock.SetSide(onLeft);

                return;
            }

            WindowChrome.SetWindowChrome(this, null);
            _surface.Padding = new Thickness(0);
            _frame.Fixed = true;

            _dock.Dock(onLeft);
        }

        void Float()
        {
            _dock.Undock();

            _frame.Fixed = false;
            _surface.Padding = new Thickness(ResizeBand);
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
    }
}
