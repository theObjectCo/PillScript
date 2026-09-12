using System;
using Grasshopper.Kernel;

namespace SharpScript
{
    /// <summary>
    /// Optional base class for a script. Deriving from it gives the script access to the
    /// component it runs in, the active Rhino document and a Print sink that ends up in
    /// the component's "out" parameter.
    /// </summary>
    public abstract class ScriptBase
    {
        /// <summary>The component executing this script.</summary>
        public GH_Component Component { get; internal set; }

        /// <summary>Zero based index of the current solve iteration.</summary>
        public int Iteration { get; internal set; }

        /// <summary>The Rhino document the Grasshopper document belongs to.</summary>
        public Rhino.RhinoDoc RhinoDocument { get; internal set; }

        internal Action<string> PrintSink { get; set; }

        /// <summary>Writes a line to the component's output panel.</summary>
        public void Print(object value) => PrintSink?.Invoke(value?.ToString() ?? "null");

        /// <summary>Writes a formatted line to the component's output panel.</summary>
        public void Print(string format, params object[] args) => PrintSink?.Invoke(string.Format(format, args));

        /// <summary>Adds a remark bubble to the component.</summary>
        public void Remark(string message) => AddMessage(GH_RuntimeMessageLevel.Remark, message);

        /// <summary>Adds a warning bubble to the component.</summary>
        public void Warning(string message) => AddMessage(GH_RuntimeMessageLevel.Warning, message);

        /// <summary>Adds an error bubble to the component.</summary>
        public void Error(string message) => AddMessage(GH_RuntimeMessageLevel.Error, message);

        void AddMessage(GH_RuntimeMessageLevel level, string message)
            => Component?.AddRuntimeMessage(level, message);
    }

    /// <summary>Overrides the nickname Grasshopper shows on the parameter.</summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class NameAttribute : Attribute
    {
        public NameAttribute(string nickname) { Nickname = nickname; }
        public string Nickname { get; }
    }

    /// <summary>Sets the parameter description shown in the tooltip.</summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public sealed class DescriptionAttribute : Attribute
    {
        public DescriptionAttribute(string text) { Text = text; }
        public string Text { get; }
    }

    /// <summary>Gives an input a persistent default value, used when nothing is wired in.</summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class DefaultAttribute : Attribute
    {
        public DefaultAttribute(object value) { Value = value; }
        public object Value { get; }
    }

    /// <summary>Marks an input as optional, so an empty socket does not abort the solve.</summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class OptionalAttribute : Attribute { }
}
