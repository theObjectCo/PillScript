using System.Collections.Generic;

namespace PillScript
{
    /// <summary>One control, as the script registered it and as the panel will draw it.</summary>
    public sealed class UiControl
    {
        public string Kind { get; internal set; } = "slider";
        public string Name { get; internal set; } = string.Empty;
        public string Label { get; internal set; }

        /// <summary>A word beside the control itself, where the label alone leaves a doubt.</summary>
        public string Note { get; internal set; }

        public double Minimum { get; internal set; }
        public double Maximum { get; internal set; } = 1;
        public double Step { get; internal set; }
        public List<string> Options { get; internal set; }

        /// <summary>A button drawn plainly, for the one standing beside the main action.</summary>
        public bool Quiet { get; internal set; }

        /// <summary>What the script said the control should start at.</summary>
        public object Value { get; internal set; }
    }
}
