using System;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using PillScript.Components;
using PillScript.Scripting;

namespace PillScript.Bridge
{
    /// <summary>
    /// The tools the bridge offers and what each one does to a component. Everything here runs on
    /// the UI thread, which is the only thread that may touch the Grasshopper document.
    /// </summary>
    internal static class BridgeTools
    {
        public static object Run(string tool, JsonElement arguments)
        {
            switch (tool)
            {
                case "list_components":
                    return new { components = BridgeLookup.Components().Select(BridgeLookup.Describe).ToList() };

                case "list_files":
                    return new { files = Find(arguments).Project.Files.Select(f => f.Name).ToList() };

                case "read_file":
                    return ReadFile(arguments);

                case "write_file":
                    return WriteFile(arguments);

                case "delete_file":
                    return Changed(arguments, component => component.Project.RemoveFile(
                        BridgeLookup.Text(arguments, "file"), out var error) ? null : error);

                case "rename_file":
                    return Changed(arguments, component => component.Project.RenameFile(
                        BridgeLookup.Text(arguments, "from"),
                        BridgeLookup.Text(arguments, "to"), out var error) ? null : error);

                case "list_references":
                    return ListReferences(arguments);

                case "add_package":
                    return Changed(arguments, component => ProjectReferences.AddPackage(
                        component.Project,
                        BridgeLookup.Text(arguments, "id"),
                        BridgeLookup.Text(arguments, "version")));

                case "remove_package":
                    return Changed(arguments, component => ProjectReferences.RemovePackage(
                        component.Project, BridgeLookup.Text(arguments, "id")));

                case "add_reference":
                    return Changed(arguments, component => ProjectReferences.AddLocal(
                        component.Project, BridgeLookup.Text(arguments, "path")));

                case "remove_reference":
                    return Changed(arguments, component => ProjectReferences.RemoveLocal(
                        component.Project, BridgeLookup.Text(arguments, "name")));

                case "compile":
                    return Compile(Find(arguments));

                case "solve":
                    return Solve(Find(arguments));

                case "open_editor":
                    Find(arguments).OpenEditor();
                    return new { ok = true };

                default:
                    throw new InvalidOperationException("Unknown tool: " + tool);
            }
        }

        static PillScriptComponent Find(JsonElement arguments) => BridgeLookup.Component(arguments);

        // ----- files -----------------------------------------------------------------------------

        static object ReadFile(JsonElement arguments)
        {
            var component = Find(arguments);
            var name = BridgeLookup.Text(arguments, "file");

            var file = component.Project.Find(name)
                ?? throw new InvalidOperationException(name + " is not part of this script.");

            return new { file = file.Name, content = file.Content };
        }

        static object WriteFile(JsonElement arguments)
        {
            var component = Find(arguments);
            var asked = BridgeLookup.Text(arguments, "file");

            // A new file can end up with a different name than the one asked for, so the answer
            // names the file that was actually written.
            var file = component.Project.Find(asked);

            if (file == null)
            {
                file = component.Project.AddFile(asked, out var error);
                if (file == null) throw new InvalidOperationException(error);
            }

            component.Project.SetContent(file.Name, BridgeLookup.Text(arguments, "content"));

            component.NoteSaved(file.Name);
            component.AfterProjectChanged();
            return new { ok = true, file = file.Name };
        }

        // ----- references ------------------------------------------------------------------------

        static object ListReferences(JsonElement arguments)
        {
            var (local, packages) = ProjectReferences.Read(Find(arguments).Project);

            return new
            {
                local = local.Select(r => new { name = r.Name, path = r.Path, missing = r.Missing }).ToList(),
                packages = packages.Select(r => new { id = r.Id, version = r.Version }).ToList()
            };
        }

        /// <summary>
        /// Applies a change to the project and brings the component and any open editor up to
        /// date. The change returns null when it succeeded and a reason when it did not.
        /// </summary>
        static object Changed(JsonElement arguments, Func<PillScriptComponent, string> change)
        {
            var component = Find(arguments);
            var error = change(component);

            if (error != null) throw new InvalidOperationException(error);

            component.AfterProjectChanged();
            return new { ok = true };
        }

        // ----- building and running ----------------------------------------------------------------

        static object Compile(PillScriptComponent component)
        {
            CompileResult result = null;

            // The compile finishes back on the UI thread, which this call is holding, so the wait
            // runs a nested message loop. A plain block here would never be released.
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = ScriptBridgeServer.CallTimeout
            };

            timeout.Tick += (_, __) => frame.Continue = false;
            timeout.Start();

            component.Compile(answer =>
            {
                result = answer;
                frame.Continue = false;
            });

            Dispatcher.PushFrame(frame);
            timeout.Stop();

            if (result == null) return new { ok = false, error = "The compile did not finish in time." };

            return new
            {
                ok = result.Success,
                elapsedMs = (int)result.Elapsed.TotalMilliseconds,
                diagnostics = result.Diagnostics.Select(d => new
                {
                    file = d.File,
                    line = d.Line,
                    column = d.Column,
                    severity = d.Severity,
                    id = d.Id,
                    message = d.Message
                }).ToList(),
                parameters = BridgeLookup.Parameters(component)
            };
        }

        static object Solve(PillScriptComponent component)
        {
            component.Run();

            return new
            {
                ok = true,
                parameters = BridgeLookup.Parameters(component),
                output = BridgeLookup.Output(component)
            };
        }
    }
}
