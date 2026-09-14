using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace PillScript.Components
{
    /// <summary>
    /// The viewport passes, forwarded to the compiled script. Grasshopper already previews whatever
    /// geometry leaves the outputs; these hooks are for the rest, where a script wants to draw
    /// something it never produces as a parameter.
    ///
    /// Drawing happens on every redraw, which is far more often than a solve, and it happens inside
    /// the display pipeline where an exception is a crash rather than a red component. Everything
    /// here is therefore wrapped, and a script that throws is reported once and then left out of
    /// the passes until the next compile.
    /// </summary>
    public partial class PillScriptComponent
    {
        /// <summary>Set when a draw call threw, and cleared when a new build replaces it.</summary>
        bool _drawFaulted;

        /// <summary>Whether this build overrides either draw method. Worked out once per build.</summary>
        bool? _drawsItself;

        internal void ClearDrawFault()
        {
            _drawFaulted = false;
            _drawsItself = null;
        }

        /// <summary>
        /// Grasshopper runs the viewport passes only for a component it believes has something to
        /// show, and it decides that from the outputs. A script that draws instead of producing
        /// geometry would never be asked, so overriding either draw method counts here as well.
        /// </summary>
        public override bool IsPreviewCapable => base.IsPreviewCapable || DrawsItself();

        bool DrawsItself()
        {
            if (_drawsItself.HasValue) return _drawsItself.Value;

            var drawn = false;
            if (_compiled?.Instance is ScriptBase script)
            {
                var type = script.GetType();
                drawn = Overridden(type, nameof(ScriptBase.DrawWires))
                        || Overridden(type, nameof(ScriptBase.DrawMeshes));
            }

            _drawsItself = drawn;
            return drawn;
        }

        static bool Overridden(Type type, string name)
        {
            var method = type.GetMethod(name, new[] { typeof(IGH_PreviewArgs) });
            return method != null && method.DeclaringType != typeof(ScriptBase);
        }

        /// <summary>
        /// The box Zoom Extents and the clipping planes work from. The base covers the geometry on
        /// the outputs, and the script adds whatever it draws beyond that.
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get
            {
                var box = base.ClippingBox;
                var script = Drawable();
                if (script == null) return box;

                try
                {
                    var extra = script.DrawBounds;
                    if (extra.IsValid) box.Union(extra);
                }
                catch (Exception exception)
                {
                    Faulted("DrawBounds", exception);
                }

                return box;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            var script = Drawable();
            if (script == null) return;

            try { script.DrawWires(args); }
            catch (Exception exception) { Faulted("DrawWires", exception); }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            var script = Drawable();
            if (script == null) return;

            try { script.DrawMeshes(args); }
            catch (Exception exception) { Faulted("DrawMeshes", exception); }
        }

        /// <summary>
        /// The instance to draw with, or null. A recompile swaps the field from under this, so it
        /// is read once, and a hidden or locked component is skipped because the passes still run
        /// for the clipping box.
        /// </summary>
        ScriptBase Drawable()
        {
            if (_drawFaulted || Hidden || Locked) return null;

            return _compiled?.Instance as ScriptBase;
        }

        /// <summary>
        /// Reports a failure and takes the script out of the passes. Nothing here adds a runtime
        /// message: that would expire the display and bring the pipeline straight back to the call
        /// that just threw.
        /// </summary>
        void Faulted(string where, Exception exception)
        {
            _drawFaulted = true;

            var fault = Scripting.ScriptFault.Unwrap(exception);
            Rhino.RhinoApp.WriteLine(
                "PillScript: " + NickName + "." + where + " threw, so it is no longer drawn. "
                + Scripting.ScriptFault.Describe(fault));
        }
    }
}
