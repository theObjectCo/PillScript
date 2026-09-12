using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino;
using SharpScript.Components;
using SharpScript.Scripting;

namespace SharpScript.Bridge
{
    /// <summary>
    /// A small HTTP endpoint on the loopback address that lets a tool outside Rhino read and write
    /// the sources of any script component on the canvas, compile it and read what it produced.
    /// It speaks a plain request and answer shape; the MCP wrapper that agents talk to lives
    /// outside and forwards to this.
    /// </summary>
    internal static class ScriptBridgeServer
    {
        const int DefaultPort = 57321;
        static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(2);

        static HttpListener _listener;

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static int Port { get; private set; }

        /// <summary>
        /// Starts listening unless SHARPSCRIPT_BRIDGE is set to off. Failing to start is not worth
        /// interrupting anybody over: the editor works without it.
        /// </summary>
        public static void Start()
        {
            if (_listener != null) return;

            var setting = Environment.GetEnvironmentVariable("SHARPSCRIPT_BRIDGE");
            if (string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase)) return;

            Port = ReadPort();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                _listener.Start();
            }
            catch (Exception exception)
            {
                RhinoApp.WriteLine("SharpScript: the bridge could not listen on port " + Port
                    + " (" + exception.Message + ").");

                _listener = null;
                return;
            }

            Task.Run(Listen);
            RhinoApp.WriteLine("SharpScript: bridge listening on http://127.0.0.1:" + Port);
        }

        static int ReadPort()
        {
            var configured = Environment.GetEnvironmentVariable("SHARPSCRIPT_BRIDGE_PORT");
            return int.TryParse(configured, out var port) && port > 0 && port < 65536 ? port : DefaultPort;
        }

        public static void Stop()
        {
            try { _listener?.Stop(); }
            catch (Exception) { }

            _listener = null;
        }

        static async Task Listen()
        {
            while (_listener != null && _listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => Answer(context));
            }
        }

        static void Answer(HttpListenerContext context)
        {
            // This runs on a pool thread, where an escaping exception ends the process, so the
            // whole exchange including writing the answer is wrapped.
            try
            {
                string body;

                using (var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();

                object result;

                try
                {
                    result = Dispatch(body);
                }
                catch (Exception exception)
                {
                    result = new { error = exception.Message };
                }

                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, Json));

                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch (Exception)
            {
                try { context.Response.Abort(); }
                catch (Exception) { }
            }
        }

        static object Dispatch(string body)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;

            var tool = Text(root, "tool");
            var arguments = root.TryGetProperty("arguments", out var value) && value.ValueKind == JsonValueKind.Object
                ? value
                : default;

            // Everything below touches the Grasshopper document, which belongs to the UI thread.
            return OnUi(() => Run(tool, arguments));
        }

        static object Run(string tool, JsonElement arguments)
        {
            switch (tool)
            {
                case "list_components":
                    return new { components = Components().Select(Describe).ToList() };

                case "list_files":
                    return new { files = Component(arguments).Project.Files.Select(f => f.Name).ToList() };

                case "read_file":
                {
                    var component = Component(arguments);
                    var name = Text(arguments, "file");
                    var file = component.Project.Find(name)
                        ?? throw new InvalidOperationException(name + " is not part of this script.");

                    return new { file = file.Name, content = file.Content };
                }

                case "write_file":
                {
                    var component = Component(arguments);
                    var name = Text(arguments, "file");

                    if (component.Project.Find(name) == null
                        && !component.Project.AddFile(name, out var error))
                        throw new InvalidOperationException(error);

                    component.Project.SetContent(
                        component.Project.Find(name).Name, Text(arguments, "content"));

                    component.AfterProjectChanged();
                    return new { ok = true, file = name };
                }

                case "delete_file":
                {
                    var component = Component(arguments);
                    if (!component.Project.RemoveFile(Text(arguments, "file"), out var error))
                        throw new InvalidOperationException(error);

                    component.AfterProjectChanged();
                    return new { ok = true };
                }

                case "rename_file":
                {
                    var component = Component(arguments);
                    if (!component.Project.RenameFile(
                            Text(arguments, "from"), Text(arguments, "to"), out var error))
                        throw new InvalidOperationException(error);

                    component.AfterProjectChanged();
                    return new { ok = true };
                }

                case "list_references":
                {
                    var (local, packages) = ProjectReferences.Read(Component(arguments).Project);

                    return new
                    {
                        local = local.Select(r => new { name = r.Name, path = r.Path, missing = r.Missing }).ToList(),
                        packages = packages.Select(r => new { id = r.Id, version = r.Version }).ToList()
                    };
                }

                case "add_package":
                    return Changed(arguments, component => ProjectReferences.AddPackage(
                        component.Project, Text(arguments, "id"), Text(arguments, "version")));

                case "remove_package":
                    return Changed(arguments, component => ProjectReferences.RemovePackage(
                        component.Project, Text(arguments, "id")));

                case "add_reference":
                    return Changed(arguments, component => ProjectReferences.AddLocal(
                        component.Project, Text(arguments, "path")));

                case "remove_reference":
                    return Changed(arguments, component => ProjectReferences.RemoveLocal(
                        component.Project, Text(arguments, "name")));

                case "compile":
                    return Compile(Component(arguments));

                case "solve":
                {
                    var component = Component(arguments);
                    component.Run();

                    return new
                    {
                        ok = true,
                        parameters = Parameters(component),
                        output = Output(component)
                    };
                }

                case "open_editor":
                    Component(arguments).OpenEditor();
                    return new { ok = true };

                default:
                    throw new InvalidOperationException("Unknown tool: " + tool);
            }
        }

        /// <summary>
        /// Applies a change to the project file and lets the component and any open editor catch
        /// up with it. The change answers null when it went through, a reason when it did not.
        /// </summary>
        static object Changed(JsonElement arguments, Func<SharpScriptComponent, string> change)
        {
            var component = Component(arguments);
            var error = change(component);

            if (error != null) throw new InvalidOperationException(error);

            component.AfterProjectChanged();
            return new { ok = true };
        }

        static object Compile(SharpScriptComponent component)
        {
            CompileResult result = null;

            // The compile finishes back on the UI thread, which this call is holding, so the wait
            // is a nested message loop rather than a block that would never be released.
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = CallTimeout
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
                parameters = Parameters(component)
            };
        }

        static object Describe(SharpScriptComponent component)
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

        static object Parameters(SharpScriptComponent component)
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
        static List<string> Output(SharpScriptComponent component)
        {
            var lines = new List<string>();
            var parameter = component.Params.Output.FirstOrDefault();

            if (parameter == null) return lines;

            foreach (var item in parameter.VolatileData.AllData(true))
                lines.Add(item?.ToString() ?? string.Empty);

            return lines;
        }

        static IEnumerable<SharpScriptComponent> Components()
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
        /// which is the common case and saves a round trip.
        /// </summary>
        static SharpScriptComponent Component(JsonElement arguments)
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

        static string Text(JsonElement root, string name)
            => root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : string.Empty;

        static object OnUi(Func<object> work)
        {
            object result = null;
            Exception failure = null;

            var done = new ManualResetEventSlim(false);

            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try { result = work(); }
                catch (Exception exception) { failure = exception; }
                finally { done.Set(); }
            }));

            if (!done.Wait(CallTimeout))
                throw new TimeoutException("Rhino did not answer in time.");

            if (failure != null) throw failure;

            return result;
        }
    }
}
