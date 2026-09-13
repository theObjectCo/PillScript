using System;
using Grasshopper.Kernel;
using PillScript.Bridge;

namespace PillScript
{
    /// <summary>
    /// Starts the bridge as Grasshopper loads, so a tool outside Rhino can reach the components
    /// without an editor being opened first. Nothing listens unless PILLSCRIPT_BRIDGE is set to
    /// on. The editor needs no port, and a port that writes and runs code should not come with a
    /// plain install of a script component.
    ///
    /// The Rhino panel is registered here for the opposite reason: Rhino has to know about a panel
    /// before anything asks for it, so that a document reopened with a published component finds
    /// the tab already in place.
    ///
    /// The working folders are swept here too, in the background. This runs once and early, and it
    /// is the only point in the plugin that belongs to no document.
    /// </summary>
    public class PillScriptPriority : GH_AssemblyPriority
    {
        // Grasshopper is itself a Rhino plugin and a panel has to be registered against one.
        // Registering against the host's id means this .gha needs no .rhp beside it.
        static readonly Guid GrasshopperPlugin = new Guid("b45a29b1-4343-4035-989e-044e8580d9cf");

        public override GH_LoadingInstruction PriorityLoad()
        {
            ScriptBridgeServer.Start();
            RegisterPanel();
            Scripting.ProjectCache.SweepLater();

            return GH_LoadingInstruction.Proceed;
        }

        /// <summary>
        /// A failed panel registration does not fail the load. A component that cannot publish
        /// its controls still compiles and runs.
        /// </summary>
        static void RegisterPanel()
        {
            try
            {
                var host = Rhino.PlugIns.PlugIn.Find(GrasshopperPlugin);
                if (host == null) return;

                // Read from the assembly, not from a file. An icon loaded from disk registers in
                // a debug build and silently fails to in a release one.
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
