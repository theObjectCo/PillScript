using System;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;

namespace PillScript.Panel
{
    /// <summary>
    /// A spike, not a feature. It answers the one question about a docked panel that reading
    /// cannot: what Rhino 8 will accept as a panel registered from a .gha, and whether it is ever
    /// constructed. A Windows Forms control was accepted by RegisterPanel and then never built,
    /// so this is the same test with Eto, which is what Rhino 8 panels are made of.
    ///
    /// The GUID is the panel's identity to Rhino, so it is fixed rather than generated.
    /// </summary>
    [Guid("3C9F2A17-5B84-46D2-9E1B-7A0C4D85F332")]
    public class UiPanelHost : Eto.Forms.Panel
    {
        public UiPanelHost()
        {
            var heard = new Label { Text = "nothing yet", TextColor = Colors.Gray };

            var ask = new Button { Text = "Ask what is open" };
            ask.Click += (_, __) =>
            {
                var document = Grasshopper.Instances.ActiveCanvas?.Document;
                heard.Text = document?.DisplayName ?? "no definition open";
            };

            Padding = new Padding(12);
            Content = new StackLayout
            {
                Spacing = 8,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new Label { Text = "PillScript", Font = SystemFonts.Bold() },
                    new Label
                    {
                        Text = "A spike. It proves the panel docks and is constructed.",
                        TextColor = Colors.Gray
                    },
                    ask,
                    heard
                }
            };

            Rhino.RhinoApp.WriteLine("PillScript panel: constructed.");
        }

        public static Guid PanelId => typeof(UiPanelHost).GUID;
    }
}
