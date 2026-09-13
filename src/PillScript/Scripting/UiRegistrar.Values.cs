using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Rhino.Geometry;

namespace PillScript
{
    /// <summary>
    /// The other half of the registrar: the value a control currently holds. The panel sends
    /// values back as the simplest type JSON carries, so each kind is read defensively and falls
    /// back to the default the script registered instead of throwing during a solve.
    /// </summary>
    public sealed partial class UiRegistrar
    {
        double AsNumber(string name, double fallback)
        {
            if (!Read(name, out var value)) return fallback;

            switch (value)
            {
                case double number: return number;
                case int whole: return whole;
                case bool flag: return flag ? 1 : 0;
                case string text:
                    return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : fallback;
                default: return fallback;
            }
        }

        string AsText(string name, string fallback)
        {
            if (!Read(name, out var value)) return fallback;

            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
        }

        bool AsFlag(string name, bool fallback)
        {
            if (!Read(name, out var value)) return fallback;

            switch (value)
            {
                case bool flag: return flag;
                case double number: return Math.Abs(number) > 1e-9;
                case int whole: return whole != 0;
                case string text: return bool.TryParse(text, out var parsed) ? parsed : fallback;
                default: return fallback;
            }
        }

        Vector3d AsVector(string name, Vector3d fallback)
        {
            if (!Read(name, out var value)) return fallback;

            switch (value)
            {
                case Vector3d vector: return vector;
                case Point3d point: return new Vector3d(point);
                case string text: return ParseVector(text, fallback);
                default: return fallback;
            }
        }

        Color AsColour(string name, Color fallback)
        {
            if (!Read(name, out var value)) return fallback;

            switch (value)
            {
                case Color colour: return colour;
                case string text: return ParseColour(text, fallback);
                default: return fallback;
            }
        }

        bool Read(string name, out object value)
        {
            value = null;
            return name != null && _values.TryGetValue(name, out value) && value != null;
        }

        /// <summary>Three numbers separated by any non-numeric character, as the document has them.</summary>
        internal static Vector3d ParseVector(string text, Vector3d fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;

            var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return fallback;

            var numbers = new double[3];
            for (var i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Any, CultureInfo.InvariantCulture, out numbers[i]))
                    return fallback;
            }

            return new Vector3d(numbers[0], numbers[1], numbers[2]);
        }

        internal static string WriteVector(Vector3d vector)
            => string.Join(",", new[] { vector.X, vector.Y, vector.Z }
                .Select(n => n.ToString("R", CultureInfo.InvariantCulture)));

        /// <summary>A colour as the page writes it: #rrggbb, or #rrggbbaa when it is not opaque.</summary>
        internal static Color ParseColour(string text, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;

            var hex = text.Trim().TrimStart('#');
            if (hex.Length != 6 && hex.Length != 8) return fallback;

            if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
                !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
                !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                return fallback;

            var a = 255;
            if (hex.Length == 8 &&
                !int.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
                return fallback;

            return Color.FromArgb(a, r, g, b);
        }

        internal static string WriteColour(Color colour)
            => colour.A == 255
                ? "#" + colour.R.ToString("X2") + colour.G.ToString("X2") + colour.B.ToString("X2")
                : "#" + colour.R.ToString("X2") + colour.G.ToString("X2") + colour.B.ToString("X2")
                      + colour.A.ToString("X2");

        /// <summary>The buttons, cleared after the solve their press triggered.</summary>
        internal IEnumerable<string> Buttons
            => _controls.Where(c => c.Kind == "button").Select(c => c.Name);
    }
}
