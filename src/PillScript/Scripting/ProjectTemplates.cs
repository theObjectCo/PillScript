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
        public const string Source = @"// The parameters of this component are read out of the RunScript signature below.
// readme.md, in the sidebar, says how that works and what else the editor can do.

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
        /// The documentation, as a file in the project rather than a wall of comment at the top
        /// of Script.cs. It is where somebody who has just opened the editor is told how any of
        /// this works, so it is the one template worth keeping long.
        /// </summary>
        public const string Readme = @"# This script

The component on the canvas takes its shape from the code in this folder. Edit, press Compile,
and its inputs and outputs become whatever the `RunScript` signature says they are.

## Inputs and outputs

Every parameter of `RunScript` is an input. Every `out` parameter is an output. They appear in
the order they are written, and the type decides which Grasshopper parameter carries them and
how the data arrives.

- `double radius` is one value
- `List<Point3d> corners` is a list, and `Point3d[]` does the same
- `DataTree<Curve> curves` is a tree, and `GH_Structure<IGH_Goo>` does the same

A `GH_Structure` of goo arrives as it is. A `DataTree` of a plain type has each item converted, and
an item that will not convert is left out with the component saying which parameter and which
branch it came from; a tree can therefore be shorter inside the script than it was on the wire.

Four attributes fill in the rest, all of them optional.

- `[Name(""R"")]` sets the short name shown on the component, which is otherwise the parameter name
- `[Description(""..."")]` sets the tooltip
- `[Default(10.0)]` gives a value to use when nothing is connected, and makes the input optional
- `[Optional]` lets the input be left empty with nothing to fall back on

A plain C# default says what `[Default]` says: `double radius = 10.0`.

## What the compiler wants

One public class with a public `RunScript` method, a parameterless constructor, at least one
`out` parameter, and no two parameters sharing a name. Anything else is an error naming what is
wrong.

## Compiling

Nothing is compiled until you ask for it. Press `F5` or `Ctrl-B`. Until then the component goes
on running the build it already has, and wears a coloured plate on the canvas to say the sources
have moved past it.

`Ctrl-S` saves without building. Saving happens on its own a moment after typing stops anyway.

## What survives between runs

`RunScript` is called once per item rather than once per solve. Send a list of three numbers into
an input declared as a single value and it runs three times, with `Iteration` counting 0, 1, 2.

The class is turned into an object once per build, and every one of those calls runs on that same
object. So a field keeps what you put in it from one call to the next, and a field initialiser
runs once rather than on every iteration:

```csharp
int calls;                          // 1, 2, 3, 4 ... across iterations and solves
List<int> seen = new List<int>();   // built once, and it keeps growing
```

A `static` behaves the same way and for the same reason: both live as long as the build does.
Compiling throws the build away and takes them with it, which is also what happens when a
breakpoint is added or removed.

The trap is that a field collecting something grows on every iteration, not every solve, and
nothing empties it until the next compile. `Iteration == 0` is the moment to clear it if what you
want is one solve's worth.

## Controls in the Rhino panel

A script can put controls in a Rhino panel, docked beside Layers and Properties. Override
`RegisterUi` and register them. Each call does two things at once: it tells the panel what to
draw, and it writes what that control currently holds into the variable you hand it.

```csharp
double radius;
int sides;
string style;
bool bake;

public override void RegisterUi(UiRegistrar register)
{
    register.Caption(""Outline"");
    register.Slider(""radius"", 1, 50, 12, out radius);
    register.Whole(""sides"", 6, out sides, label: ""corners"");
    register.Choice(""style"", new[] { ""polygon"", ""circle"" }, out style);
    register.Button(""bake"", out bake, label: ""Bake to Rhino"");
}
```

`RegisterUi` runs before every solve, so by the time `RunScript` reads `radius` it holds what the
panel holds. Nothing is looked up by name at the point of use.

Then turn on **Publish to panel** in the component's menu. The panel stacks a section for every
published component, so several scripts can share it. Clicking a heading rolls its section up and
dragging one moves it among the others; both are kept with the document. The heading takes the
first `Caption` as its name, so the example above gives a section called Outline. Renaming the
component on the canvas takes the heading back.

The controls:

| Call | Gives |
| --- | --- |
| `Slider(name, min, max, value, out double)` | a number dragged between bounds |
| `Number(name, value, out double)` | a number typed in |
| `Whole(name, value, out int)` | the same, rounded, for a count |
| `Toggle(name, value, out bool, note:)` | a switch, with a word beside it |
| `Choice(name, options, out string)` | one of a list, segmented or a dropdown by length |
| `Text(name, value, out string)` | a line to type in |
| `Vector(name, value, out Vector3d)` | three numbers on one row |
| `Colour(name, value, out Color)` | a swatch opening Rhino's colour picker |
| `Layer(name, out string)` | a layer of the Rhino document, by full path |
| `Button(name, out bool, quiet:, icon:)` | something to press |
| `Caption(text)` | a line of explanation |

`Choice` takes any list, so the options can be worked out rather than written down. A quiet button
is drawn plainly, for the one standing beside the main action, and `icon:` puts a glyph on it:
`bake`, `run`, `refresh`, `add` or `remove`.

A `Button` is true for the one solve its press caused and false on every other, so acting on it
needs no memory of whether you already did.

Values are kept in the Grasshopper document, so they survive saving and reopening. Compiling does
not clear them; a control that disappears from `RegisterUi` takes its value with it.

The panel is a web page, and a `ui.css` in this folder is appended after its own stylesheet, so
any rule in there wins. A section is `.section`, holding a `.head` and a `.body`; a row is
`.widget` with its `label` and, for numbers, a `.reading`; the rest are `.caption`, `.field`,
`.slider`, `.segmented`, `.picker`, `.check`, `.press` and `.problem`. The colours are CSS
variables on `:root`, so a line like `--accent: #b05c18;` restyles more than a rule would.
That styling is not scoped to this script: a `ui.css` restyles the whole panel, including the
sections other components published.

## What ScriptBase gives you

- `Print(...)` writes to the output pane and to the component's `out` parameter
- `Warning(...)`, `Error(...)` and `Remark(...)` put a bubble on the component
- `Component`, `RhinoDocument` and `Iteration` say where you are

## More files

Add one with `+` in the sidebar. Every `.cs` file here compiles together, so a class in one is
visible from another with no `using` needed. `GlobalUsings.cs` holds the namespaces every file
starts with; add a line there rather than repeating a `using` in each file.

A file may be anything, not only C#: notes, a json table to read at run time, a shader. They all
live in the folder beside the sources and travel inside the Grasshopper document with it. What
they may not be is binary or larger than a megabyte, and a file like that put in the folder by
hand is left where it is rather than swallowed.

`Script.cs`, `Script.csproj` and `GlobalUsings.cs` cannot be renamed or deleted. This file can.

## Packages and references

`Script.csproj` is a real project file, and the folder it sits in opens in an IDE. Add a NuGet
package there, or use the References window in the editor, which edits the same file. A DLL goes
in as a `Reference` with a `HintPath`, and a library kept beside the script as a
`ProjectReference`, built before the script that needs it.

Rhino and Grasshopper are not listed in it. They arrive through `Directory.Build.targets`, which
is regenerated for whichever Rhino is running, and that is what keeps the project file free of
paths that work on only one machine.

## The icon

**Phosphor icon** in the component's menu takes any name from phosphoricons.com, say `gear-six`,
`flask` or `waves-bold`. That drawing then stands on the canvas and at the head of this script's
section in the panel. An empty name gives the pill back. The icon is fetched once and kept on
disk, so it costs nothing after the first time and nothing at all offline, where the pill stands
in until the fetch can happen.

## Keys

- `F5` or `Ctrl-B` compile
- `Ctrl-S` save
- `F9` breakpoint on this line
- `F12` go to declaration
- `Shift-F12` find every use
- `F2` rename, across every file at once
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

            // The rules the component compiles under, stated here rather than in the csproj, so
            // that what an IDE reports and what Compile reports agree for a project written
            // before these rules existed as much as for one written after.
            var rules = new XElement("PropertyGroup",
                new XElement("Nullable", "annotations"),
                new XElement("NoWarn", "$(NoWarn);1701;1702"));

            var document = new XDocument(
                new XComment(" Generated by PillScript. Edits here are overwritten. "),
                new XElement("Project", rules, new XElement("ItemGroup", references)));

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

            // Eto does not sit with the others. RhinoCommon loads out of the netcore folder inside
            // System, while Eto.dll stays in System itself, one above it. Which folder holds it is
            // a detail of how Rhino is put together, so both are asked and whichever answers wins.
            yield return Path.Combine(rhinoFolder, "Eto.dll");

            var above = Path.GetDirectoryName(rhinoFolder);
            if (above != null) yield return Path.Combine(above, "Eto.dll");
        }
    }
}
