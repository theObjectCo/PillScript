using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;

namespace PillScript
{
    /// <summary>
    /// What a script registers its panel controls through. Each call does two things at once: it
    /// tells the panel what to draw, and it writes what that control is currently set to into the
    /// variable handed to it. A control is declared and read in one line, and the code that uses
    /// the value never looks anything up by name.
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

        /// <summary>A number typed in, with no bounds to drag between.</summary>
        public void Number(string name, double value, out double current, string label = null)
        {
            Add(new UiControl { Kind = "number", Name = name, Label = label, Value = value });
            current = AsNumber(name, value);
        }

        /// <summary>The same, rounded, for a control that stands for a count.</summary>
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

        /// <summary>A switch. The note goes beside it, where the label leaves a doubt.</summary>
        public void Toggle(string name, bool value, out bool current,
                           string label = null, string note = null)
        {
            Add(new UiControl { Kind = "toggle", Name = name, Label = label, Note = note, Value = value });
            current = AsFlag(name, value);
        }

        /// <summary>One of a list. The options may be worked out rather than written down.</summary>
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

            // An option that has since been taken away falls back, rather than leaving the panel
            // holding a value it cannot show.
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

        /// <summary>A swatch that opens the colour picker Rhino uses everywhere else.</summary>
        public void Colour(string name, Color value, out Color current, string label = null)
        {
            Add(new UiControl { Kind = "colour", Name = name, Label = label, Value = value });
            current = AsColour(name, value);
        }

        /// <summary>
        /// A layer of the Rhino document, by its full path. The list is read when the panel
        /// draws, so layers added since do not need a recompile to show up.
        /// </summary>
        public void Layer(string name, out string current, string value = null, string label = null)
        {
            // Nothing chosen means the layer the document is drawing on, which is what a bake
            // would land on anyway, rather than an empty box that looks like a missing value.
            var start = value ?? Rhino.RhinoDoc.ActiveDoc?.Layers?.CurrentLayer?.FullPath ?? string.Empty;

            Add(new UiControl { Kind = "layer", Name = name, Label = label, Value = start });
            current = AsText(name, start);
        }

        /// <summary>
        /// Something to press. It is true for the one solve the press caused and false on every
        /// other, so a script can act on it without having to remember whether it already did.
        /// A quiet one is drawn plainly, for the button standing beside the main action.
        /// </summary>
        public void Button(string name, out bool pressed, string label = null, bool quiet = false)
        {
            Add(new UiControl { Kind = "button", Name = name, Label = label, Quiet = quiet, Value = false });
            pressed = AsFlag(name, false);
        }

        /// <summary>A line of text, for a heading or a word of explanation.</summary>
        public void Caption(string text)
            => Add(new UiControl { Kind = "caption", Name = string.Empty, Label = text });

        void Add(UiControl control)
        {
            // A control that carries a value needs a name to carry it under. A caption does not.
            if (control.Kind != "caption" && string.IsNullOrWhiteSpace(control.Name)) return;

            _controls.Add(control);
        }
    }
}
