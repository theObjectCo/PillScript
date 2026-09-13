using System.Collections.Generic;

namespace PillScript
{
    /// <summary>One control, as the script registered it and as the panel will draw it.</summary>
    public sealed class UiControl
    {
        public string Kind { get; internal set; } = "slider";
        public string Name { get; internal set; } = string.Empty;
        public string Label { get; internal set; }

        /// <summary>A note beside the control, for a label that needs explaining.</summary>
        public string Note { get; internal set; }

        public double Minimum { get; internal set; }
        public double Maximum { get; internal set; } = 1;
        public double Step { get; internal set; }
        public List<string> Options { get; internal set; }

        /// <summary>A button drawn without emphasis, for a secondary action.</summary>
        public bool Quiet { get; internal set; }

        /// <summary>A glyph on a button: bake, run, refresh, add or remove.</summary>
        public string Icon { get; internal set; }

        /// <summary>The value the script registered the control with.</summary>
        public object Value { get; internal set; }
    }
}
