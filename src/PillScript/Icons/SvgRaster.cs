using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace PillScript.Icons
{
    /// <summary>
    /// Turns an icon's drawing into the bitmap Grasshopper draws on the canvas.
    ///
    /// WPF is already loaded for the editor, and its path mini-language is the one SVG uses, so
    /// Geometry.Parse reads a d attribute unchanged. That covers the whole file: these icons are
    /// filled outlines on a 256 square with no gradients, strokes or text.
    /// </summary>
    internal static class SvgRaster
    {
        public static Bitmap Raster(string svg, int size, System.Drawing.Color ink)
        {
            try
            {
                return Draw(svg, size, ink);
            }
            catch (Exception)
            {
                // A drawing that fails to parse falls back to the pill, leaving the component
                // with an icon.
                return null;
            }
        }

        static Bitmap Draw(string svg, int size, System.Drawing.Color ink)
        {
            var root = XDocument.Parse(svg).Root;
            if (root == null) return null;

            var box = Box(root);
            var shapes = new GeometryGroup { FillRule = FillRule.Nonzero };

            foreach (var element in root.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                // The set puts a transparent square behind every icon, which is not part of it.
                if ((string)element.Attribute("fill") == "none") continue;

                var d = (string)element.Attribute("d");
                if (string.IsNullOrWhiteSpace(d)) continue;

                shapes.Children.Add(Geometry.Parse(d));
            }

            if (shapes.Children.Count == 0) return null;

            var scale = size / Math.Max(box.Width, box.Height);
            var transform = new TransformGroup();
            transform.Children.Add(new TranslateTransform(-box.X, -box.Y));
            transform.Children.Add(new ScaleTransform(scale, scale));
            shapes.Transform = transform;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawGeometry(
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(ink.A, ink.R, ink.G, ink.B)),
                    null, shapes);
            }

            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);

            return ToBitmap(rendered);
        }

        static System.Windows.Rect Box(XElement root)
        {
            var value = (string)root.Attribute("viewBox");
            var parts = (value ?? string.Empty)
                .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4) return new System.Windows.Rect(0, 0, 256, 256);

            var numbers = new double[4];
            for (var i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                    return new System.Windows.Rect(0, 0, 256, 256);
            }

            return new System.Windows.Rect(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        /// <summary>
        /// Through a PNG in memory. That is the one conversion where the transparency survives
        /// the crossing between WPF and System.Drawing.
        /// </summary>
        static Bitmap ToBitmap(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            using var loaded = new Bitmap(stream);
            return new Bitmap(loaded);
        }
    }
}
