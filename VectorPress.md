# VectorPress
VectorPress is a C# + Avalonia application to preform simple 2.5D extrusion from .svg files.

## Feature Overview

### Core Features

_(The minimum functional pipeline that makes the app useful.)_

- [ ] **SVG Import**
    *   Load `.svg` files from disk.
    *   Parse vector shapes (paths, polygons, rects, circles, groups).
    *   Apply transforms and flatten them into final coordinates.
    *   Ignore unsupported features gracefully (filters, masks, etc.).
    *   Suggested libraries:
        *   `Svg.Skia` or `SharpVectors` for parsing
        *   fallback: XML parsing via `System.Xml`
- [ ] **Shape Extraction**
    *   Extract filled vector regions from the SVG.
    *   Convert paths to polygon outlines.
    *   Flatten Bézier curves into line segments.
    *   Respect fill rules (`nonzero`, `evenodd`) for holes.
- [ ] **Color Detection**
    *   Identify unique fill colors.
    *   Group shapes by fill color.
    *   Display color list with counts.
- [ ] **Height Assignment**
    *   Allow assigning extrusion height per color group.
    *   Heights expressed in millimeters.
- [ ] **Polygon to Mesh Conversion**
    *   Triangulate 2D polygons with holes.
    *   Extrude polygons into 3D meshes.
    *   Generate:
        *   top surface
        *   bottom surface
        *   side walls
    *   Ensure watertight manifold mesh.
- [ ] **STL Export**
    *   Export generated mesh as binary `.stl`.
    *   Use millimeter units.

### Must Have

_(Necessary for real-world usability.)_

- [ ] **Avalonia Desktop UI**
    *   Framework: **Avalonia UI**
    *   Layout structure:
        *   Left panel: object/color list
        *   Center panel: SVG preview
        *   Right panel: extrusion settings
        *   Bottom/right: 3D preview
- [ ] **3D Preview**
    *   Display generated mesh interactively.
    *   Camera controls:
        *   orbit
        *   pan
        *   zoom
    *   Recommended:
        *   `HelixToolkit.SharpDX`
        *   or OpenGL control via Avalonia
- [ ] **Object Selection**
    *   Display individual SVG objects or groups.
    *   Toggle inclusion/exclusion in final mesh.
- [ ] **2D SVG Preview**
    *   Render imported SVG in the UI.
    *   Allow selecting shapes by clicking.
    *   Highlight selected shapes.
- [ ] **Base Plate Generation**
    *   Optional flat base under all shapes.
    *   Adjustable thickness.
    *   Auto-size based on SVG bounds.
- [ ] **Mesh Validation**
    *   Ensure exported mesh:
        *   closed manifold
        *   no inverted normals
        *   no degenerate triangles.
- [ ] **Unit Handling**
    *   Interpret SVG units properly.
    *   Convert to millimeters.
- [ ] **Scaling**
    *   Global scaling control before export.
    *   Option to switch to customary units
- [ ] **Error Handling**
    *   Clear warnings for unsupported SVG features:
        *   gradients
        *   masks
        *   strokes
        *   filters.

### Nice to Have

_(Improves workflow significantly.)_

- [ ] **Stroke to Outline Conversion**
    *   Convert stroked paths into filled shapes.
    *   Respect stroke width, joins, and caps.
- [ ] **Color-Based Defaults**
    *   Auto-assign heights to colors.
    *   Example: darker colors = taller.
- [ ] **Layer View**
    *   Display SVG group hierarchy.
    *   Allow per-layer extrusion settings.
- [ ] **Boolean Geometry Merge**
    *   Merge touching shapes into one mesh.
    *   Prevent internal faces.
- [ ] **Edge Rounding**
    *   Optional fillet or bevel on extrusion edges.
- [ ] **Mesh Simplification**
    *   Reduce triangle count when flattening curves.
- [ ] **Export Multiple Objects**
    *   Recognize separate objects
    *   Export shapes as separate meshes in a single file.
- [ ] **3MF Export**
    *   Generate `.3mf` files.
    *   Include units and object metadata.
    *   Implementation:
        *   ZIP package
        *   XML model definitions.
- [ ] **Preset Profiles**
    *   Save/load extrusion configurations.
- [ ] **Auto Base Margin**
    *   Automatically add border margin around design.

### Maybe Later

_(More advanced or specialized capabilities.)_

- [ ] **Text Support**
    *   Detect text elements.
    *   Convert to outlines automatically.
- [ ] **Advanced SVG Support**
    *   clipping paths
    *   transparency
    *   gradients converted to layers.
- [ ] **Height Maps**
    *   Map grayscale values to extrusion height.
- [ ] **Parametric Settings**
    *   rule-based height generation
    *   color → height functions.
- [ ] **Cookie Cutter Mode**
    *   Convert outlines into cutter walls.
- [ ] **Badge / Sign Generator**
    *   Auto generate backing plate + raised text.
- [ ] **Batch Processing**
    *   CLI tool for converting many SVGs.
- [ ] **Plugin System**
    *   Allow custom extrusion logic.
- [ ] **Export glTF**
    *   Useful for previewing in external viewers.
- [ ] **Slicer Integration**
    *   Send model directly to slicers (PrusaSlicer, etc).

## Architecture

The VectorPress is managed by git, and should use practices conducive to making it easy to track and edit files like breaking many things up into parts and using multiple files. 

A main design philosophy is to be lean and fast, you as fast and memory efficient code as possible. Prioritize optimization over readability, relying on comments when necessary. Write code with cross-platform deployment in mind.

### Core Library

Project:  
`VectorPress.Core`

Responsibilities:

*   SVG parsing
*   geometry processing
*   triangulation
*   mesh generation
*   export writers

No UI dependencies.

### UI Application

Project:  
`VectorPress.App`

Framework:

*   **Avalonia**

Responsibilities:

*   UI controls
*   preview rendering
*   user interaction
*   configuration.

### Geometry Layer

Possible libraries:

*   `NetTopologySuite` (robust polygon operations)
*   `Triangle.NET` (triangulation)
*   `Clipper2` (polygon boolean operations)

### 3D Rendering

Options:

*   **HelixToolkit.SharpDX**
*   or **OpenTK + Avalonia control**

HelixToolkit is easiest.

### Minimal Data Model

Example structures:

```plain
SvgShape
{
    List<Vector2> OuterRing
    List<List<Vector2>> Holes
    Color Fill
    string Id
}
```

```plain
ExtrusionSettings
{
    float Height
    bool Enabled
}
```

```plain
Mesh
{
    List<Vector3> Vertices
    List<int> Indices
}
```

### CLI Interface (future but easy)

```plain
vectorpress input.svg output.stl
vectorpress input.svg output.stl --base 2mm
vectorpress input.svg output.stl --height "#FF0000=3mm"
```
## UI Appearance
The UI should use a modern feeling interface with a light and dark mode. 
Gradients and glows should be considered, and most everything should have a border radius.

## Other Instructions

Use tabs, not spaces, and follow the style of the codebase. Use as many separate files as possible, and clearly document code and settings. Make the codebase easy for another AI or human to quickly find and edit things. Place constants and settings at the top of files, and start each file with a commended description of what it does.