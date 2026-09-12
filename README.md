# SharpScript

A C# script component for Grasshopper in Rhino 8. It differs from the built-in one in six ways:
the editor is Monaco with Roslyn behind it, compilation happens when asked rather than on every
keystroke, a script is a folder of files rather than a single buffer, the component's inputs and
outputs are read out of the code instead of being dragged on by hand, the folder is a real csproj
that an IDE can open, and breakpoints stop the solve so the locals can be read.

The window is laid out and coloured after Visual Studio Code's Dark Modern: a title bar the page
draws itself, a rail of layout toggles, a sidebar of files and parameters, a toolbar, a panel with
output, a shell, problems and variables, and a status bar. Rhino owns the window, so it stays over
the canvas and hides with it, without floating over everything else on the screen.

Two buttons at the foot of the rail dock the editor to the left or the right of the canvas.
Pressing the side it already sits on gives it back as a window of its own. Below them, a third
button takes the canvas to the component this editor belongs to and selects it: half a second of
travel to full zoom, landing in the middle of whatever strip of canvas the editor has left
visible. It moves rather than jumps, because a jump leaves you working out which way the canvas
went. Docked the editor fills the
canvas rectangle only, leaving
the ribbon and the status bar clear, and it moves and resizes with Grasshopper rather than being
dragged around after it. The strip along the edge facing the canvas is a splitter: drag it to
change the share, which is kept as a fraction so it survives Grasshopper being resized. The title
bar goes away while docked, since there is no window left to move, and a close button appears in
the rail instead.

Docking does not shrink the canvas; the editor covers part of it. Making room properly would mean
resizing Grasshopper's own controls, which is not a thing to rely on between versions.

While an editor is open its component wears a blue plate on the canvas. With several script
components in a definition that is the only way to tell which one the window in front of you
belongs to.

## Building and installing

```
dotnet build src/SharpScript/SharpScript.csproj -c Release
```

Copy everything in `src/SharpScript/bin/Release` into
`%APPDATA%\Grasshopper\Libraries\SharpScript` and restart Rhino. The `web` folder has to travel
with the `.gha`; it holds Monaco.

## Writing a script

The component looks for exactly one public class with a public `RunScript` method. Ordinary
parameters become inputs, `out` parameters become outputs, and the CLR type decides which
Grasshopper parameter carries them.

```csharp
public class Script : ScriptBase
{
    public void RunScript(
        [Description("Corner points of the outline")] List<Point3d> points,
        [Default(2)] int divisions,
        out Polyline outline,
        out double length)
    {
        outline = PolyHelper.Close(points);
        length = outline.Length;

        Print("{0} corners", points.Count);
    }
}
```

`GlobalUsings.cs` is an ordinary file in the project and holds the namespaces every file starts
with, so a script can reach a namespace it uses often by adding one line there rather than
repeating a using in each file. It cannot be renamed or deleted, because the build needs it.

`List<T>` gives a list input, `GH_Structure<T>` or `DataTree<T>` gives a tree, anything else is an
item. `[Description]`, `[Name]`, `[Default]` and `[Optional]` adjust the parameter. Deriving from
`ScriptBase` adds `Print`, `Remark`, `Warning`, `Error`, `Component`, `Iteration` and
`RhinoDocument`; it is optional.

The parameter list is rebuilt after each successful compile. Wires survive as long as the parameter
keeps its name, its type and its access.

## What the editor knows

Completion, hover, parameter hints and squiggles come from Roslyn, running in the Rhino process
against the same assemblies the component compiles with. Typing a dot after `Mesh` lists the
members RhinoCommon actually has, and a misspelled member is underlined before anything is built.

Errors shown while typing come from the semantic model, so they describe the code on screen rather
than the last build. The Problems panel is the record of the last compile, which is a different
thing and is why both exist.

## Breakpoints

Click the gutter, or press F9, to mark a line. The marks are compiled into the script: Roslyn
rewrites the tree and puts a call in front of each marked statement carrying the locals that are
definitely assigned there. When the solve reaches one it stops, the editor shows the values, and
Rhino stays responsive because the wait is a nested message loop rather than a blocked thread.
Continue lets the script finish; Stop abandons that solve.

Because the pauses are part of the build, moving a breakpoint makes the component stale. The
breakpoint count in the status bar opens a small menu to disable every mark at once, which leaves
them where they are but builds a script that runs straight through, or to remove them.

## Compiling

Nothing compiles during a solution. Press Compile in the editor, F5, or use the component's right
click menu. Between an edit and the next compile the component is outlined in blue and keeps
running the previous build, so a half-written script does not break a definition that was working.

A component compiles once when the document opens, which is what restores a saved definition to a
running state. That can be turned off per component.

## The project on disk

Each component owns a folder under `%LOCALAPPDATA%\SharpScript\projects`, holding a normal SDK
style project. Open that folder in Visual Studio, Rider or VS Code and you get full completion
against the Rhino that is running: `Directory.Build.targets` is regenerated on every mirror with
this machine's paths, and `GlobalUsings.g.cs` carries the same usings the component compiles with.
Edits made outside are picked up by a watcher, and the component goes stale as if they had been
typed in the editor.

Sources live in the `.gh` file, so sending someone a definition sends the code with it. The folder
is a working copy.

`PackageReference` entries in `Script.csproj` are restored with the .NET SDK on the next compile
and used by both the component and the IDE. `Reference` entries with a `HintPath` work too, for a
DLL that is not on NuGet. Packages are the only part that needs the SDK installed.

References are managed from the sidebar: the References line opens a window listing the assemblies
the project points at by path and the NuGet packages it asks for, with a file picker for the first
and a name and version for the second. Both are written into Script.csproj, so the same edits can
be made by hand or in an IDE, and the same list is available over the bridge.

`ProjectReference` to a csproj alongside the script is built with the SDK on compile and its output
referenced, so a library can be kept in its own project and edited in an IDE.

The Terminal tab opens PowerShell in the project folder for `dotnet add package`, `dotnet build`
and git.

## Driving it from outside

The plugin listens on http://127.0.0.1:57321 while Grasshopper is loaded, and `tools/mcp.js` wraps
that as an MCP server so an agent can work on a component's project directly: list the components
on the open canvases, read, write, rename and delete files, compile and read the diagnostics, solve
and read what the script printed. It is the same project the editor window shows, so a file written
this way appears in the editor immediately and the component goes stale until it is compiled.

Register it with:

```
claude mcp add sharpscript -- node <repo>/tools/mcp.js
```

The port is loopback only and has no authentication, which is the same posture as a debug server:
anything able to run code on the machine can already do more than this allows. Set
`SHARPSCRIPT_BRIDGE=off` to keep the port shut, or `SHARPSCRIPT_BRIDGE_PORT` to move it.

## Known gaps

- The terminal is a line shell, not a terminal. Its streams are redirected rather than attached to
  a pseudo console, so a full screen program such as an editor or a pager will not draw, and each
  command appears twice: once as it is typed and once as PowerShell echoes it. A ConPTY version
  was written and abandoned: the pseudo console is created and its pipes carry ConPTY's own
  handshake, but no client ever attaches to it, with the attribute list sized and applied exactly
  as the documented sequence requires.
- A breakpoint pauses the whole Grasshopper solve, and the canvas stays live while it does. Editing
  the document while a script is paused has not been thought through.
- The first completion in a session waits for Roslyn to build its view of the project, which takes
  a second or two. After that it is quick.
- Completion inserts the item's display text. Roslyn's own insertion rules, which matter for
  override and partial method completion, are not applied.
- Refactoring, go to definition and find references are not wired up, though the workspace that
  would answer them is already there.
- The editor page can be opened in a browser for work on the UI itself: `index.html` shows a
  sample, and `index.html?probe=1` stands in a fake component so the Monaco wiring can be
  exercised without Rhino.
