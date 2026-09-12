using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PillScript.Components
{
    /// <summary>
    /// Draws the component icon rather than shipping a bitmap, so the plugin stays a single file.
    /// </summary>
    internal static class ComponentIcon
    {
        static Bitmap _bitmap;

        public static Bitmap Bitmap => _bitmap ??= Draw();

        static Bitmap Draw()
        {
            var bitmap = new Bitmap(24, 24);

            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var background = new SolidBrush(Color.FromArgb(0, 122, 204)))
            using (var path = new GraphicsPath())
            {
                path.AddArc(1, 1, 8, 8, 180, 90);
                path.AddArc(15, 1, 8, 8, 270, 90);
                path.AddArc(15, 15, 8, 8, 0, 90);
                path.AddArc(1, 15, 8, 8, 90, 90);
                path.CloseFigure();

                graphics.FillPath(background, path);
            }

            using (var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var text = new SolidBrush(Color.White))
            using (var format = new StringFormat
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center
                   })
            {
                graphics.DrawString("C#", font, text, new RectangleF(0, 0, 24, 24), format);
            }

            return bitmap;
        }
    }
}
