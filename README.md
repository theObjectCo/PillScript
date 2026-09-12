# PillScript

A C# script component for Grasshopper in Rhino 8. It differs from the built-in one in six ways:
the editor is Monaco with Roslyn behind it, compilation happens when asked rather than on every
keystroke, a script is a folder of files rather than a single buffer, the component's inputs and
outputs are read out of the code instead of being dragged on by hand, the folder is a real csproj
that an IDE can open, and breakpoints stop the solve so the locals can be read.

The window is laid out and coloured after Visual Studio Code's Dark Modern: a title bar the page
draws itself, a rail of layout toggles, a sidebar of files and parameters, a toolbar, a panel with
output, problems and variables, and a status bar. Rhino owns the window, so it stays over
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

## Installing

Rhino 8.30 or newer, Windows, 64 bit. The editor runs in WebView2, which Rhino 8 installs for its
own interface, so there is nothing else to fetch.

In Rhino, run `_PackageManager`, search for PillScript, install it and restart Rhino. The download
is about 13 MB, most of it Monaco.

## Building it yourself

```
dotnet build src/PillScript/PillScript.csproj -c Release
```

Copy everything in `src/PillScript/bin/Release` into
`%APPDATA%\Grasshopper\Libraries\PillScript` and restart Rhino. The `web` folder has to travel
with the `.gha`; it holds Monaco.

The same folder is what the package is made of. `manifest.yml` and `icon.png` are copied into it by
the build, so a package is built from the build output:

```
"C:\Program Files\Rhino 8\System\Yak.exe" build --platform win
```

run inside `src/PillScript/bin/Release`. Yak reads the minimum Rhino version out of the assembly's
own references, which is where the 8.30 in the package name comes from: the Grasshopper package
this builds against. Referencing an older one widens the range, and wants testing on whatever the
oldest Rhino is that has to run it.

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

F12 goes to where a thing is declared, Shift-F12 lists everywhere it is used, and F2 renames it
across every file in the script at once. These answer for the script's own files only: a type out
of RhinoCommon is declared in an assembly, and there is no file to open for it.

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

Each component owns a folder under `%LOCALAPPDATA%\PillScript\projects`, holding a normal SDK
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

## Driving it from outside

With `PILLSCRIPT_BRIDGE=on` in the environment Rhino starts from, the plugin listens on
http://127.0.0.1:57321, and `tools/mcp.js` wraps that as an MCP server so an agent can work on a
component's project directly: list the components on the open canvases, read, write, rename and
delete files, compile and read the diagnostics, solve and read what the script printed. It is the
same project the editor window shows, so a file written this way appears in the editor immediately
and the component goes stale until it is compiled. `PILLSCRIPT_BRIDGE_PORT` moves the port.

Register it with:

```
claude mcp add pillscript -- node <repo>/tools/mcp.js
```

Nothing listens unless asked for. The editor needs no port at all: it talks to the plugin inside
the process, so installing the package gets you the editor and nothing else. Turning the bridge on
is worth understanding first, because writing a file and compiling it is running code.

Binding to the loopback address keeps other machines out, and does nothing about the browser on
this one: a page can reach 127.0.0.1, and it does not need to read the answer for a written and
compiled script to have run. So a request is only answered when it declares `application/json`,
carries no `Origin` header, and names the loopback address in `Host` — a page cannot manage the
first two, and the third is what a rebound name gives itself away by. There is no authentication
beyond that, which is the same posture as a debug server: anything already running code on the
machine can do more than this allows.

## How the code is laid out

Three layers, and no file over about three hundred lines.

`src/PillScript/Scripting` is the model, and knows nothing about windows. A `ScriptProject` holds
the files and mirrors them to a folder; `ScriptProject.Disk.cs` is that mirror and
`ProjectTemplates` is what a new script starts as. `PackageResolver` turns what the csproj
references into assembly paths, with `DotnetSdk` running the SDK and `RestoreAssets` reading what
restore left behind. `ScriptCompiler`, `ScriptSignature`, `ScriptRunner` and `ScriptLoadContext`
build and run; `ScriptLanguageService` answers the editor between builds, formatting its answers
through `LanguageFormats`.

`src/PillScript/Editor` is the view and the view model. `EditorViewModel` is everything the
editor can do to a component, written without a single UI type. `EditorBridge` carries messages
and decides which half each belongs to, `EditorPayloads` holds the wire format and `EditorRequest`
parses what arrives. `ScriptEditorWindow` is left with what only a window can do, leaning on
`WindowFrame` for moving and resizing, `DockHost` for living inside Grasshopper and
`CanvasNavigator` for driving the canvas.

The page under `Editor/web` is plain modules, no build step: `app/state.js` holds what they share,
`app/bridge.js` is the only thing that talks to WebView2, and the rest take a part of the window
each. `index.html` lists them in load order. The stylesheet is split the same way under `css/`.

`src/PillScript/Components` is the Grasshopper surface, and `src/PillScript/Bridge` is the
loopback endpoint an agent reaches.

## Licence

MIT, in `LICENSE` at the root.

The `src/PillScript/Editor/web/vs` folder is the Monaco Editor 0.56.0, redistributed unchanged
under its own MIT licence, with the notice kept alongside it in that folder. Rhino, Grasshopper
and Roslyn arrive as NuGet packages and are not redistributed here.

## Known gaps

- There is no terminal. One was built and taken out again: as a process with redirected streams it
  could not do what a terminal tab promises, and everything it was for is covered better elsewhere
  in the window. Open project folder gives a real shell in one step. The code is in the history if
  a pseudo console ever cooperates.
- A breakpoint pauses the whole Grasshopper solve, and the canvas stays live while it does. Editing
  the document while a script is paused has not been thought through.
- The first completion in a session waits for Roslyn to build its view of the project, which takes
  a second or two. After that it is quick.
- Completion inserts the item's display text. Roslyn's own insertion rules, which matter for
  override and partial method completion, are not applied.
- Rename is the only refactoring wired up. The workspace would answer for the rest, and they are
  not offered.
- The editor page can be opened in a browser for work on the UI itself: `index.html` shows a
  sample, and `index.html?probe=1` stands in a fake component so the Monaco wiring can be
  exercised without Rhino.
