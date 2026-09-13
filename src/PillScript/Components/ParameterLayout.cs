using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using PillScript.Scripting;

namespace PillScript.Components
{
    /// <summary>
    /// Turns the signature a build produced into the component's parameters. It sits apart from
    /// the component because deciding which wires survive, and whether the canvas already matches
    /// the code, is a job of its own.
    /// </summary>
    internal static class ParameterLayout
    {
        /// <summary>
        /// Rebuilds the parameter list, reconnecting wires whose parameter kept its name and
        /// shape. Does nothing when the layout already matches, so an ordinary recompile leaves
        /// the canvas alone.
        /// </summary>
        public static void Apply(GH_Component component, ScriptSignature signature, int fixedOutputs)
        {
            if (Matches(component, signature, fixedOutputs)) return;

            component.RecordUndoEvent("Script parameters");

            var slots = component.Params;

            var inputSources = slots.Input.ToDictionary(
                p => p.Name, p => p.Sources.ToList(), StringComparer.OrdinalIgnoreCase);

            var outputRecipients = slots.Output.Skip(fixedOutputs).ToDictionary(
                p => p.Name, p => p.Recipients.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var param in slots.Input.ToList())
                slots.UnregisterInputParameter(param);

            foreach (var param in slots.Output.Skip(fixedOutputs).ToList())
                slots.UnregisterOutputParameter(param);

            foreach (var declared in signature.Inputs)
            {
                var param = Build(declared);
                slots.RegisterInputParam(param);

                if (!inputSources.TryGetValue(declared.Name, out var sources)) continue;
                foreach (var source in sources) param.AddSource(source);
            }

            foreach (var declared in signature.Outputs)
            {
                var param = Build(declared);
                slots.RegisterOutputParam(param);

                if (!outputRecipients.TryGetValue(declared.Name, out var recipients)) continue;
                foreach (var recipient in recipients) recipient.AddSource(param);
            }

            slots.OnParametersChanged();
        }

        static bool Matches(GH_Component component, ScriptSignature signature, int fixedOutputs)
        {
            var inputs = component.Params.Input;
            var outputs = component.Params.Output;

            if (inputs.Count != signature.Inputs.Count) return false;
            if (outputs.Count != signature.Outputs.Count + fixedOutputs) return false;

            for (var i = 0; i < signature.Inputs.Count; i++)
            {
                if (!Describes(inputs[i], signature.Inputs[i])) return false;
            }

            for (var i = 0; i < signature.Outputs.Count; i++)
            {
                if (!Describes(outputs[i + fixedOutputs], signature.Outputs[i])) return false;
            }

            return true;
        }

        static bool Describes(IGH_Param param, ScriptParam declared)
            => string.Equals(param.Name, declared.Name, StringComparison.Ordinal)
               && param.Access == declared.GhAccess
               && param.GetType() == ParamFactory.Create(declared.ElementType).GetType();

        static IGH_Param Build(ScriptParam declared)
        {
            var param = ParamFactory.Create(declared.ElementType);

            param.Name = declared.Name;
            param.NickName = declared.Nickname;
            param.Description = declared.Description;
            param.Access = declared.GhAccess;
            param.Optional = declared.Optional || declared.Default != null;

            if (!declared.IsOutput) ParamFactory.ApplyDefault(param, declared.Default);

            return param;
        }
    }
}
