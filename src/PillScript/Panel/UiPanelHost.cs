using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Grasshopper;
using PillScript.Components;
using Microsoft.Web.WebView2.Core;

namespace PillScript.Panel
{
    /// <summary>
    /// The Rhino panel a script publishes its controls into. It is an Eto panel because Rhino 8
    /// only constructs those: a Windows Forms control handed to RegisterPanel is accepted and then
    /// never built. The panel holds a single web view, so every control a script declares is an
    /// HTML element and a ui.css in the script project can restyle all of them.
    ///
    /// Rhino identifies the panel by this GUID, which is why it is a constant.
    /// </summary>
    [Guid("3C9F2A17-5B84-46D2-9E1B-7A0C4D85F332")]
    public class UiPanelHost : Eto.Forms.Panel
    {
        const string VirtualHost = "pillscript.panel";

        static readonly System.Text.Json.JsonSerializerOptions Json =
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

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
        /// Brings the panel up. It is registered from the moment Grasshopper loads, but until it
        /// is opened it sits collapsed in whichever tab group Rhino put it in, where publishing a
        /// component would appear to do nothing. Rhino leaves an already open panel in place.
        /// </summary>
        public static void Show()
        {
            try
            {
                Rhino.UI.Panels.OpenPanel(PanelId, true);
            }
            catch (Exception exception)
            {
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
            }
        }

        /// <summary>
        /// Eto.Wpf holds the bridge from a WPF element to an Eto control, and Rhino has it loaded
        /// already. Reflection is deliberate here: the plugin has to bind to whichever Eto the
        /// running Rhino ships, and a compile-time reference would pin it to another version.
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

                PillScriptComponent.Published += Refresh;
                Instances.ActiveCanvas.DocumentChanged += OnDocumentChanged;
            }
            catch (Exception exception)
            {
                // An exception escaping while Rhino docks the panel brings Rhino down.
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
            }
        }

        void OnDocumentChanged(object sender, Grasshopper.GUI.Canvas.GH_CanvasDocumentChangedEventArgs e)
            => Refresh();

        /// <summary>
        /// Redraws the panel. Calls come from a component's menu or from a document being opened,
        /// both on the UI thread, but the guard is cheap and an exception escaping here would bring
        /// Rhino down.
        /// </summary>
        void Refresh()
        {
            if (_web.CoreWebView2 == null) return;

            try { Publish(); }
            catch (Exception exception) { Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message); }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var message = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;

                if (!root.TryGetProperty("type", out var type)) return;

                switch (type.GetString())
                {
                    case "ready": Publish(); break;
                    case "set": Heard(root); break;
                    case "collapse": Collapse(root); break;
                    case "colour": Pick(root); break;
                    case "order": Sort(root); break;
                }
            }
            catch (Exception exception)
            {
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
            }
        }

        /// <summary>Sends the controls the published components declare and the values they hold.</summary>
        void Publish()
        {
            if (_web.CoreWebView2 == null) return;

            try
            {
                _web.CoreWebView2.PostWebMessageAsJson(
                    System.Text.Json.JsonSerializer.Serialize(UiPublication.Payload(), Json));
            }
            catch (Exception exception)
            {
                Rhino.RhinoApp.WriteLine("PillScript panel: " + exception.Message);
            }
        }

        /// <summary>
        /// Puts a value onto the component it belongs to. The component decides when that turns
        /// into a solve, which is what keeps a slider drag down to one.
        /// </summary>
        static void Heard(System.Text.Json.JsonElement root)
        {
            var component = UiPublication.Find(
                root.TryGetProperty("component", out var id) ? id.GetString() : null);

            if (component == null) return;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name == null) return;

            component.SetUiValue(name, Plain(root.TryGetProperty("value", out var v) ? v : default));
        }

        /// <summary>
        /// Opens Rhino's own colour picker. The panel docks beside Layers and picking a colour
        /// there works the same way.
        /// </summary>
        static void Pick(System.Text.Json.JsonElement root)
        {
            var component = UiPublication.Find(
                root.TryGetProperty("component", out var id) ? id.GetString() : null);

            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (component == null || name == null) return;

            var colour = UiRegistrar.ParseColour(
                root.TryGetProperty("value", out var v) ? v.GetString() : null,
                System.Drawing.Color.Gray);

            if (Rhino.UI.Dialogs.ShowColorDialog(ref colour))
                component.SetUiValue(name, UiRegistrar.WriteColour(colour));
        }

        /// <summary>
        /// Takes the order the sections were dragged into. The page sends the whole list, not just
        /// the section that moved, which keeps the two sides in step over a run of drags.
        /// </summary>
        static void Sort(System.Text.Json.JsonElement root)
        {
            if (!root.TryGetProperty("components", out var list) ||
                list.ValueKind != System.Text.Json.JsonValueKind.Array) return;

            var index = 0;
            foreach (var id in list.EnumerateArray())
                UiPublication.Find(id.GetString())?.SetOrder(index++);
        }

        /// <summary>Rolls a section up or down and records the state in the document.</summary>
        static void Collapse(System.Text.Json.JsonElement root)
        {
            var component = UiPublication.Find(
                root.TryGetProperty("component", out var id) ? id.GetString() : null);

            if (component == null) return;

            component.SetCollapsed(
                root.TryGetProperty("value", out var value) &&
                value.ValueKind == System.Text.Json.JsonValueKind.True);
        }

        /// <summary>
        /// A JSON value as the simplest CLR type that carries it. A vector arrives as three
        /// numbers and is stored as text, which keeps the document format at three kinds: flag,
        /// number and text.
        /// </summary>
        static object Plain(System.Text.Json.JsonElement value)
        {
            switch (value.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Number: return value.GetDouble();
                case System.Text.Json.JsonValueKind.True: return true;
                case System.Text.Json.JsonValueKind.False: return false;
                case System.Text.Json.JsonValueKind.String: return value.GetString();

                case System.Text.Json.JsonValueKind.Object:
                    return value.TryGetProperty("x", out var x) &&
                           value.TryGetProperty("y", out var y) &&
                           value.TryGetProperty("z", out var z)
                        ? UiRegistrar.WriteVector(
                            new Rhino.Geometry.Vector3d(x.GetDouble(), y.GetDouble(), z.GetDouble()))
                        : null;

                default: return null;
            }
        }
    }
}
