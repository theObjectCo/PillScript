using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using PillScript.Components;
using PillScript.Scripting;

namespace PillScript.Panel
{
    /// <summary>
    /// Gathers what the panel should show: every published component on the canvas, its controls
    /// as ui.json declares them, and the values they currently hold. The panel draws it; nothing
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
        /// What the page is sent: the sections to draw, and the stylesheets the published scripts
        /// carry, joined in document order so the last one wins the same way a later rule does.
        /// </summary>
        public static object Payload()
        {
            var sections = new List<object>();
            var styles = new List<string>();
            var problems = new List<string>();

            foreach (var component in Published())
            {
                var (registrar, problem, style) = component.Publication();

                if (problem != null) problems.Add(Title(component) + ": " + problem);
                if (!string.IsNullOrWhiteSpace(style)) styles.Add(style);
                if (registrar.Controls.Count == 0) continue;

                sections.Add(new
                {
                    component = component.InstanceGuid.ToString(),
                    title = Title(component),
                    widgets = registrar.Controls.Select(control => Describe(control, component.UiHeld))
                });
            }

            return new
            {
                type = "published",
                css = string.Join("\n\n", styles),
                problems,
                sections
            };
        }

        /// <summary>
        /// The section heading. A component keeps its nickname unless somebody renames it, and a
        /// panel of three sections all called "C#" would be no use, so the name comes first and
        /// falls back to something that at least differs.
        /// </summary>
        static string Title(PillScriptComponent component)
        {
            var name = component.NickName;

            if (!string.IsNullOrWhiteSpace(name) && name != "C#") return name;

            return "Script " + component.InstanceGuid.ToString().Substring(0, 4);
        }

        /// <summary>
        /// What the panel draws. The value is whatever has been set, falling back to what the
        /// script registered it as, which is also what it shows before anybody has touched it.
        /// </summary>
        static object Describe(UiControl control, IReadOnlyDictionary<string, object> held)
        {
            held.TryGetValue(control.Name ?? string.Empty, out var value);

            return new
            {
                kind = control.Kind,
                name = control.Name,
                label = control.Label,
                minimum = control.Minimum,
                maximum = control.Maximum,
                step = control.Step,
                options = control.Options,
                value = value ?? control.Value
            };
        }
    }
}
