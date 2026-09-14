using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PillScript.Scripting
{
    /// <summary>
    /// The templates a new script starts from, plus the one generated file that ties the project
    /// to the Rhino running on this machine. They sit here, away from the project code, so the text
    /// an author reads first can be read in one piece.
    /// </summary>
    internal static class ProjectTemplates
    {
        /// <summary>
        /// The usings every file in the script starts with. This is an ordinary project file and
        /// can be edited. Adding a line here saves repeating the same using in every file.
        /// </summary>
        public const string Usings = @"// Namespaces every file in this script starts with, so that no file has to repeat them. This is
// an ordinary file of the project: add a line here and the next compile picks it up.
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
        /// The project file. It holds no machine specific paths, so the same csproj serves both
        /// the IDE that opens it and the restore the component runs.
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
        /// The Script.cs a new component starts with. It compiles and produces a circle, so a
        /// fresh component has something to run before a line of it is edited. The two comment
        /// lines at the top point at readme.md, where the rules are written down.
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
        /// The documentation, kept as a file in the project instead of a long comment at the top
        /// of Script.cs. It is the only place the rules are written down, which is why it runs to
        /// some length.
        /// </summary>
        public const string Readme = @"# This script

The component on the canvas is defined by the code in this folder. Edit it, press Compile, and its
inputs and outputs follow the `RunScript` signature.

## Inputs and outputs

Every parameter of `RunScript` is an input. Every `out` parameter is an output. They appear in
the order they are written, and the type decides which Grasshopper parameter carries them and
how the data arrives.

- `double radius` is one value
- `List<Point3d> corners` is a list, and `Point3d[]` does the same
- `DataTree<Curve> curves` is a tree, and `GH_Structure<IGH_Goo>` does the same

A `GH_Structure` of goo arrives as it is. A `DataTree` of a plain type has each item converted. An
item that will not convert is left out, and the component reports which parameter and which branch
it came from, so a tree can be shorter inside the script than it was on the wire.

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

Nothing compiles on its own. Press `F5` or `Ctrl-B`. Until then the component keeps running its
last build and shows a coloured plate on the canvas to mark it as out of date.

`Ctrl-S` saves without building. Saving happens on its own a moment after typing stops anyway.

## What survives between runs

`RunScript` is called once per item, not once per solve. Send a list of three numbers into an input
declared as a single value and it runs three times, with `Iteration` counting 0, 1, 2.

The class becomes an object once per build, and all those calls run on that same object. A field
therefore still holds its value on the next call, and a field initialiser runs once:

```csharp
int calls;                          // 1, 2, 3, 4 ... across iterations and solves
List<int> seen = new List<int>();   // built once, and it keeps growing
```

A `static` behaves the same way and for the same reason: both live as long as the build does.
Compiling throws the build away and takes them with it, which is also what happens when a
breakpoint is added or removed.

The trap is that a field collecting something grows on every iteration, not every solve, and
nothing empties it until the next compile. Clearing it while `Iteration == 0` keeps the contents
to a single solve.

## Controls in the Rhino panel

A script can register controls that appear in a Rhino panel, docked with Layers and Properties.
Override `RegisterUi`. Each call describes one control for the panel and assigns that control's
current value to the variable passed in.

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

`RegisterUi` runs before every solve, so `radius` holds the panel's value by the time `RunScript`
reads it. No lookup by name is needed at the point of use.

Then turn on **Publish to panel** in the component's menu. The panel holds a section for every
published component, so several scripts can share it. Clicking a heading rolls its section up,
dragging one moves it among the others, and both are saved with the document. The heading is named
after the first `Caption`, so the example above gives a section called Outline. Renaming the
component on the canvas overrides that.

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

`Choice` takes any list, so the options can be computed. A quiet button is drawn without the
accent colour, for a secondary action, and `icon:` puts a glyph on one: `bake`, `run`, `refresh`,
`add` or `remove`.

A `Button` is true for the one solve its press caused and false on every other solve, so acting on
it needs no record of whether it was already handled.

Values are saved in the Grasshopper document and survive reopening. Compiling does not clear them.
A control removed from `RegisterUi` is forgotten together with its value.

The panel is a web page, and a `ui.css` in this folder is appended after its own stylesheet, so any
rule in it wins. A section is `.section`, holding a `.head` and a `.body`. A row is `.widget` with
its `label` and, for numbers, a `.reading`. The rest are `.caption`, `.field`, `.slider`,
`.segmented`, `.picker`, `.check`, `.press` and `.problem`. The colours are CSS variables on
`:root`: setting `--accent: #b05c18;` recolours everything that uses it. This styling is not scoped
to one script. A `ui.css` restyles the whole panel, including the sections other components
published.

## Drawing in the viewport

Geometry on an output is previewed by Grasshopper already. To draw something that is not an output,
override `DrawWires`, and `DrawMeshes` for anything drawn with a material:

```csharp
public override void DrawWires(IGH_PreviewArgs args)
{
    args.Display.DrawPoint(centre, PointStyle.RoundControlPoint, 4, Color.OrangeRed);
}

public override BoundingBox DrawBounds => box;
```

`DrawBounds` is what Zoom Extents and the clipping planes work from. Without it the drawing is
clipped away at some camera angles.

Both methods run on every redraw, which is far more often than a solve, so the work belongs in
`RunScript` and the drawing should read a field it filled in. A throw inside either one is caught,
printed once on the Rhino command line, and stops the drawing until the next compile.

## What ScriptBase provides

- `Print(...)` writes to the output pane and to the component's `out` parameter
- `Warning(...)`, `Error(...)` and `Remark(...)` put a bubble on the component
- `Component`, `RhinoDocument` and `Iteration` identify the component, the Rhino document and the
  current iteration
- `DrawWires(...)`, `DrawMeshes(...)` and `DrawBounds` draw into the Rhino viewport

## More files

Add one with `+` in the sidebar. Every `.cs` file here compiles together, so a class in one is
visible from another with no `using` needed. `GlobalUsings.cs` holds the namespaces every file
starts with. Add a line there and no file has to repeat it.

A file may be anything, not only C#: notes, a json table to read at run time, a shader. They all
live in the folder beside the sources and travel inside the Grasshopper document. Binary files and
files larger than a megabyte are not taken into the document; one placed in the folder by hand
stays there and is left alone.

`Script.cs`, `Script.csproj` and `GlobalUsings.cs` cannot be renamed or deleted; this readme can.

## Packages and references

`Script.csproj` is a real project file, and the folder it sits in opens in an IDE. Add a NuGet
package there, or use the References window in the editor, which edits the same file. A DLL goes
in as a `Reference` with a `HintPath`, and a library kept beside the script as a
`ProjectReference`, built before the script that needs it.

Rhino and Grasshopper are not listed in it. They arrive through `Directory.Build.targets`, which
is regenerated for whichever Rhino is running, and that is what keeps the project file free of
paths that work on only one machine.

## The icon

**Phosphor icon** in the component's menu takes any name from https://phosphoricons.com, for example
`gear-six`, `flask` or `waves-bold`. Type it on the line in the menu and press Enter. The drawing
is then used on the canvas and at the head of this script's section in the panel. An empty name
restores the pill. Each icon is fetched once and kept on disk. Without a route to the internet the
component keeps the pill, and the icon appears once a fetch succeeds.

## Keys

- `F5` or `Ctrl-B` compile
- `Ctrl-S` save
- `F9` breakpoint on this line
- `F12` go to declaration
- `Shift-F12` find every use
- `F2` rename, across every file at once
";

        /// <summary>
        /// Points the project at the Rhino process that is running, instead of at a RhinoCommon
        /// NuGet package of some other version. Regenerated on every mirror, which keeps machine
        /// specific paths out of the csproj the author edits.
        /// </summary>
        public static string BuildTargets()
        {
            var references = HostReferences().Select(path => new XElement("Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                new XElement("HintPath", path),
                new XElement("Private", "false")));

            // The rules the component compiles under. They go in the generated file instead of
            // the csproj, so projects created before these rules existed get them too and the IDE
            // reports the same diagnostics as Compile.
            var rules = new XElement("PropertyGroup",
                new XElement("Nullable", "annotations"),
                new XElement("NoWarn", "$(NoWarn);1701;1702"));

            var document = new XDocument(
                new XComment(" Generated by PillScript. Edits here are overwritten. "),
                new XElement("Project", rules, new XElement("ItemGroup", references)));

            return document.ToString();
        }

        /// <summary>
        /// The Rhino assemblies a script may use. ScriptCompiler is given the same list, so the
        /// IDE resolves from the csproj exactly what the component compiles against.
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

            // Eto is not stored with the others. RhinoCommon.dll loads from the netcore folder
            // inside System, while Eto.dll sits in System itself, one level up. That layout is an
            // internal detail of Rhino, so both folders are tried and the first hit is used.
            yield return Path.Combine(rhinoFolder, "Eto.dll");

            var above = Path.GetDirectoryName(rhinoFolder);
            if (above != null) yield return Path.Combine(above, "Eto.dll");
        }
    }
}
