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
    /// Collects what the panel shows: every published component on the active canvas, the
    /// controls its RegisterUi declares, and the values they hold. Drawing happens in the web page.
    /// </summary>
    internal static class UiPublication
    {
        /// <summary>
        /// Published components on the active canvas, in the order the panel was left in. A
        /// section that has never been dragged has no stored position and follows the ones that
        /// have, in document order.
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

            return found
                .Select((component, index) => new { component, index })
                .OrderBy(pair => pair.component.UiOrder)
                .ThenBy(pair => pair.index)
                .Select(pair => pair.component)
                .ToList();
        }

        public static PillScriptComponent Find(string id)
        {
            if (!Guid.TryParse(id, out var instance)) return null;

            return Published().FirstOrDefault(c => c.InstanceGuid == instance);
        }

        /// <summary>
        /// What the page is sent: the sections to draw, the state of the last solve for the status
        /// bar, and the stylesheets the published scripts carry, concatenated in document order so
        /// that a later rule overrides an earlier one, as in any stylesheet.
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

                var controls = registrar.Controls;

                // A script whose first control is a caption has effectively named itself, and a
                // heading reading C# above a caption reading Outline repeats it. Renaming the
                // component on the canvas restores the heading and leaves the caption in place.
                var lead = !component.IsNamed && controls.Count > 0 && controls[0].Kind == "caption"
                    ? controls[0]
                    : null;

                sections.Add(new
                {
                    component = component.InstanceGuid.ToString(),
                    title = lead != null ? lead.Label : Title(component),
                    mark = Mark(component),
                    icon = component.IconSvg,
                    collapsed = component.IsCollapsed,
                    problem,
                    widgets = controls.Skip(lead == null ? 0 : 1)
                        .Select(control => Describe(control, component.UiHeld))
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
        /// The status bar: how long the last solve took and whether it completed. With several
        /// published scripts the slowest of them is reported.
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

        /// <summary>Full layer paths, read from the Rhino document at this moment.</summary>
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
        /// The section heading: the component's nickname, with four characters of its id beside
        /// it. An untouched component is nicknamed C#, so the mark is what tells two of them apart
        /// until one is renamed.
        /// </summary>
        static string Title(PillScriptComponent component)
            => string.IsNullOrWhiteSpace(component.NickName) ? component.Name : component.NickName;

        static string Mark(PillScriptComponent component)
            => component.InstanceGuid.ToString("N").Substring(0, 4).ToUpperInvariant();

        /// <summary>
        /// What the panel draws. The value is the one the panel has set, falling back to the
        /// default the script registered, which is what an untouched control shows.
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
                icon = control.Icon,
                value = Wire(control.Kind, value)
            };
        }

        /// <summary>A value converted to the shape the page reads.</summary>
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
