using Grasshopper.Kernel;
using PillScript.Bridge;

namespace PillScript
{
    /// <summary>
    /// Starts the bridge as Grasshopper loads, so a tool outside Rhino can reach the components
    /// without anybody opening an editor first. Set PILLSCRIPT_BRIDGE=off to keep the port shut.
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
