using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SharpScript.Editor
{
    /// <summary>
    /// Minimising, maximising, moving and resizing a window that has no frame of its own. The
    /// page covers the window edge to edge and reports a press near an edge; this hands that
    /// press to the system, which runs the move or resize loop as it would for any window.
    /// </summary>
    internal sealed class WindowFrame
    {
        const int WmNcLeftButtonDown = 0x00A1;

        readonly Window _window;

        public WindowFrame(Window window) { _window = window; }

        /// <summary>Set while the window is a child of Grasshopper, where none of this applies.</summary>
        public bool Fixed { get; set; }

        public void Apply(string action)
        {
            switch (action)
            {
                case "minimize":
                    _window.WindowState = WindowState.Minimized;
                    break;

                case "maximize":
                    _window.WindowState = _window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    break;

                case "close":
                    _window.Close();
                    break;

                default:
                    var part = PartOf(action);
                    if (part != 0) Grab(part);

                    break;
            }
        }

        /// <summary>The piece of frame the page says was grabbed, as the system numbers them.</summary>
        static int PartOf(string action)
        {
            switch (action)
            {
                case "drag": return 2;
                case "resize-left": return 10;
                case "resize-right": return 11;
                case "resize-top": return 12;
                case "resize-topleft": return 13;
                case "resize-topright": return 14;
                case "resize-bottom": return 15;
                case "resize-bottomleft": return 16;
                case "resize-bottomright": return 17;
                default: return 0;
            }
        }

        /// <summary>
        /// DragMove cannot be used here: by the time the page's message arrives WPF no longer
        /// sees the press that started it, and it cannot resize at all.
        /// </summary>
        void Grab(int part)
        {
            if (Fixed) return;

            var handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;

            ReleaseCapture();
            SendMessage(handle, WmNcLeftButtonDown, (IntPtr)part, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
