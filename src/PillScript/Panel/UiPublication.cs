using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Rhino.Geometry;
using PillScript.Components;

namespace PillScript.Panel
{
    /// <summary>
    /// Gathers what the panel should show: every published component on the canvas, the controls
    /// its RegisterUi declares, and the values they currently hold. The panel draws it; nothing
    /// here knows how.
    /// </summary>
    internal static class UiPublication
    {
        /// <summary>
        /// Published components on the active canvas, in the order the document holds them, which
        /// is stable enough that sections do not jump about between refreshes.
        /// </summary>
        public static List<PillScriptComponent> Published()
        {
            var found = new List<PillScriptComponent>();
            var document = Instances.ActiveCanvas?.Document;

            if (document == null) return found;

            foreach (var obj in document.Objects)
            {
                if (obj is PillScriptComponent component && component.IsPublished) found.Add(component);
            }

            return found;
        }

        public static PillScriptComponent Find(string id)
        {
            if (!Guid.TryParse(id, out var instance)) return null;

            return Published().FirstOrDefault(c => c.InstanceGuid == instance);
        }

        /// <summary>
        /// What the page is sent: the sections to draw, the state of the last solve for the status
        /// bar, and the stylesheets the published scripts carry, joined in document order so the
        /// last one wins the same way a later rule does.
        /// </summary>
        public static object Payload()
        {
            var published = Published();
            var sections = new List<object>();
            var styles = new List<string>();
            var wantsLayers = false;

            foreach (var component in published)
            {
                var (registrar, problem, style) = component.Publication();

                if (!string.IsNullOrWhiteSpace(style)) styles.Add(style);
                if (registrar.Controls.Any(c => c.Kind == "layer")) wantsLayers = true;

                sections.Add(new
                {
                    component = component.InstanceGuid.ToString(),
                    title = Title(component),
                    mark = Mark(component),
                    collapsed = component.IsCollapsed,
                    problem,
                    widgets = registrar.Controls.Select(control => Describe(control, component.UiHeld))
                });
            }

            return new
            {
                type = "published",
                document = Instances.ActiveCanvas?.Document?.DisplayName ?? string.Empty,
                count = published.Count,
                status = Status(published),
                layers = wantsLayers ? Layers() : null,
                css = string.Join("\n\n", styles),
                sections
            };
        }

        /// <summary>
        /// The status bar: how long the last solve took and whether it went through. With several
        /// published scripts it is the slowest of them, since that is the one worth looking at.
        /// </summary>
        static object Status(IEnumerable<PillScriptComponent> published)
        {
            var solved = published.Where(c => c.LastSolveMs > 0).ToList();

            return new
            {
                milliseconds = solved.Count == 0 ? 0 : solved.Max(c => c.LastSolveMs),
                failed = solved.Any(c => c.LastSolveFailed)
            };
        }

        /// <summary>Full layer paths, as the Rhino document has them at this moment.</summary>
        static List<string> Layers()
        {
            var document = Rhino.RhinoDoc.ActiveDoc;
            if (document == null) return new List<string>();

            return document.Layers
                .Where(layer => !layer.IsDeleted)
                .Select(layer => layer.FullPath)
                .ToList();
        }

        /// <summary>
        /// The section heading, which is the component's nickname, and the four characters of its
        /// id beside it. The nickname of an untouched component is C#, so the mark is what tells
        /// two of them apart until somebody renames one.
        /// </summary>
        static string Title(PillScriptComponent component)
            => string.IsNullOrWhiteSpace(component.NickName) ? component.Name : component.NickName;

        static string Mark(PillScriptComponent component)
            => component.InstanceGuid.ToString("N").Substring(0, 4).ToUpperInvariant();

        /// <summary>
        /// What the panel draws. The value is whatever has been set, falling back to what the
        /// script registered it as, which is also what it shows before anybody has touched it.
        /// </summary>
        static object Describe(UiControl control, IReadOnlyDictionary<string, object> held)
        {
            held.TryGetValue(control.Name ?? string.Empty, out var set);
            var value = set ?? control.Value;

            return new
            {
                kind = control.Kind,
                name = control.Name,
                label = control.Label,
                note = control.Note,
                minimum = control.Minimum,
                maximum = control.Maximum,
                step = control.Step,
                options = control.Options,
                quiet = control.Quiet,
                value = Wire(control.Kind, value)
            };
        }

        /// <summary>A value in the shape the page reads it in.</summary>
        static object Wire(string kind, object value)
        {
            switch (kind)
            {
                case "vector":
                    var vector = value is Vector3d v
                        ? v
                        : UiRegistrar.ParseVector(value as string ?? string.Empty, Vector3d.Zero);

                    return new { x = vector.X, y = vector.Y, z = vector.Z };

                case "colour":
                    var colour = value is Color c
                        ? c
                        : UiRegistrar.ParseColour(value as string ?? string.Empty, Color.Gray);

                    return UiRegistrar.WriteColour(colour);

                default:
                    return value;
            }
        }
    }
}
