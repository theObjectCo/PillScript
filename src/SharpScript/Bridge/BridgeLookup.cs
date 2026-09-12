using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Grasshopper;
using Grasshopper.Kernel;
using SharpScript.Components;

namespace SharpScript.Bridge
{
    /// <summary>
    /// Finding the component a call is about, and saying what a component looks like from outside
    /// Rhino. Shared by every tool, so it is kept where all of them can see it.
    /// </summary>
    internal static class BridgeLookup
    {
        /// <summary>Every script component on every open canvas, in document order.</summary>
        public static IEnumerable<SharpScriptComponent> Components()
        {
            var server = Instances.DocumentServer;
            if (server == null) yield break;

            foreach (GH_Document document in server)
            {
                foreach (var obj in document.Objects)
                {
                    if (obj is SharpScriptComponent component) yield return component;
                }
            }
        }

        /// <summary>
        /// Finds the component the call is about. With one on the canvas the id may be left out,
        /// which is the common case and saves a round trip. A prefix of the id is enough.
        /// </summary>
        public static SharpScriptComponent Component(JsonElement arguments)
        {
            var id = Text(arguments, "component");
            var all = Components().ToList();

            if (all.Count == 0)
                throw new InvalidOperationException("There is no C# Script component on any open canvas.");

            if (string.IsNullOrWhiteSpace(id))
            {
                if (all.Count == 1) return all[0];

                var names = string.Join(", ", all.Select(c => c.InstanceGuid + " (" + c.NickName + ")"));
                throw new InvalidOperationException(
                    "There are " + all.Count + " script components; name one with 'component': " + names);
            }

            var found = all.FirstOrDefault(c =>
                c.InstanceGuid.ToString().StartsWith(id, StringComparison.OrdinalIgnoreCase));

            return found ?? throw new InvalidOperationException("No script component matches " + id + ".");
        }

        public static object Describe(SharpScriptComponent component)
            => new
            {
                id = component.InstanceGuid.ToString(),
                nickname = component.NickName,
                document = component.OnPingDocument()?.DisplayName ?? "unsaved",
                stale = component.IsStale,
                editorOpen = component.IsEditorOpen,
                files = component.Project.Files.Select(f => f.Name).ToList(),
                parameters = Parameters(component)
            };

        public static object Parameters(SharpScriptComponent component)
            => new
            {
                inputs = component.Params.Input
                    .Select(p => new { name = p.Name, type = p.TypeName, access = p.Access.ToString() })
                    .ToList(),
                outputs = component.Params.Output
                    .Select(p => new { name = p.Name, type = p.TypeName, access = p.Access.ToString() })
                    .ToList()
            };

        /// <summary>The component's own print and error pane, which is where a script reports itself.</summary>
        public static List<string> Output(SharpScriptComponent component)
        {
            var lines = new List<string>();
            var parameter = component.Params.Output.FirstOrDefault();

            if (parameter == null) return lines;

            foreach (var item in parameter.VolatileData.AllData(true))
                lines.Add(item?.ToString() ?? string.Empty);

            return lines;
        }

        /// <summary>Reads a string argument, treating anything missing or of another kind as empty.</summary>
        public static string Text(JsonElement root, string name)
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : string.Empty;
    }
}
