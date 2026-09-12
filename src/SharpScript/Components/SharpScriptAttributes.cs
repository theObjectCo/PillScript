using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace SharpScript.Components
{
    /// <summary>
    /// Draws the component, and lays a blue plate under it while its editor window is open. On a
    /// canvas with several script components that is the only way to tell which one the editor in
    /// front of you belongs to.
    /// </summary>
    internal sealed class SharpScriptAttributes : GH_ComponentAttributes
    {
        static readonly Color Open = Color.FromArgb(0, 122, 204);
        const int Offset = 7;

        public SharpScriptAttributes(SharpScriptComponent owner) : base(owner) { }

        new SharpScriptComponent Owner => (SharpScriptComponent)base.Owner;

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            // Painted before the component so the capsule sits on top of it, which is what makes
            // it read as a plate rather than as a box drawn over the wires.
            if (channel == GH_CanvasChannel.Objects && Owner.IsEditorOpen)
            {
                var plate = Rectangle.Round(Bounds);
                plate.Inflate(Offset, Offset);

                var state = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var path = Rounded(plate, 5))
                using (var brush = new SolidBrush(Open))
                {
                    graphics.FillPath(brush, path);
                }

                graphics.Restore(state);
            }

            base.Render(canvas, graphics, channel);
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            Owner.OpenEditor();
            return GH_ObjectResponse.Handled;
        }

        static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
