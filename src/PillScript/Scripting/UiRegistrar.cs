using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;

namespace PillScript
{
    /// <summary>
    /// The object a script registers its panel controls through. Each call declares one control
    /// for the panel to draw and assigns that control's current value to the out variable passed
    /// in, so a control is declared and read in a single line:
    ///
    ///     register.Slider("radius", 1, 50, 10, out radius);
    ///
    /// RegisterUi runs before every solve, so those variables hold what the panel holds.
    /// </summary>
    public sealed partial class UiRegistrar
    {
        readonly IReadOnlyDictionary<string, object> _values;
        readonly List<UiControl> _controls = new List<UiControl>();

        internal UiRegistrar(IReadOnlyDictionary<string, object> values)
        {
            _values = values ?? new Dictionary<string, object>();
        }

        internal IReadOnlyList<UiControl> Controls => _controls;

        /// <summary>A number dragged between two bounds.</summary>
        public void Slider(
            string name, double minimum, double maximum, double value, out double current,
            string label = null, double step = 0)
        {
            Add(new UiControl
            {
                Kind = "slider",
                Name = name,
                Label = label,
                Minimum = minimum,
                Maximum = maximum,
                Step = step,
                Value = value
            });

            current = AsNumber(name, value);
        }

        /// <summary>A number typed in, with no bounds.</summary>
        public void Number(string name, double value, out double current, string label = null)
        {
            Add(new UiControl { Kind = "number", Name = name, Label = label, Value = value });
            current = AsNumber(name, value);
        }

        /// <summary>The same control, rounded to whole numbers, for counts.</summary>
        public void Whole(string name, int value, out int current, string label = null)
        {
            Add(new UiControl
            {
                Kind = "number",
                Name = name,
                Label = label,
                Step = 1,
                Value = (double)value
            });

            current = (int)Math.Round(AsNumber(name, value));
        }

        /// <summary>A switch. The optional note is drawn beside it, for a label that needs one.</summary>
        public void Toggle(string name, bool value, out bool current,
                           string label = null, string note = null)
        {
            Add(new UiControl { Kind = "toggle", Name = name, Label = label, Note = note, Value = value });
            current = AsFlag(name, value);
        }

        /// <summary>One value out of a list. The options can be computed at registration time.</summary>
        public void Choice(
            string name, IEnumerable<string> options, out string current,
            string value = null, string label = null)
        {
            var list = options?.ToList() ?? new List<string>();
            var first = value ?? list.FirstOrDefault() ?? string.Empty;

            Add(new UiControl
            {
                Kind = "choice",
                Name = name,
                Label = label,
                Options = list,
                Value = first
            });

            var chosen = AsText(name, first);

            // A held value whose option no longer exists falls back to the default. Otherwise
            // the panel would hold a value it cannot draw.
            current = list.Count == 0 || list.Contains(chosen) ? chosen : first;
        }

        /// <summary>A line to type in.</summary>
        public void Text(string name, string value, out string current, string label = null)
        {
            Add(new UiControl { Kind = "text", Name = name, Label = label, Value = value ?? string.Empty });
            current = AsText(name, value ?? string.Empty);
        }

        /// <summary>Three numbers on one row, for a direction or an offset.</summary>
        public void Vector(string name, Vector3d value, out Vector3d current, string label = null)
        {
            Add(new UiControl { Kind = "vector", Name = name, Label = label, Value = value });
            current = AsVector(name, value);
        }

        /// <summary>A swatch that opens Rhino's own colour picker.</summary>
        public void Colour(string name, Color value, out Color current, string label = null)
        {
            Add(new UiControl { Kind = "colour", Name = name, Label = label, Value = value });
            current = AsColour(name, value);
        }

        /// <summary>
        /// A layer of the Rhino document, by its full path. The list is read each time the panel
        /// draws, so a layer added after the last compile still appears.
        /// </summary>
        public void Layer(string name, out string current, string value = null, string label = null)
        {
            // With nothing chosen the control shows the document's current layer, which is where
            // a bake would land anyway. An empty box would read as a missing value.
            var start = value ?? Rhino.RhinoDoc.ActiveDoc?.Layers?.CurrentLayer?.FullPath ?? string.Empty;

            Add(new UiControl { Kind = "layer", Name = name, Label = label, Value = start });
            current = AsText(name, start);
        }

        /// <summary>
        /// A button. The value is true for the single solve the press caused and false in every
        /// other solve, so a script can act on it without tracking whether it already has. A quiet
        /// button is drawn without emphasis, for a secondary action. An icon name draws a glyph:
        /// bake, run, refresh, add or remove.
        /// </summary>
        public void Button(string name, out bool pressed,
                           string label = null, bool quiet = false, string icon = null)
        {
            Add(new UiControl
            {
                Kind = "button",
                Name = name,
                Label = label,
                Quiet = quiet,
                Icon = icon,
                Value = false
            });

            pressed = AsFlag(name, false);
        }

        /// <summary>A line of text, for a heading or a word of explanation.</summary>
        public void Caption(string text)
            => Add(new UiControl { Kind = "caption", Name = string.Empty, Label = text });

        void Add(UiControl control)
        {
            // Captions carry no value, so they need no name to store it under.
            if (control.Kind != "caption" && string.IsNullOrWhiteSpace(control.Name)) return;

            _controls.Add(control);
        }
    }
}
