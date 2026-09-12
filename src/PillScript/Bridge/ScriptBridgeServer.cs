using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rhino;

namespace PillScript.Bridge
{
    /// <summary>
    /// A small HTTP endpoint on the loopback address that lets a tool outside Rhino read and write
    /// the sources of any script component on the canvas, compile it and read what it produced.
    /// It speaks a plain request and answer shape; the MCP wrapper that agents talk to lives
    /// outside and forwards to this. What the tools actually do is in BridgeTools.
    /// </summary>
    internal static class ScriptBridgeServer
    {
        const int DefaultPort = 57321;

        /// <summary>How long a single call may take, whether waiting for Rhino or for a compile.</summary>
        internal static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(2);

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static HttpListener _listener;

        public static int Port { get; private set; }

        /// <summary>
        /// True when PILLSCRIPT_BRIDGE asks for the bridge. It is off unless asked for, because
        /// installing the editor should not also open a port that writes and runs code; only
        /// somebody driving the component from outside Rhino wants that, and they know they do.
        /// </summary>
        public static bool Wanted
        {
            get
            {
                var setting = Environment.GetEnvironmentVariable("PILLSCRIPT_BRIDGE");

                return string.Equals(setting, "on", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(setting, "1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(setting, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Starts listening, if asked to. Failing to start is not worth interrupting anybody over:
        /// the editor works without it.
        /// </summary>
        public static void Start()
        {
            if (_listener != null || !Wanted) return;

            Port = ReadPort();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                _listener.Start();
            }
            catch (Exception exception)
            {
                RhinoApp.WriteLine("PillScript: the bridge could not listen on port " + Port
                    + " (" + exception.Message + ").");

                _listener = null;
                return;
            }

            Task.Run(Listen);
            RhinoApp.WriteLine("PillScript: bridge listening on http://127.0.0.1:" + Port);
        }

        static int ReadPort()
        {
            var configured = Environment.GetEnvironmentVariable("PILLSCRIPT_BRIDGE_PORT");
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
                var refusal = Refuse(context.Request);

                object result;

                if (refusal != null)
                {
                    context.Response.StatusCode = 403;
                    result = new { error = refusal };
                }
                else
                {
                    string body;

                    using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();

                    try
                    {
                        result = Dispatch(body);
                    }
                    catch (Exception exception)
                    {
                        result = new { error = exception.Message };
                    }
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

        /// <summary>
        /// Says why a request is not being answered, or null to answer it. This is what keeps a
        /// page in a browser from driving Rhino: the port is reachable from one, and writing a
        /// script and compiling it are side effects, so a page never needs to read the answer to
        /// get what it wants.
        /// </summary>
        static string Refuse(HttpListenerRequest request)
        {
            // A page can only set this content type cross-origin by asking permission first, and
            // that question is one this listener never answers. A local caller just sets it.
            var type = request.ContentType ?? string.Empty;

            if (type.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) < 0)
                return "This endpoint takes application/json.";

            // Anything a browser sends from a page carries this, whatever the page does.
            if (!string.IsNullOrEmpty(request.Headers["Origin"]))
                return "This endpoint does not answer requests from a web page.";

            // With the name rebound to the loopback address a page counts as same origin, and the
            // two checks above stop applying. What it cannot do is arrive naming the address.
            var host = request.Headers["Host"] ?? string.Empty;

            if (!host.StartsWith("127.0.0.1", StringComparison.Ordinal)
                && !host.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return "This endpoint answers on the loopback address only.";

            return null;
        }

        static object Dispatch(string body)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;

            var tool = BridgeLookup.Text(root, "tool");
            var arguments = root.TryGetProperty("arguments", out var value)
                            && value.ValueKind == JsonValueKind.Object
                ? value
                : default;

            // Everything below touches the Grasshopper document, which belongs to the UI thread.
            return OnUi(() => BridgeTools.Run(tool, arguments));
        }

        /// <summary>Runs the work where the Grasshopper document can be touched, and waits for it.</summary>
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
