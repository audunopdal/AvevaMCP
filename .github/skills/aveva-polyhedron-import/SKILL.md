---
name: aveva-polyhedron-import
description: "Use when implementing or debugging AVEVA UE/CAF mesh import, Revit triangle transfer, POHE/POHEDRON or POLYHE/POLYHEDRON geometry. Applies the verified ImportPlant shared-point/VXREF and POGON patterns, with explicit units, owner transforms, validation, rollback and unsaved-work semantics."
---

# AVEVA polyhedron import

## Purpose

Implement mesh geometry through the managed AVEVA database API, using the
ImportPlant project as evidence. Both POLYHEDRON and POHEDRON creation are
implemented in that source; an STL converter or macro executor is not a
prerequisite for these database creation patterns.

This skill records source evidence, not proof of compatibility with every UE
module. AvevaMCP now implements `create_polyhe` (CafOperations.cs) and
`import_revit_mesh` (CafOperations.RevitImport.cs plus server-side
RevitMeshSource.cs, which pages meshes directly from the RevitMCP pipe); extend
those rather than duplicating them. Consult the
[source map and API findings](./references/importplant-patterns.md) before editing.

## Workflow

1. Inspect the current CAF operation dispatcher, MCP tool definitions, project
   references, and existing geometry validation/rollback helpers. Reuse their
   conventions; do not load AVEVA assemblies into the standalone MCP server.
2. Define the mesh contract before database mutation:
   - Explicit parent, unique full name, vertices, and ordered face indices.
   - For triangles, exactly three valid indices per face.
   - Finite coordinates, nondegenerate faces, and bounded point/face counts.
   - Explicit source units, scale, origin, and coordinate system.
   - Whether open surfaces are allowed; when preservation is requested, route
     valid open/non-manifold surfaces directly to POHE rather than rejecting
     them during POLYHE closed-volume checks.
   - No overwrite, explicit cleanup, and no implicit save.
3. Validate the entire payload and resolve all required types/attributes before
   creating elements. Check parent validity and `IsCreatable` for the chosen
   geometry type. ImportPlant uses EQUIPMENT as the geometry owner under a ZONE;
   do not assume the geometry is legal directly under a ZONE.
4. Resolve coordinates once:
   - Reconstruct Revit shared coordinates from shared origin plus relative
     vertices where that is the exporter contract.
   - Convert lengths explicitly; ImportPlant's scale of one is not unit detection.
   - Transform world/shared positions into the intended owner's local frame.
   - Preserve vertex order and winding. Avoid applying origin or scale twice.
   - Inspect the actual positioning helper implementation if reusing it; matrix
     variable names alone do not establish whether inputs are local or world.
5. Choose a source-backed geometry representation:

   **POLYHEDRON (POLYHE): shared points and referenced loops**
   - Create `DbElementTypeInstance.POLYHEDRON` under a legal owner.
   - Create its `POLPTLIST` and the list's `POINT` children.
   - Keep a point-element array in the same order as the input vertex array.
   - Use POLYHE only when the mesh is closed, consistently outward-wound and
     has positive finite volume.
   - For each ordinary triangular face create
     `POLFACE -> POLOOP -> LOOPTS` under the polyhedron.
   - Assign the ordered `DbElement[]` for that loop to
     `DbAttributeInstance.VXREF` on `LOOPTS`.
   - ImportPlant checks `GVOL` with `GetDbDouble` and rejects zero volume.
     On creation/setup/GVOL failure, clean up the created element before POHE
     fallback; report both errors if fallback also fails.
   - Holes/multiple loops need explicit topology handling; do not infer their
     semantics from a triangle-only contract.

   **POHEDRON (POHE): per-polygon points**
   - Create `DbElementTypeInstance.POHEDRON` under a legal owner.
   - Create a `POGON` child for each face loop.
   - Create ordered `POINT` children beneath that `POGON`.
   - Place each point from its indexed vertex with the correct coordinate
     transform. Unlike POLYHEDRON, these points are not shared through `VXREF`.
   - For open or non-manifold input, POHE can preserve the submitted faces
     without inventing caps. Treat it as a surface representation, not proof of
     a valid closed solid.

6. On failure, delete only the hierarchy created by this request. Surface the
   original error; if cleanup fails, report both errors and the affected path.
   Do not copy ImportPlant's catch-and-return-false behavior into an MCP success
   response. If fallback was permitted, report its actual type and reason.
7. Wire the CAF operation and matching MCP tool together. Return the actual
   created path/type, geometry counts, coordinate/unit policy, verification
   results, any fallback, and unsaved status. Do not claim that an unimplemented
   tool already exists.
8. Build the smallest affected projects using their existing tooling. Verify
   geometry in a UE session; compilation alone does not validate database
   legality, topology, placement, or visibility.

## Verification checklist

- Closed tetrahedron: four vertices and four outward-facing triangular faces.
  Check the resulting hierarchy, point references, position and nonzero volume;
  compare volume against the expected value with a stated unit/tolerance.
- Open triangle: verify explicit rejection for a closed-solid contract, or the
  agreed POHE surface behavior. Never silently change representations.
- Duplicate name, invalid index, NaN/infinity, degenerate face, illegal parent,
  and configured count bounds: explicit errors with no partial hierarchy.
- Translated/rotated owner and a nonzero source origin: verify placement against
  expected world coordinates, not just stored local coordinates.
- Failed setup: no newly created geometry remains, or incomplete cleanup is
  explicitly reported.
- Confirm visibility and counts in UE; report what was actually tested.
- Leave changes unsaved. Saving real model work requires a separate explicit
  request; never discard unrelated unsaved work to clean up a test.

## Boundaries

- No GENPRI/MESH blob or undocumented attribute fabrication.
- Source evidence comes from a legacy E3D project; verify installed UE APIs and
  legal ownership rather than asserting binary/runtime compatibility.
- Keep external source and signing configuration read-only. Do not copy
  credentials, build signing commands, or source implementation bodies into this
  skill or the AvevaMCP repository.
