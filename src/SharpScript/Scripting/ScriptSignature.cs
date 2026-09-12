using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace SharpScript.Scripting
{
    internal enum ScriptAccess { Item, List, Tree }

    /// <summary>One input or output, as declared by a RunScript parameter.</summary>
    internal sealed class ScriptParam
    {
        public string Name;
        public string Nickname;
        public string Description = string.Empty;
        public Type ElementType;
        public Type DeclaredType;
        public ScriptAccess Access;
        public object Default;
        public bool Optional;
        public bool IsOutput;

        public GH_ParamAccess GhAccess => Access switch
        {
            ScriptAccess.List => GH_ParamAccess.list,
            ScriptAccess.Tree => GH_ParamAccess.tree,
            _ => GH_ParamAccess.item
        };

        /// <summary>Identity used to decide whether a rebuilt parameter can keep its wires.</summary>
        public string Signature => $"{Name}:{ElementType.FullName}:{Access}";
    }

    /// <summary>The input and output shape the component takes from a compiled script.</summary>
    internal sealed class ScriptSignature
    {
        public Type ScriptType;
        public MethodInfo Run;
        public List<ScriptParam> Inputs = new List<ScriptParam>();
        public List<ScriptParam> Outputs = new List<ScriptParam>();

        const string EntryMethod = "RunScript";

        /// <summary>
        /// Finds the single RunScript entry point in a freshly compiled assembly and reads
        /// the parameter list off it. Throws when there is no entry point or more than one.
        /// </summary>
        public static ScriptSignature Read(Assembly assembly)
        {
            var candidates = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic)
                .Select(t => new { Type = t, Method = FindRun(t) })
                .Where(x => x.Method != null)
                .ToList();

            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    $"No entry point found. Add a public class with a public void {EntryMethod}(...) method.");

            if (candidates.Count > 1)
            {
                var names = string.Join(", ", candidates.Select(c => c.Type.Name));
                throw new InvalidOperationException(
                    $"Several classes declare {EntryMethod}: {names}. Exactly one is allowed.");
            }

            var entry = candidates[0];
            var signature = new ScriptSignature
            {
                ScriptType = entry.Type,
                Run = entry.Method
            };

            if (entry.Type.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException(
                    $"{entry.Type.Name} needs a public parameterless constructor.");

            foreach (var parameter in entry.Method.GetParameters())
                signature.Add(Describe(parameter));

            if (signature.Outputs.Count == 0)
                throw new InvalidOperationException(
                    $"{EntryMethod} declares no outputs. Add at least one out parameter.");

            var duplicate = signature.Inputs.Concat(signature.Outputs)
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"Parameter name '{duplicate.Key}' is used twice.");

            return signature;
        }

        static MethodInfo FindRun(Type type)
            => type.GetMethod(EntryMethod, BindingFlags.Public | BindingFlags.Instance);

        void Add(ScriptParam param)
        {
            if (param.IsOutput) Outputs.Add(param);
            else Inputs.Add(param);
        }

        static ScriptParam Describe(ParameterInfo parameter)
        {
            var declared = parameter.ParameterType;
            var isOutput = parameter.IsOut || declared.IsByRef;
            if (declared.IsByRef) declared = declared.GetElementType();

            var param = new ScriptParam
            {
                Name = parameter.Name,
                Nickname = parameter.GetCustomAttribute<NameAttribute>()?.Nickname ?? parameter.Name,
                Description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Text ?? string.Empty,
                DeclaredType = declared,
                IsOutput = isOutput,
                Optional = parameter.GetCustomAttribute<OptionalAttribute>() != null,
                Default = parameter.GetCustomAttribute<DefaultAttribute>()?.Value
            };

            (param.Access, param.ElementType) = ReadAccess(declared);

            if (param.Default == null && parameter.HasDefaultValue && parameter.DefaultValue != null)
            {
                param.Default = parameter.DefaultValue;
                param.Optional = true;
            }

            return param;
        }

        /// <summary>
        /// Reads the data access from the declared type: List&lt;T&gt; is a list, a tree type is a
        /// tree, anything else is a single item.
        /// </summary>
        static (ScriptAccess, Type) ReadAccess(Type declared)
        {
            if (declared.IsGenericType)
            {
                var definition = declared.GetGenericTypeDefinition();
                var argument = declared.GetGenericArguments()[0];

                if (definition == typeof(List<>)) return (ScriptAccess.List, argument);
                if (definition == typeof(GH_Structure<>)) return (ScriptAccess.Tree, argument);
                if (definition == typeof(DataTree<>)) return (ScriptAccess.Tree, argument);
            }

            if (declared.IsArray && declared.GetArrayRank() == 1)
                return (ScriptAccess.List, declared.GetElementType());

            return (ScriptAccess.Item, declared);
        }
    }
}
