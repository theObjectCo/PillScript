# Changelog

## 0.3.0

First public release.

### Controls in a Rhino panel

A script can override `RegisterUi` and register sliders, number and integer fields, toggles,
choices, text fields, vectors, colour swatches, layer pickers, buttons and captions. Each call
declares one control and assigns its current value to the variable passed in, so a control is
declared and read in one line. The controls appear in a Rhino panel docked with Layers and
Properties, and `Publish to panel` in the component menu puts a component there.

Several components share the panel. Each gets a heading that rolls its section up and can be dragged
to reorder it, and the arrangement, the collapsed state and the values are saved in the .gh file. A
dragged slider costs one solve after the drag instead of one per step. A button is true for the
single solve its press caused. Colour swatches open Rhino's own picker, and the layer picker lists
the document's layers as they stand when the panel draws.

A script may carry a `ui.css` to restyle the panel, and a saved stylesheet reaches the panel without
a compile or a solve.

### Component icons

`Phosphor icon` in the component menu takes any name from https://phosphoricons.com. The icon is
fetched once, cached under `%LOCALAPPDATA%\PillScript\icons`, and drawn both on the canvas and at
the head of the component's section in the panel.

### Fixes and smaller changes

- Tree inputs report the items they could not convert instead of silently dropping them, naming the
  first refusal and the branch it came from.
- CS1701 and CS1702 are suppressed, since a script author cannot act on an assembly version that
  Rhino resolved before the script was written. Nullable annotations are on with their warnings off,
  so `out Brep? brep` compiles without annotating the rest of the file.
- Eto resolves for scripts that reference it. It sits one folder above the other Rhino assemblies,
  and both locations are now searched.
- Working folders that nothing has compiled from for a week are deleted in the background as
  Grasshopper loads. The scripts themselves live in the .gh files, so only the restore is lost.
- `Publish to panel` is no longer greyed out when a component declares no controls, which used to
  trap a published component whose controls had been renamed.
- Dropdowns in the panel are drawn in the page. A native `select` opens a window that a docked panel
  loses to Rhino's focus handling.
- A press on a section heading rolls the section up again. Pointer capture started on the press and
  swallowed the click that followed.
- The panel uses JetBrains Mono, shipped in the package, so it draws with no network.

### Examples

`examples/perforated-sheet.gh` drives a sheet of holes entirely from the panel. Nothing on the
canvas feeds it.

`examples/diffusion-limited-aggregation.gh` grows a dendrite from two source files, with a spatial
grid that cuts 1500 particles down to about a fifth of a second, where the same run without it
takes minutes.

## 0.2.0 and earlier

Not released publicly. The editor, explicit compilation, the project on disk, parameters read from
the `RunScript` signature, breakpoints and the MCP bridge were built over these versions.
