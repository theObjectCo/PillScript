using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Interop;
using Grasshopper;

namespace SharpScript.Editor
{
    /// <summary>
    /// Puts the editor window inside the Grasshopper window as a child, taking a strip down one
    /// side of the canvas, and puts it back when asked. The canvas keeps its full size underneath;
    /// what changes is that the editor is clipped to Grasshopper, moves with it and cannot be
    /// lost behind it.
    /// </summary>
    internal sealed class DockHost
    {
        const int GwlStyle = -16;
        const int WsChild = 0x40000000;
        const int WsPopup = unchecked((int)0x80000000);
        const int WsThickFrame = 0x00040000;

        const uint SwpNoZOrder = 0x0004;
        const uint SwpNoActivate = 0x0010;
        const uint SwpFrameChanged = 0x0020;

        readonly Window _window;

        Form _host;
        Control _canvas;
        IntPtr _handle;
        Rect _floating;
        int _style;

        public DockHost(Window window) { _window = window; }

        public bool IsDocked { get; private set; }

        /// <summary>How much of the canvas the editor takes.</summary>
        public double Fraction { get; private set; } = 0.45;

        /// <summary>Which side of the canvas it sits on.</summary>
        public bool OnLeft { get; private set; }

        /// <summary>
        /// The width the editor is actually drawn at, in real pixels. The splitter measures from
        /// this rather than from the window's own width, which WPF reports in units that are a
        /// display scale away from the ones every other number here is in.
        /// </summary>
        public int CurrentWidth { get; private set; }

        public bool CanDock => Instances.DocumentEditor != null;

        /// <summary>Docks against the chosen side of the canvas.</summary>
        public void Dock(bool onLeft)
        {
            if (IsDocked) return;

            OnLeft = onLeft;

            _host = Instances.DocumentEditor;
            if (_host == null) return;

            _handle = new WindowInteropHelper(_window).Handle;
            if (_handle == IntPtr.Zero) return;

            _floating = new Rect(_window.Left, _window.Top, _window.Width, _window.Height);
            _style = GetWindowLong(_handle, GwlStyle);

            // A child has no frame of its own: it is sized by the splitter and by its parent.
            SetWindowLong(_handle, GwlStyle, (_style & ~WsPopup & ~WsThickFrame) | WsChild);
            SetParent(_handle, _host.Handle);

            _host.Resize += OnHostResize;

            _canvas = Instances.ActiveCanvas;
            if (_canvas != null) _canvas.SizeChanged += OnHostResize;

            IsDocked = true;

            Layout();
            BringWindowToTop(_handle);
        }

        public void Undock()
        {
            if (!IsDocked) return;

            Release();
            SetWindowLong(_handle, GwlStyle, _style);

            // The owner is re-applied because detaching cleared it along with the child bit.
            var owner = Rhino.RhinoApp.MainWindowHandle();
            if (owner != IntPtr.Zero) new WindowInteropHelper(_window).Owner = owner;

            SetWindowPos(_handle, IntPtr.Zero,
                (int)_floating.X, (int)_floating.Y, (int)_floating.Width, (int)_floating.Height,
                SwpNoZOrder | SwpFrameChanged);

            _window.Activate();
        }

        /// <summary>Moves the split, keeping the editor between a fifth and four fifths of the width.</summary>
        public void SetFraction(double fraction)
        {
            Fraction = Math.Min(0.85, Math.Max(0.15, fraction));
            if (IsDocked) Layout();
        }

        /// <summary>
        /// Detaches from Grasshopper and repaints the area the editor was covering. Used on its
        /// own when the window is closing, where there is no point putting it back where it used
        /// to float, but every point in leaving the canvas whole.
        /// </summary>
        public void Release()
        {
            if (!IsDocked) return;

            IsDocked = false;

            var host = _host;
            var canvas = _canvas;

            if (host != null) host.Resize -= OnHostResize;
            if (canvas != null) canvas.SizeChanged -= OnHostResize;

            _host = null;
            _canvas = null;

            SetParent(_handle, IntPtr.Zero);

            // Windows does not repaint what a departing child was covering, so Grasshopper is
            // asked to, or the canvas keeps a rectangle of stale pixels until it is panned.
            try
            {
                canvas?.Invalidate(true);
                host?.Invalidate(true);
                host?.Update();
            }
            catch (Exception)
            {
                // Repainting is a courtesy; failing at it must not stop the window from closing.
            }
        }

        /// <summary>Moves the editor to the chosen side of the canvas.</summary>
        public void SetSide(bool onLeft)
        {
            if (OnLeft == onLeft) return;

            OnLeft = onLeft;
            Layout();
        }

        void OnHostResize(object sender, EventArgs e) => Layout();

        void Layout()
        {
            if (!IsDocked || _host == null) return;

            var area = CanvasArea();
            var width = (int)Math.Round(area.Width * Fraction);

            var x = OnLeft ? area.Left : area.Right - width;
            CurrentWidth = width;

            SetWindowPos(_handle, IntPtr.Zero,
                x, area.Top, width, area.Height,
                SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        /// <summary>
        /// The canvas rectangle in the Grasshopper window's own coordinates. Docking into that
        /// rather than into the whole window keeps the ribbon and the status bar clear, which is
        /// the difference between sitting beside the canvas and sitting on top of Grasshopper.
        /// </summary>
        Rectangle CanvasArea()
        {
            var canvas = Instances.ActiveCanvas ?? _canvas;

            if (canvas == null || canvas.IsDisposed || !canvas.IsHandleCreated)
                return new Rectangle(System.Drawing.Point.Empty, _host.ClientSize);

            var corner = _host.PointToClient(canvas.PointToScreen(System.Drawing.Point.Empty));
            return new Rectangle(corner, canvas.Size);
        }

        /// <summary>The width the splitter measures against, which is the canvas and not the window.</summary>
        public int HostWidth => IsDocked && _host != null ? CanvasArea().Width : 0;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr child, IntPtr parent);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(
            IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
