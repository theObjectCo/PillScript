using Grasshopper.Kernel;
using SharpScript.Bridge;

namespace SharpScript
{
    /// <summary>
    /// Starts the bridge as Grasshopper loads, so a tool outside Rhino can reach the components
    /// without anybody opening an editor first. Set SHARPSCRIPT_BRIDGE=off to keep the port shut.
    /// </summary>
    public class SharpScriptPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            ScriptBridgeServer.Start();
            return GH_LoadingInstruction.Proceed;
        }
    }
}
