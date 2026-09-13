using System;
using Grasshopper.Kernel;
using PillScript.Bridge;

namespace PillScript
{
    /// <summary>
    /// Offers the bridge as Grasshopper loads, so a tool outside Rhino can reach the components
    /// without anybody opening an editor first. Nothing listens unless PILLSCRIPT_BRIDGE is set
    /// to on: the editor needs no port, and one that writes and runs code is not something to
    /// hand to somebody who only installed a script component.
    ///
    /// The Rhino panel is registered here too, and for the opposite reason: Rhino wants to know
    /// about a panel before anybody asks for one, so that a document reopened with a published
    /// component finds the tab already there.
    /// </summary>
    public class PillScriptPriority : GH_AssemblyPriority
    {
        // Grasshopper is itself a Rhino plugin, and a panel has to be registered against one.
        // Borrowing the host's own means this .gha needs no .rhp beside it.
        static readonly Guid GrasshopperPlugin = new Guid("b45a29b1-4343-4035-989e-044e8580d9cf");

        public override GH_LoadingInstruction PriorityLoad()
        {
            ScriptBridgeServer.Start();
            RegisterPanel();

            return GH_LoadingInstruction.Proceed;
        }

        /// <summary>
        /// Failing to register the panel is not a reason to fail to load: a component that cannot
        /// publish its controls is still a component that compiles and runs.
        /// </summary>
        static void RegisterPanel()
        {
            try
            {
                var host = Rhino.PlugIns.PlugIn.Find(GrasshopperPlugin);
                if (host == null) return;

                // From the assembly rather than from a file: an icon read off disk registers in a
                // debug build and quietly does not in a release one.
                var icon = Rhino.UI.DrawingUtilities.IconFromResource(
                    "PillScript.panel.ico", typeof(PillScriptPriority).Assembly);

                Rhino.UI.Panels.RegisterPanel(host, typeof(Panel.UiPanelHost), "PillScript", icon);
            }
            catch (Exception exception)
            {
                Rhino.RhinoApp.WriteLine("PillScript: the panel could not be registered ("
                    + exception.Message + ").");
            }
        }
    }
}
