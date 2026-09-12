using Grasshopper.Kernel;
using PillScript.Bridge;

namespace PillScript
{
    /// <summary>
    /// Offers the bridge as Grasshopper loads, so a tool outside Rhino can reach the components
    /// without anybody opening an editor first. Nothing listens unless PILLSCRIPT_BRIDGE is set
    /// to on: the editor needs no port, and one that writes and runs code is not something to
    /// hand to somebody who only installed a script component.
    /// </summary>
    public class PillScriptPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            ScriptBridgeServer.Start();
            return GH_LoadingInstruction.Proceed;
        }
    }
}
