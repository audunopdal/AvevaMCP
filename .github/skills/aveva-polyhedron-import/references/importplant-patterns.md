# ImportPlant source evidence

Inspected source: `ISYPlantImportPlant-3.1`, entered through
`ISY.Plant.Aveva.ImportPlant.App\ISY.Plant.Aveva.ImportPlant.App.csproj`.
The source was inspected read-only, not built or executed for this skill.

Local checkout used during inspection:
`C:\Users\aop\source\repos\plant\Aveva\ISYPlantImportPlant-3.1`.
This path is a navigation aid, not a required dependency or portable build path.
If unavailable, obtain the corresponding checkout rather than assuming these
findings verify another version.

## Navigation

Paths below are relative to the source checkout; line numbers are navigation
hints for the inspected version.

| Source | Relevant entry point / evidence |
| --- | --- |
| `ISY.Plant.Aveva.ImportPlant.App\ImportPlant.cs`, lines 13-61 | `ImportPlant : IAddin`; creates the GUI main control. |
| `ISY.Plant.Aveva.ImportPlant.GUI\MainControl.cs`, lines 220-230 | Dispatches import through `Specific.Import.StartImport`. |
| `ISY.Plant.Aveva.ImportPlant.Specific\Import.cs`, lines 37-158 | XML validation/deserialization, piping/equipment import, connection work and in-memory cleanup. |
| `ISY.Plant.Aveva.ImportPlant.Specific\Equipment.cs`, lines 186-326 | Resolves zone, creates EQUIPMENT, applies placement and dispatches primitive geometry. |
| `ISY.Plant.Aveva.ImportPlant.Specific\Geometry.cs`, lines 74-85 | Dispatches `XMLSchema.FacetRep` to POLYHE first, with POHE fallback. |
| `ISY.Plant.Aveva.ImportPlant.Specific\Geometry.cs`, lines 119-168 | `CreateGeometryPOLYHE`: point list, faces, loops, `VXREF`, volume check and failure deletion. |
| `ISY.Plant.Aveva.ImportPlant.Specific\Geometry.cs`, lines 170-201 | `CreateGeometryPOHE`: polygons and point children, with failure deletion/logging. |

## Verified creation patterns

The managed API pattern uses `DbElement.CreateLast`, with these
`DbElementTypeInstance` constants:

```text
EQUIPMENT
  POLYHEDRON
    POLPTLIST
      POINT ...
    POLFACE ...
      POLOOP
        LOOPTS  -- VXREF references the ordered POINT elements
```

`CreateGeometryPOLYHE` constructs a `DbElement[]` from the input loop's indices
and assigns it through `SetAttribute(DbAttributeInstance.VXREF, points)` on
`LOOPTS`. Indices access the vertex array directly (zero-based). It reads
`GetDbDouble(DbAttributeInstance.GVOL)` and deletes/rejects zero-volume geometry.
It also deletes on exceptions and reports failure to its caller.

The alternative pattern is:

```text
EQUIPMENT
  POHEDRON
    POGON ...
      POINT ...
```

`CreateGeometryPOHE` creates a POGON for each input face loop and creates its
own ordered POINT children from the indexed coordinates. The source does not
special-case triangles; a triangular input loop supplies three point entries.

Use the exact managed constants, not guessed short-name aliases:
`POLYHEDRON` and `POHEDRON` are distinct types. POLYHE/POHE are the names used
by the source methods and AVEVA terminology, not interchangeable topology.

## Placement and ownership

- `Equipment.ImportEquipment` checks
  `zone.IsCreatable(DbElementTypeInstance.EQUIPMENT)` before creation.
- Geometry methods check the owner is valid and can create the requested type.
- Equipment name derives from configured prefix/suffix and tag, removing spaces.
  Existing equipment is skipped with a warning.
- Equipment and facet placement use
  `UtilElement.SetElementPositionWrtOwner`; world matrices are obtained with
  `Attribute.GetElementMatrixWorld`.
- Facet origins and vertex coordinates are passed through
  `NonSpecific.UtilMath.Scale` and the positioning helpers.
- The helper bodies were not established in this extraction. Inspect their
  implementations before translating their calls into explicit matrix math.
  Do not infer exact transform direction from the helper names.

## Limitations to correct when adapting

- `Import.StartImport` sets the length scale to one. It reads the active
  `UNITS` expression but does not derive a conversion from it.
- POLYHE failures return false and trigger POHE fallback. An MCP operation must
  distinguish validation errors, topology failure, and cleanup failure, and
  must report a fallback instead of masking it.
- The source's face/loop construction and volume-check placement should not be
  copied mechanically. Validate completed topology; verify multiple-loop/hole
  handling separately from a triangle-only importer.
- Equipment-level exceptions are logged/recorded, not necessarily rethrown.
  No transaction-wide rollback was found in the traced path.
- No explicit database save was found in the traced import flow. That finding
  is not a guarantee about unrelated application paths.
- The facet dispatch does not update the last positive primitive returned to
  subsequent negative-primitive processing. Do not assume Boolean host
  association is handled for these representations.

## Compatibility

The entry project targets .NET Framework 4.8 and references legacy E3D 3.1
installation paths. AvevaMCP's CAF project targets .NET Framework 4.8.1 with
C# 7.3 and UE 4.1 references.

The relevant API namespaces are `Aveva.Core.Database` and
`Aveva.Core.Geometry`. Resolve the listed constants, `VXREF`, `GVOL`, array
attribute overloads and positioning operations against the installed SDK, then
test creation in UE. Source inspection establishes a concrete managed API
pattern, not runtime verification in this workspace.
