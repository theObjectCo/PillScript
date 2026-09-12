using System;
using System.Drawing;
using System.Windows.Threading;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using PillScript.Components;

namespace PillScript.Editor
{
    /// <summary>
    /// Moves the Grasshopper canvas to the component the editor belongs to. Kept apart from the
    /// window because it is about the canvas, not about the editor, and because the viewport has
    /// enough of its own habits to be worth reading in one place.
    /// </summary>
    internal sealed class CanvasNavigator
    {
        static readonly TimeSpan Travel = TimeSpan.FromMilliseconds(500);
        static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(15);

        readonly PillScriptComponent _component;
        DispatcherTimer _glide;

        public CanvasNavigator(PillScriptComponent component) { _component = component; }

        /// <summary>
        /// Brings the canvas to the component at full zoom and selects it. The viewport has its
        /// own way of being moved: setting its target leaves the projection untouched, so nothing
        /// appears to happen. Focus is the method meant for it.
        /// </summary>
        /// <param name="covered">
        /// How many pixels of canvas the editor is covering, negative when it covers the left.
        /// The component is placed in the middle of what is left rather than behind the editor.
        /// </param>
        public void Locate(float covered)
        {
            var document = _component.OnPingDocument();
            var canvas = Instances.ActiveCanvas;

            if (document == null || canvas == null || _component.Attributes == null) return;

            document.DeselectAll();
            _component.Attributes.Selected = true;

            var bounds = _component.Attributes.Bounds;
            var to = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

            var region = canvas.Viewport.VisibleRegion;
            var from = new PointF(region.X + region.Width / 2f, region.Y + region.Height / 2f);

            Glide(canvas, from, canvas.Viewport.Zoom, to, 1f, covered);
        }

        public void Stop()
        {
            _glide?.Stop();
            _glide = null;
        }

        /// <summary>
        /// Walks the viewport from where it is to where it should be over half a second. A jump
        /// leaves you wondering which way the canvas went; a short move shows you.
        /// </summary>
        void Glide(GH_Canvas canvas, PointF from, float fromZoom, PointF to, float toZoom, float offset)
        {
            Stop();

            var started = DateTime.UtcNow;

            _glide = new DispatcherTimer(DispatcherPriority.Render) { Interval = Frame };
            _glide.Tick += (_, __) =>
            {
                var t = Math.Min(1.0, (DateTime.UtcNow - started).TotalMilliseconds / Travel.TotalMilliseconds);
                var eased = t * t * (3.0 - 2.0 * t);

                var zoom = (float)(fromZoom + (toZoom - fromZoom) * eased);
                var x = (float)(from.X + (to.X - from.X) * eased);
                var y = (float)(from.Y + (to.Y - from.Y) * eased);

                // Focus wants its point already multiplied by the zoom, and divides it again to
                // find the centre. That makes the nudge past a docked editor plain pixels.
                canvas.Viewport.Zoom = zoom;
                canvas.Viewport.Focus(new PointF(x * zoom + offset, y * zoom));
                canvas.Refresh();

                if (t < 1.0) return;

                Stop();
            };

            _glide.Start();
        }
    }
}
