using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace PillScript.Scripting
{
    /// <summary>
    /// Moves data between Grasshopper and one compiled script: reads the inputs the signature
    /// declares, calls RunScript, writes the outputs back.
    /// </summary>
    internal static class ScriptRunner
    {
        static readonly MethodInfo GetDataGeneric = typeof(IGH_DataAccess)
            .GetMethods()
            .First(m => m.Name == "GetData" && m.IsGenericMethod && m.GetParameters().Length == 2
                        && m.GetParameters()[0].ParameterType == typeof(int));

        static readonly MethodInfo GetDataListGeneric = typeof(IGH_DataAccess)
            .GetMethods()
            .First(m => m.Name == "GetDataList" && m.IsGenericMethod && m.GetParameters().Length == 2
                        && m.GetParameters()[0].ParameterType == typeof(int));

        static readonly MethodInfo GetDataTreeGeneric = typeof(IGH_DataAccess)
            .GetMethods()
            .First(m => m.Name == "GetDataTree" && m.IsGenericMethod && m.GetParameters().Length == 2
                        && m.GetParameters()[0].ParameterType == typeof(int));

        static readonly MethodInfo CastTo = typeof(IGH_Goo).GetMethod("CastTo");

        public static void Run(
            CompiledScript script,
            IGH_DataAccess access,
            GH_Component component,
            int iteration,
            int outputOffset,
            Action<string> print)
        {
            var signature = script.Signature;
            var instance = script.NewInstance();

            if (instance is ScriptBase scripted)
            {
                scripted.Component = component;
                scripted.Iteration = iteration;
                scripted.RhinoDocument = Rhino.RhinoDoc.ActiveDoc;
                scripted.PrintSink = print;
            }

            var parameters = signature.Run.GetParameters();
            var arguments = new object[parameters.Length];

            var inputIndex = 0;
            for (var i = 0; i < parameters.Length; i++)
            {
                var declared = parameters[i].ParameterType;
                if (parameters[i].IsOut || declared.IsByRef)
                {
                    arguments[i] = null;
                    continue;
                }

                var param = signature.Inputs[inputIndex];
                arguments[i] = ReadInput(access, inputIndex, param);
                inputIndex++;
            }

            signature.Run.Invoke(instance, arguments);

            var outputIndex = 0;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].IsOut && !parameters[i].ParameterType.IsByRef) continue;

                WriteOutput(access, outputIndex + outputOffset, signature.Outputs[outputIndex], arguments[i]);
                outputIndex++;
            }
        }

        static object ReadInput(IGH_DataAccess access, int index, ScriptParam param)
        {
            switch (param.Access)
            {
                case ScriptAccess.List:
                    return ReadList(access, index, param);

                case ScriptAccess.Tree:
                    return ReadTree(access, index, param);

                default:
                    var arguments = new[] { (object)index, Blank(param.ElementType) };
                    GetDataGeneric.MakeGenericMethod(param.ElementType).Invoke(access, arguments);
                    return arguments[1];
            }
        }

        static object ReadList(IGH_DataAccess access, int index, ScriptParam param)
        {
            var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(param.ElementType));
            GetDataListGeneric.MakeGenericMethod(param.ElementType)
                .Invoke(access, new[] { (object)index, list });

            if (!param.DeclaredType.IsArray) return list;

            var toArray = typeof(List<>).MakeGenericType(param.ElementType).GetMethod("ToArray");
            return toArray.Invoke(list, null);
        }

        /// <summary>
        /// Trees arrive as goo. A GH_Structure signature takes them as they are; a DataTree of a
        /// plain type gets each item unwrapped into that type.
        /// </summary>
        static object ReadTree(IGH_DataAccess access, int index, ScriptParam param)
        {
            var isStructure = param.DeclaredType.GetGenericTypeDefinition() == typeof(GH_Structure<>);

            if (isStructure && typeof(IGH_Goo).IsAssignableFrom(param.ElementType))
            {
                var arguments = new object[] { index, null };
                GetDataTreeGeneric.MakeGenericMethod(param.ElementType).Invoke(access, arguments);
                return arguments[1] ?? Activator.CreateInstance(param.DeclaredType);
            }

            var raw = new object[] { index, null };
            GetDataTreeGeneric.MakeGenericMethod(typeof(IGH_Goo)).Invoke(access, raw);
            var source = raw[1] as GH_Structure<IGH_Goo>;

            var tree = Activator.CreateInstance(typeof(DataTree<>).MakeGenericType(param.ElementType));
            if (source == null) return tree;

            var add = tree.GetType().GetMethod("Add", new[] { param.ElementType, typeof(GH_Path) });

            for (var branch = 0; branch < source.PathCount; branch++)
            {
                var path = source.Paths[branch];
                foreach (var goo in source.Branches[branch])
                {
                    if (TryUnwrap(goo, param.ElementType, out var value))
                        add.Invoke(tree, new[] { value, path });
                }
            }

            return tree;
        }

        static void WriteOutput(IGH_DataAccess access, int index, ScriptParam param, object value)
        {
            switch (param.Access)
            {
                case ScriptAccess.List:
                    access.SetDataList(index, value as IEnumerable);
                    break;

                case ScriptAccess.Tree:
                    WriteTree(access, index, value);
                    break;

                default:
                    access.SetData(index, value);
                    break;
            }
        }

        static void WriteTree(IGH_DataAccess access, int index, object value)
        {
            switch (value)
            {
                case null:
                    break;

                case IGH_Structure structure:
                    access.SetDataTree(index, structure);
                    break;

                case IGH_DataTree dataTree:
                    access.SetDataTree(index, dataTree);
                    break;

                default:
                    access.SetData(index, value);
                    break;
            }
        }

        /// <summary>Turns a goo into the CLR type a parameter declared, if it can.</summary>
        static bool TryUnwrap(IGH_Goo goo, Type target, out object value)
        {
            value = null;
            if (goo == null) return false;
            if (target.IsInstanceOfType(goo)) { value = goo; return true; }

            var arguments = new object[] { null };
            if ((bool)CastTo.MakeGenericMethod(target).Invoke(goo, arguments))
            {
                value = arguments[0];
                return true;
            }

            var raw = goo.ScriptVariable();
            if (target.IsInstanceOfType(raw)) { value = raw; return true; }

            try
            {
                value = Convert.ChangeType(raw, target);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static object Blank(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
