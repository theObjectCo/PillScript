using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Microsoft.Web.WebView2.Core;

namespace PillScript.Panel
{
    /// <summary>
    /// A spike, not a feature. What it has settled so far: a .gha can register a panel by
    /// borrowing Grasshopper's own PlugIn; Rhino 8 will not construct a Windows Forms control
    /// given to RegisterPanel, though it reports success; an Eto control works and docks.
    ///
    /// What it is settling now: whether WebView2 can live inside that Eto control, which is the
    /// last thing in the way of rendering the panel as a page.
    ///
    /// The GUID is the panel's identity to Rhino, so it is fixed rather than generated.
    /// </summary>
    [Guid("3C9F2A17-5B84-46D2-9E1B-7A0C4D85F332")]
    public class UiPanelHost : Eto.Forms.Panel
    {
        const string VirtualHost = "pillscript.panel";

        readonly Microsoft.Web.WebView2.Wpf.WebView2 _web = new Microsoft.Web.WebView2.Wpf.WebView2();

        public UiPanelHost()
        {
            var hosted = Wrap(_web);

            if (hosted == null)
            {
                Padding = new Padding(12);
                Content = new Label { Text = "WebView2 could not be hosted in an Eto panel." };
                return;
            }

            Content = hosted;
            Start();
        }

        public static Guid PanelId => typeof(UiPanelHost).GUID;

        /// <summary>
        /// Eto.Wpf carries the bridge from a WPF element to an Eto control, and Rhino has it
        /// loaded. It is reached by reflection here so the spike settles the question without
        /// first settling which package to reference it by.
        /// </summary>
        static Control Wrap(System.Windows.FrameworkElement element)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Eto.Wpf");

                var helpers = assembly?.GetType("Eto.Forms.WpfHelpers");
                var toEto = helpers?.GetMethod("ToEto", new[] { typeof(System.Windows.FrameworkElement) });

                return toEto?.Invoke(null, new object[] { element }) as Control;
            }
            catch (Exception exception)
            {
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
                return null;
            }
        }

        async void Start()
        {
            var folder = Path.Combine(
                Path.GetDirectoryName(typeof(UiPanelHost).Assembly.Location) ?? string.Empty,
                "web", "panel");

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PillScript", "webview");

            Directory.CreateDirectory(userData);

            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(environment);

                var page = _web.CoreWebView2;
                page.SetVirtualHostNameToFolderMapping(
                    VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);

                page.Settings.AreDefaultContextMenusEnabled = false;
                page.WebMessageReceived += OnMessage;

                page.Navigate("https://" + VirtualHost + "/index.html");

                Rhino.RhinoApp.WriteLine("PillScript panel: WebView2 is up inside the Eto panel.");
            }
            catch (Exception exception)
            {
                // A panel that throws while Rhino is docking it takes Rhino with it.
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
            }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var message = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;

                if (!root.TryGetProperty("type", out var type)) return;

                if (type.GetString() == "ready") Publish();
                else if (type.GetString() == "set") Heard(root);
            }
            catch (Exception exception)
            {
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
            }
        }

        /// <summary>Stands in for reading ui.json off every component that has been published.</summary>
        void Publish()
        {
            var document = Grasshopper.Instances.ActiveCanvas?.Document;
            var title = document?.DisplayName ?? "no definition open";

            var payload = @"{
              ""type"": ""published"",
              ""css"": "".section > h2 { color: #d18616; }"",
              ""sections"": [
                {
                  ""component"": ""spike"",
                  ""title"": ""SPIKE, IN " + title + @""",
                  ""widgets"": [
                    { ""kind"": ""slider"", ""name"": ""radius"", ""minimum"": 1, ""maximum"": 50, ""value"": 10 },
                    { ""kind"": ""number"", ""name"": ""count"", ""value"": 12 },
                    { ""kind"": ""choice"", ""name"": ""style"", ""options"": [""round"", ""square""], ""value"": ""round"" },
                    { ""kind"": ""toggle"", ""name"": ""cap"", ""value"": true },
                    { ""kind"": ""button"", ""name"": ""bake"", ""label"": ""Bake"" }
                  ]
                }
              ]
            }";

            _web.CoreWebView2.PostWebMessageAsJson(payload);
        }

        /// <summary>Where a real panel would write the value onto the component and expire it.</summary>
        static void Heard(System.Text.Json.JsonElement root)
        {
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var value = root.TryGetProperty("value", out var v) ? v.ToString() : "?";

            Rhino.RhinoApp.WriteLine("PillScript panel: " + name + " = " + value);
        }
    }
}
