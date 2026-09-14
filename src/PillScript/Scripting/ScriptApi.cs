using System;
using Grasshopper.Kernel;

namespace PillScript
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

        /// <summary>
        /// Registers the controls this script contributes to the Rhino panel and receives the
        /// values they currently hold. Override it, call the registrar, and turn Publish to panel
        /// on in the component's menu. It runs before every solve, so the variables it writes into
        /// hold what the panel holds.
        /// </summary>
        public virtual void RegisterUi(UiRegistrar register) { }

        /// <summary>The Rhino document the Grasshopper document belongs to.</summary>
        public Rhino.RhinoDoc RhinoDocument { get; internal set; }

        /// <summary>
        /// Draws into the Rhino viewport, in the wire pass. Override it to add anything the output
        /// geometry does not already show: a label, a direction, a diagram. Grasshopper draws the
        /// outputs first, and this runs after them.
        ///
        /// It runs on every redraw, not on every solve, so the work belongs in a field the solve
        /// filled in. Anything thrown here is caught, reported once and stops further drawing
        /// until the next compile, since an exception per frame would be unreadable.
        /// </summary>
        public virtual void DrawWires(IGH_PreviewArgs args) { }

        /// <summary>The shaded pass, for meshes and breps drawn with a material.</summary>
        public virtual void DrawMeshes(IGH_PreviewArgs args) { }

        /// <summary>
        /// What the drawing covers, so that Zoom Extents and the clipping planes account for it.
        /// Anything drawn outside this box is clipped away at some camera angles. Grasshopper asks
        /// for it often, so it should return a stored value and not compute one.
        /// </summary>
        public virtual Rhino.Geometry.BoundingBox DrawBounds => Rhino.Geometry.BoundingBox.Empty;

        internal Action<string> PrintSink { get; set; }

        /// <summary>Writes a line to the component's output panel.</summary>
        public void Print(object value) => PrintSink?.Invoke(value?.ToString() ?? "null");

        /// <summary>Writes a formatted line to the component's output panel.</summary>
        /// <summary>
        /// With arguments the text is formatted; with none it is printed as it stands. Without
        /// that check a line containing a brace, as any piece of JSON does, throws instead of
        /// printing.
        /// </summary>
        public void Print(string format, params object[] args)
            => PrintSink?.Invoke(args == null || args.Length == 0
                ? format
                : string.Format(format, args));

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
