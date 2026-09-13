using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PillScript.Scripting
{
    /// <summary>
    /// What a new script starts as, and the one generated file that ties the project to the Rhino
    /// running on this machine. Kept apart from the project itself so the text an author first
    /// reads can be read here in one piece.
    /// </summary>
    internal static class ProjectTemplates
    {
        /// <summary>
        /// The usings every file in the script starts with. An ordinary project file, so it can be
        /// edited: adding a line here is how a script reaches a namespace it uses often without
        /// repeating the using in each file.
        /// </summary>
        public const string Usings = @"// Namespaces every file in this script starts with, so no file has to repeat them. Add a line
// here rather than a using at the top of each file; this is an ordinary file of the project and
// the next compile picks it up.
global using System;
global using System.Collections;
global using System.Collections.Generic;
global using System.Linq;
global using Rhino;
global using Rhino.Geometry;
global using Grasshopper;
global using Grasshopper.Kernel;
global using Grasshopper.Kernel.Data;
global using Grasshopper.Kernel.Types;
global using PillScript;
";

        /// <summary>
        /// The project file. It carries no machine specific paths of its own, which is what lets
        /// it be both the thing an IDE opens and the thing the component restores against.
        /// </summary>
        public const string Project = @"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net7.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>Script</AssemblyName>
  </PropertyGroup>

  <!--
    A real project file. Open the folder in an IDE and it builds there too.

    Rhino, Grasshopper and the script API are not listed here: they arrive through
    Directory.Build.targets, which PillScript writes next to this file and regenerates for
    whichever Rhino is running. That is what keeps this file free of paths that only work on
    one machine.

    Add a package below and press Compile: it is restored and used by the component and by the
    IDE both. A DLL goes in as a Reference with a HintPath, and a library kept beside the script
    as a ProjectReference, which is built before the script that needs it. The References window
    in the editor edits this same file, so either way round works.
  -->
  <ItemGroup>
    <!-- <PackageReference Include=""MathNet.Numerics"" Version=""5.0.0"" /> -->
    <!-- <Reference Include=""Something""><HintPath>C:\libs\Something.dll</HintPath></Reference> -->
  </ItemGroup>

</Project>
";

        /// <summary>
        /// What a new component holds. The comment at the top is the documentation for writing
        /// one of these: the rules the compiler enforces, how a parameter becomes an input, and
        /// what lives between runs. It is the first thing anybody opening the editor reads, and
        /// the only place they are told any of it.
        /// </summary>
        public const string Source = @"// Where this component's parameters come from: every RunScript parameter below is an
// input, every out parameter an output, in the order written. The type picks the
// Grasshopper parameter and how the data arrives. A List<T> or T[] makes it a list, a
// DataTree<T> or GH_Structure<T> a tree, anything else one value.
//
// Attributes fill in the rest, all optional. [Name(""R"")] is the short name shown on the
// component, [Description(""..."")] the tooltip, [Default(10.0)] a value to use when
// nothing is connected, [Optional] permission to be left empty. A plain C# default says
// what [Default] says: double radius = 10.0.
//
// The compiler wants one public class with a public RunScript, a parameterless
// constructor, at least one out parameter, and no two parameters sharing a name.
//
// Nothing is compiled until asked: F5 or Ctrl-B. Until then the component runs the build
// it already has and wears a plate on the canvas. F9 sets a breakpoint, F12 goes to a
// declaration, Shift-F12 finds every use, F2 renames across files. More files: + in the
// sidebar.
//
// RunScript runs once per item rather than once per solve, and Iteration counts them. The
// class becomes an object once per build and every call runs on that object, so a field
// keeps what you put in it between calls, until the next compile clears it.
//
// From ScriptBase: Print writes to the output pane and the out parameter; Warning, Error
// and Remark put a bubble on the component; Component, RhinoDocument and Iteration say
// where you are.

public class Script : ScriptBase
{
    public void RunScript(
        [Description(""Radius of the circle"")] [Default(10.0)] double radius,
        [Description(""Plane the circle sits on, world XY when nothing is connected"")]
        [Optional] Plane plane,
        out Circle result)
    {
        if (!plane.IsValid) plane = Plane.WorldXY;

        result = new Circle(plane, radius);

        Print(""Circumference: {0:F2}"", result.Circumference);
    }
}
";

        /// <summary>
        /// Points the project at the Rhino that is running right now rather than at a NuGet
        /// package of some other version. Regenerated on every mirror, which is what keeps the
        /// csproj the author edits free of machine specific paths.
        /// </summary>
        public static string BuildTargets()
        {
            var references = HostReferences().Select(path => new XElement("Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                new XElement("HintPath", path),
                new XElement("Private", "false")));

            var document = new XDocument(
                new XComment(" Generated by PillScript. Edits here are overwritten. "),
                new XElement("Project", new XElement("ItemGroup", references)));

            return document.ToString();
        }

        /// <summary>
        /// The Rhino side assemblies a script may use. The compiler takes the same list, so what
        /// the IDE resolves from the csproj and what the component compiles against stay equal.
        /// </summary>
        public static IEnumerable<string> HostReferences()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Candidates())
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                if (seen.Add(Path.GetFileNameWithoutExtension(path))) yield return path;
            }
        }

        static IEnumerable<string> Candidates()
        {
            var rhinoCommon = typeof(Rhino.RhinoDoc).Assembly.Location;
            var grasshopper = typeof(Grasshopper.Kernel.GH_Component).Assembly.Location;

            yield return rhinoCommon;
            yield return grasshopper;
            yield return typeof(ProjectTemplates).Assembly.Location;

            var grasshopperFolder = Path.GetDirectoryName(grasshopper);
            if (grasshopperFolder != null) yield return Path.Combine(grasshopperFolder, "GH_IO.dll");

            var rhinoFolder = Path.GetDirectoryName(rhinoCommon);
            if (rhinoFolder == null) yield break;

            yield return Path.Combine(rhinoFolder, "Rhino.UI.dll");
            yield return Path.Combine(rhinoFolder, "Eto.dll");
        }
    }
}
