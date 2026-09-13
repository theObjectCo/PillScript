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
    /// A small HTTP endpoint on the loopback address. A tool outside Rhino can use it to read
    /// and write the sources of any script component on the canvas, compile it and read the
    /// output. The protocol is a plain request and answer shape. The MCP wrapper that agents talk
    /// to runs as a separate process and forwards to this. The tools themselves are in
    /// BridgeTools.
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
        /// True when PILLSCRIPT_BRIDGE is set. The bridge is off by default: installing the
        /// editor should not also open a port that writes and runs code. Only a caller driving the
        /// component from outside Rhino needs it, and setting the variable is an explicit request.
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
        /// Starts listening, if the variable is set. A failure to start is swallowed, since the
        /// editor works without the bridge.
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
            // whole exchange including writing the answer is inside the try.
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
        /// The reason a request is refused, or null to answer it. These checks are what keep a
        /// web page from driving Rhino: the port is reachable from a browser, and writing a script
        /// and compiling it are side effects, so an attacker would not need to read the response.
        /// </summary>
        static string Refuse(HttpListenerRequest request)
        {
            // A page can only set this content type cross-origin after a preflight request,
            // which this listener never answers. A local caller sets it directly.
            var type = request.ContentType ?? string.Empty;

            if (type.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) < 0)
                return "This endpoint takes application/json.";

            // A browser attaches this header to every request a page makes.
            if (!string.IsNullOrEmpty(request.Headers["Origin"]))
                return "This endpoint does not answer requests from a web page.";

            // A hostname rebound to the loopback address makes a page same origin, which defeats
            // the two checks above. Only a Host header naming the address itself is accepted.
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

        /// <summary>Runs the work on the thread that owns the Grasshopper document and waits.</summary>
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
