using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PillScript
{
    /// <summary>One control, as the script registered it and as the panel will draw it.</summary>
    public sealed class UiControl
    {
        public string Kind { get; internal set; } = "slider";
        public string Name { get; internal set; } = string.Empty;
        public string Label { get; internal set; }
        public double Minimum { get; internal set; }
        public double Maximum { get; internal set; } = 1;
        public double Step { get; internal set; }
        public List<string> Options { get; internal set; }

        /// <summary>What the script said the control should start at.</summary>
        public object Value { get; internal set; }
    }

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
    public sealed class UiRegistrar
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
            Add(new UiControl { Kind = "number", Name = name, Label = label, Value = (double)value });
            current = (int)Math.Round(AsNumber(name, value));
        }

        /// <summary>A switch.</summary>
        public void Toggle(string name, bool value, out bool current, string label = null)
        {
            Add(new UiControl { Kind = "toggle", Name = name, Label = label, Value = value });
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

        /// <summary>
        /// Something to press. It is true for the one solve the press caused and false on every
        /// other, so a script can act on it without having to remember whether it already did.
        /// </summary>
        public void Button(string name, out bool pressed, string label = null)
        {
            Add(new UiControl { Kind = "button", Name = name, Label = label, Value = false });
            pressed = AsFlag(name, false);
        }

        /// <summary>A line of text, for a heading or a word of explanation.</summary>
        public void Text(string text)
            => Add(new UiControl { Kind = "text", Name = string.Empty, Label = text });

        // ----- what a control currently holds -------------------------------------------------

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

        bool Read(string name, out object value)
        {
            value = null;
            return name != null && _values.TryGetValue(name, out value) && value != null;
        }

        void Add(UiControl control)
        {
            // A control that carries a value needs a name to carry it under. Text does not.
            if (control.Kind != "text" && string.IsNullOrWhiteSpace(control.Name)) return;

            _controls.Add(control);
        }

        /// <summary>What every named control starts at, for a component that holds nothing yet.</summary>
        internal Dictionary<string, object> Declared()
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var control in _controls)
            {
                if (control.Value != null && !string.IsNullOrWhiteSpace(control.Name))
                    values[control.Name] = control.Value;
            }

            return values;
        }

        /// <summary>The buttons, which are cleared once the solve their press caused is over.</summary>
        internal IEnumerable<string> Buttons
            => _controls.Where(c => c.Kind == "button").Select(c => c.Name);
    }
}
