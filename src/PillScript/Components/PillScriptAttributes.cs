using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;

namespace PillScript.Components
{
    /// <summary>
    /// Draws the component, and lays a plate under it while its editor window is open. On a canvas
    /// with several script components that is the only way to tell which one the editor in front of
    /// you belongs to.
    ///
    /// The plate is halved down the middle and wears the two colours of the pill, so what the
    /// canvas shows and what the icon shows are recognisably the same thing.
    /// </summary>
    internal sealed class PillScriptAttributes : GH_ComponentAttributes
    {
        // The body colours of the two halves of the capsule, taken from the middle of the
        // gradients the icPill symbol is painted with.
        static readonly Color Left = Color.FromArgb(0x1A, 0x44, 0xB4);
        static readonly Color Right = Color.FromArgb(0xC0, 0x24, 0x14);

        const int Offset = 7;

        public PillScriptAttributes(PillScriptComponent owner) : base(owner) { }

        new PillScriptComponent Owner => (PillScriptComponent)base.Owner;

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
                {
                    var middle = plate.Left + plate.Width / 2;

                    Half(graphics, path, Rectangle.FromLTRB(plate.Left, plate.Top, middle, plate.Bottom), Left);
                    Half(graphics, path, Rectangle.FromLTRB(middle, plate.Top, plate.Right, plate.Bottom), Right);
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

        /// <summary>
        /// Fills the part of the plate that falls on one side of the middle. The clip is narrowed
        /// rather than replaced, so whatever the canvas had already masked off stays masked off,
        /// and it is put back afterwards.
        /// </summary>
        static void Half(Graphics graphics, GraphicsPath plate, Rectangle side, Color colour)
        {
            var state = graphics.Save();
            graphics.SetClip(side, CombineMode.Intersect);

            using (var brush = new SolidBrush(colour))
                graphics.FillPath(brush, plate);

            graphics.Restore(state);
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
