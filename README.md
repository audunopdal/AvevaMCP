# AVEVA Unified Engineering 4.1 MCP

An MCP stdio server that exposes AVEVA Unified Engineering engineering data through a CAF add-in running inside UE. The MCP process does not load CAF assemblies; a named pipe ACL restricted to the UE user's Windows SID forwards requests to the add-in, where CAF calls run on UE's application thread.

## Prerequisites

- AVEVA Unified Engineering 4.1 installed and licensed.
- .NET 8 SDK/runtime to build and run the MCP server.
- The UE 4.1 customization/build environment and reference assemblies to build the CAF add-in. AVEVA's customization scripts set `UNIFIED_ENGINEERING_REFERENCE_PATH` and `CAF_ADDINS_PATH`.

## Build

Build both projects using the PowerShell scripts at the repository root:

```powershell
.\build-debug.ps1
.\build-release.ps1
```

In VS Code, press **Ctrl+Shift+B** to build Debug and copy the add-in using `build-debug.ps1`. For Release, run **Tasks: Run Task** from the Command Palette and select **build-release**. F5 retains its server-only build and does not deploy the add-in.

Add `-Rebuild` to force recompilation, for example `.\build-debug.ps1 -Rebuild`.
Both scripts locate the solution relative to the script, so they also work when invoked from another directory. They restore dependencies as needed and return the build's exit code on build failure.
Release builds select Release for both projects and enable optimization for the CAF add-in.

Only after the entire solution builds successfully, each script copies `AvevaMcp.AvevaMcpAddin.dll` and `AvevaMcp.AvevaMcpAddin.pdb` from its configuration's output directory to `D:\MyUE\addins`, creating the directory if needed. Copy errors fail the script. The scripts disable project post-build events so `CAF_ADDINS_PATH` cannot trigger an earlier copy before the solution finishes building. Direct builds in Visual Studio or with `dotnet build` retain the existing post-build behavior.

Stop the MCP server before building if it locks its executable, and close UE before replacing a loaded add-in. The scripts do not stop processes or restart the server.

Open `AvevaMcp.sln` in Visual Studio to work with both projects. From a UE customization environment with the AVEVA reference assemblies configured, build the entire solution with:

```powershell
dotnet build .\AvevaMcp.sln
```

When `UNIFIED_ENGINEERING_REFERENCE_PATH` is unset, the CAF add-in uses `%PUBLIC%\Documents\AVEVA\Unpack\AVEVA.UnifiedEngineering\4100.2606.2414.5053` for its AVEVA and Newtonsoft.Json references. This lets Visual Studio resolve references without being launched from a customization shell when that SDK version is installed. For another SDK location, set `UNIFIED_ENGINEERING_REFERENCE_PATH` before starting Visual Studio. Reload the project after changing its reference configuration.

The projects can also be built individually:

Build the MCP server:

```powershell
dotnet build .\src\AvevaMcp.Server\AvevaMcp.Server.csproj
```

Build the CAF add-in from a UE customization environment:

```powershell
$env:UNIFIED_ENGINEERING_REFERENCE_PATH = 'C:\Users\Public\Documents\AVEVA\Unpack\AVEVA.UnifiedEngineering\4100.2606.2414.5053'
dotnet build .\src\AvevaMcp.CafAddin\AvevaMcp.CafAddin.csproj /p:Configuration=Release
```

Set `UNIFIED_ENGINEERING_REFERENCE_PATH` to the UE 4.1 SDK directory. AVEVA's customization environment also sets `CAF_ADDINS_PATH`; if defined, the build copies the add-in there. Register `AvevaMcp.CafAddin` in the UE 4.1 module's add-in configuration using the same deployment procedure as other local CAF add-ins, then restart UE.

The CAF add-in disables Copy Local for installed AVEVA, framework, and Newtonsoft.Json assembly references; UE supplies these at runtime. The standalone MCP server still copies its NuGet runtime dependencies.

The CAF add-in declares the `win` runtime identifier so command-line restore also produces the Windows assets expected by Visual Studio's legacy .NET Framework project system. After changing runtime identifiers, run `dotnet restore .\AvevaMcp.sln` and reload the project.

## Configure the MCP client

The included `.vscode/mcp.json` configures VS Code to run the server. Open this repository as the workspace, build the server, and enable the `aveva-unified-engineering` server in VS Code's MCP configuration.

The server only connects to the named pipe when a tool is called. Start UE and load the CAF add-in first. The default pipe is `AvevaUE41Mcp`; the server and add-in must use the same pipe name.

## Tools

For implementing POHE/POLYHE mesh import, use the
[AVEVA polyhedron import skill](.github/skills/aveva-polyhedron-import/SKILL.md).
It records the managed database creation patterns found in ImportPlant and the
validation needed when adapting them to UE 4.1; it does not add a mesh MCP tool.

| Tool | Purpose |
| --- | --- |
| `get_context` | Get the current project, user, and MDB names. |
| `list_sites` | List all SITE elements under WORLD (`/*`) in the active MDB, returning paths and types without a result cap. |
| `get_element` | Read an element's path and type. |
| `list_children` | List direct children of an element (maximum 500). |
| `search_elements` | Search descendant element names below a path (maximum 500 results). |
| `read_attribute` | Read a scalar attribute, or use `position` for POS (x/y/z) and `orientation` for ORI (axisX/axisY/axisZ vectors), in owner coordinates. |
| `set_attribute` | Set a string, integer, double, or boolean attribute. |
| `create_element` | Create an element under a parent and assign its full AVEVA name. |
| `create_rectangular_extrusion` | Build a length-by-width XY profile and extrude vertically, with `HEIG` equal to wall Z-height. Set `updateExisting=true` to update an existing four-vertex LOOP. |
| `create_primitive` | Create a positioned and oriented BOX, NBOX, CYLINDER, or CTORUS, returning actual geometry read-back. |
| `create_polyhe` | Create a closed triangular POLYHEDRON, automatically falling back to POHEDRON/POGO if POLYHE creation fails. |
| `import_revit_mesh` | Read a Revit element mesh directly from the RevitMCP add-in and create it under a new EQUIPMENT in a ZONE, in world-aligned coordinates. |
| `get_primitive_geometry` | Read a primitive's centre, local axes, and dimensions in owner coordinates. |
| `delete_element` | Delete an element only when `confirmPath` exactly matches its path. |
| `save_work` | Explicitly save current MDB work with a comment. |
| `discard_work` | Explicitly discard current MDB work; requires `DISCARD_CURRENT_WORK`. |

Changes are not automatically saved. Review edits in UE and call `save_work` explicitly. `discard_work` discards all unsaved changes in the current MDB, not only changes made through MCP.

### Creating a POLYHE mesh

`create_polyhe(parentPath, name, vertices, faces)` first attempts a named POLYHEDRON
under an existing legal owner such as EQUIPMENT, with automatic POHE fallback.
Create the EQUIPMENT under the
target ZONE first if required by database ownership rules. Existing names are
never overwritten.

Vertices are `[x,y,z]` arrays in the **parent's coordinate system**, using the
same AVEVA project length units as `create_primitive`. Faces are arrays of three
zero-based vertex indices, and every vertex must be used. Shared positions
should use shared vertex indices for closed POLYHE detection; POHE stores its
own point for each face corner. Limits are 3-10,000 vertices and 1-20,000 faces.

Example tetrahedron (100 project length units on each axis):

```json
{
  "parentPath": "/MESH-EQUIPMENT",
  "name": "/MESH-TETRAHEDRON",
  "vertices": [[0,0,0], [100,0,0], [0,100,0], [0,0,100]],
  "faces": [[0,2,1], [0,1,3], [0,3,2], [1,2,3]]
}
```

The created structure is `POLYHEDRON -> POLPTLIST -> POINT` plus
`POLFACE -> POLOOP -> LOOPTS` per triangle, referencing the shared points through
`VXREF`. The polyhedron is positioned at the first vertex with owner-aligned
axes; its points are stored relative to that origin. For Revit data, add the
exporter's shared origin and convert units/world coordinates into the parent's
frame **before** calling. No world-coordinate or unit conversion is inferred.

Preflight checks reject malformed, degenerate triangles and unused vertices.
They do not establish that arbitrary input is free of self-intersections. A
closed mesh with each edge shared exactly twice in opposite directions and
positive signed volume is attempted as POLYHE. Open, non-manifold, inward-wound,
or zero-volume surfaces go directly to POHE without inventing caps. After
POLYHE creation, AVEVA's completed `GVOL` must be finite and positive. If
POLYHE ownership, creation, setup or volume
validation fails, the command deletes any newly created POLYHE before trying
`POHEDRON -> POGON -> POINT` with the same name, placement and ordered triangles.
POHE stores three separate points per triangle and does not require a positive
database `GVOL`; its creation is not certification of a valid solid.

Malformed triangles and duplicate-name errors do not trigger fallback; open
triangular surfaces are preserved as POHE faces. A parent must legally
own the selected representation. If cleanup fails, fallback is aborted and the
affected name is explicitly reported. Failed POHE setup is also cleaned up;
if both attempts fail, both errors are reported. There is no automatic save.

The response includes `path`, `type`, `parentPath`, vertex/face counts, input
coordinate/length units, `inputVolume` in project length units cubed, measured
`volume` with AVEVA's reported `volumeUnits`, and `saved: false`.
`fallbackUsed` and `fallbackReason` explicitly report the outcome, and `type`
is the actual created database type. On POHE fallback, measured `volume` and
`volumeUnits` are null (not verified); `pointCount` is three times `faceCount`.
`vertexCount` always reports the input vertex count.

Run the dependency-free mesh validation checks with:

```powershell
dotnet run --project .\tests\AvevaMcp.Geometry.Tests\AvevaMcp.Geometry.Tests.csproj
```

These checks do not replace live UE verification of ownership, point placement,
`VXREF`, volume and visible geometry. Rebuild/reload the CAF add-in and restart
the MCP server before using the new tool.

### Importing a Revit mesh

`import_revit_mesh(revitElementId, zonePath, equipmentName?, meshName?, weldTolerance=0.1, revitProcessId?)`
moves a whole Revit element mesh into UE in one call. The MCP server reads every
`get_element_mesh` page directly from the local RevitMCP add-in pipe
(`RevitMCP-2026-<pid>`), so
triangles never pass through the chat agent. Revit 2026 must be running with the
RevitMCP add-in loaded and the model open; specify `revitProcessId` only when
several Revit processes run. Only `mesh_fallback` elements with at most 20000
triangles are accepted.

- Coordinates: `originShared + relative vertex` in mm. Revit shared
  East/North/Elevation is treated as UE world X/Y/Z in database millimetres, then
  transformed into the ZONE's frame using its `WORPOS`/`WORI` (or `POS`/`ORI` WRT world).
- Topology: corners are welded on a `weldTolerance` mm grid; degenerate and
  duplicate (including back-to-back) triangles are dropped and counted.
- Ownership: a new EQUIPMENT (default `/REVIT-<id>`, `DESC` set to the Revit ID,
  name and unique ID) is created under the ZONE at the first vertex with
  identity orientation. The mesh (default `<equipmentName>-MESH`) is created
  with the `create_polyhe` rules: POLYHEDRON for closed outward meshes, otherwise
  POHEDRON.
- Verification: the mesh world position must match the expected shared position within
  `max(1 mm, 10 × weldTolerance)`, otherwise the import is rolled back. A vertex
  world-position check is reported as `verification.pointDeviation`.
- Existing names are refused; any failure deletes the new EQUIPMENT. Changes are
  left unsaved.

The response extends the `create_polyhe` result with `equipmentPath`,
`zonePath`, `coordinateMapping`, `sourceTriangleCount`, dropped-triangle counts,
`firstVertexWorld`, `verification`, and Revit `source` metadata.

### Placing door primitives

`create_primitive` takes `parentPath`, a unique full `name`, `elementType`, centre coordinates
`x`, `y`, `z`, and positive `xLength`, `yLength`, `zLength` for boxes/cylinders. For a CYLINDER,
`xLength` and `yLength` both specify diameter and `zLength` specifies its axial height.
For BOX and NBOX these are local X/Y/Z dimensions. Optional `axisXX`, `axisXY`,
`axisXZ` and `axisYX`, `axisYY`, `axisYZ` specify orthogonal unit local X/Y axes
in owner coordinates; defaults are the owner's X and Y axes. Local Z follows
the right-hand rule. All lengths use the AVEVA project length unit.

Create the door STRU with `create_element`, then create the leaf BOX, simplified
frame BOXes, and hardware CYLINDERs beneath it. Existing rectangular extrusion
tools can instead create the frame members as EXTRUSIONs. Use CTORUS bends
for curved handle segments when the bend dimensions are known.

For CTORUS, omit `xLength`, `yLength`, `zLength` and supply `insideRadius`,
`outsideRadius`, and `sweepAngle` in degrees. Both radii are measured from the
arc centre to the tube's inner/outer surfaces, with
`outsideRadius > insideRadius > 0` and `0 < sweepAngle <= 360`.
The position is the arc centre, and the arc lies in local XY. The tube diameter
is `outsideRadius - insideRadius`; its centreline radius is their average.
For example, a 20 mm diameter tube bent on a 30 mm centreline radius uses
`insideRadius=20`, `outsideRadius=40`, `sweepAngle=90` for a quarter bend.
This example is not an inferred dimension of the Revit door handle.

Use Revit component-local bounds to calculate the centre and dimensions, and
transform the centre and axes into the AVEVA owner's coordinates. Shared Revit
coordinates cannot be passed unchanged if the AVEVA owner is translated or
rotated. Check units and coordinate alignment first. Put the opening NBOX beneath
the host wall's positive EXTRUSION, not beneath the door STRU, and convert its
placement into that extrusion's local coordinates. Its depth must span the wall.

The tool refuses existing names and invalid dimensions or axes before creating
anything. On a setup or read-back failure it deletes only the newly created
primitive; cleanup failures are reported explicitly. Use `get_primitive_geometry`
to verify placement and dimensions before saving. After deploying updated
binaries, restart UE and the MCP server to expose the new operations.

## Limitations

- A compatible UE session with the add-in loaded is required; the pipe is not a REST service and does not work when UE is closed.
- CAF operations are performed on UE's application synchronization context. Calls fail clearly if the add-in cannot capture that context.
- General geometry, references, rules, transactions, and other CAF attribute types are not exposed. `create_rectangular_extrusion` supports vertical rectangular walls with a horizontal baseline in the owner's coordinates. The profile is length by thickness in local XY; local Z points up in the owner's coordinates, and `HEIG` is the wall height, not its baseline length. Coordinates and dimensions must already be converted to the AVEVA project length unit. Existing geometry is refused unless `updateExisting=true`, and updates require exactly one four-vertex LOOP. Failed writes attempt to restore the previous geometry and report rollback failures explicitly. The extrusion operation does not create openings, curved walls, or sloping walls; a separate NBOX can represent a rectangular opening.
- No operation silently substitutes a mock result when UE is unavailable.

## References

- [AVEVA CAF API documentation](https://help.aveva.com/API/CAF/index.html#topic1.html)
- [AVEVA Unified Engineering customization framework](https://github.com/AVEVA/UnifiedEngineeringCustomizationFramework)
