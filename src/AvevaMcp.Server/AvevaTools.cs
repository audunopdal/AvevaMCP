using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AvevaMcp.Server;

[McpServerToolType]
public static class AvevaTools
{
    [McpServerTool(ReadOnly = true), 
     Description("Return the active UE project, user, and MDB. Requires a running UE session with the CAF add-in loaded.")]
    public static Task<string> get_context(CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("get_context", new { }, cancellationToken);

    [McpServerTool(ReadOnly = true), 
      Description("List all SITE elements under WORLD (/*) in the active MDB. Returns each site's path and type without a result limit.")]
    public static Task<string> list_sites(CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("list_sites", new { }, cancellationToken);

    [McpServerTool(ReadOnly = true), 
     Description("Get the full name and element type for an AVEVA element path.")]
    public static Task<string> get_element(
        [Description("Full AVEVA element path, for example /* or /SITE/ZONE/EQUIPMENT.")] string path,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("get_element", new { path }, cancellationToken);

    [McpServerTool(ReadOnly = true), 
     Description("List direct children of an AVEVA element. The maximum page size is 500.")]
    public static Task<string> list_children(
        [Description("Full AVEVA parent element path.")] string path,
        [Description("Maximum number of children to return (1-500).")] int limit = 100,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("list_children", new { path, limit }, cancellationToken);

    [McpServerTool(ReadOnly = true), 
     Description("Search element names below a root element path. Results are capped at 500.")]
    public static Task<string> search_elements(
        [Description("Full AVEVA root path to search below.")] string rootPath,
        [Description("Case-insensitive text to match in element names.")] string nameContains,
        [Description("Maximum number of results (1-500).")] int limit = 100,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("search_elements", new { rootPath, nameContains, limit }, cancellationToken);

    [McpServerTool(ReadOnly = true), 
     Description("Read an AVEVA scalar, position, or orientation attribute. Position returns x/y/z; orientation returns axisX/axisY/axisZ vectors. Geometry values use owner coordinates.")]
    public static Task<string> read_attribute(
        [Description("Full AVEVA element path.")] string path,
        [Description("Attribute name such as DESC, REV, PRES, or BUIL.")] string attribute,
        [Description("One of: string, integer, double, boolean, position, orientation. For geometry use POS/position or ORI/orientation.")] string valueType,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("read_attribute", new { path, attribute, valueType }, cancellationToken);

    [McpServerTool, Description("Set a string, integer, double, or boolean AVEVA attribute. This changes the active work but does not save it.")]
    public static Task<string> set_attribute(
        [Description("Full AVEVA element path.")] string path,
        [Description("Attribute name such as DESC, REV, PRES, or BUIL.")] string attribute,
        [Description("One of: string, integer, double, boolean.")] string valueType,
        [Description("Attribute value, encoded as text (for example 14.5 or true).")] string value,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("set_attribute", new { path, attribute, valueType, value }, cancellationToken);

    [McpServerTool, Description("Create a UE database element under a parent and assign its full element name. This changes active work but does not save it.")]
    public static Task<string> create_element(
        [Description("Full parent element path.")] string parentPath,
        [Description("UE element type name, such as EQUIPMENT, PIPE, or BRANCH.")] string elementType,
        [Description("Full AVEVA element name, normally an absolute path beginning with /.")] string name,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("create_element", new { parentPath, elementType, name }, cancellationToken);

    [McpServerTool, Description("Create rectangular wall geometry in an EXTRUSION: the length-by-width profile is in local XY and HEIG is always the vertical Z-height. Start/end points are the horizontal wall centreline at its base in the owner's coordinate system; dimensions use the AVEVA project length unit. This changes active work but does not save it.")]
    public static Task<string> create_rectangular_extrusion(
        [Description("Full path to an existing EXTRUSION element.")] string path,
        [Description("Start point X coordinate at the wall base.")] double startX,
        [Description("Start point Y coordinate at the wall base.")] double startY,
        [Description("Start point Z coordinate at the wall base.")] double startZ,
        [Description("End point X coordinate at the wall base.")] double endX,
        [Description("End point Y coordinate at the wall base.")] double endY,
        [Description("End point Z coordinate at the wall base. Must match startZ for a vertical wall.")] double endZ,
        [Description("Wall thickness, centered about the start-to-end baseline.")] double width,
        [Description("Vertical Z-height above the baseline; written to HEIG.")] double height,
        [Description("Explicitly update an existing four-vertex LOOP; false refuses existing geometry.")] bool updateExisting = false,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync(
            "create_rectangular_extrusion",
            new { path, startX, startY, startZ, endX, endY, endZ, width, height, updateExisting },
            cancellationToken);

    [McpServerTool, Description("Create and place BOX, NBOX, CYLINDER, or CTORUS with orthogonal local X/Y axes in owner coordinates. Position is the primitive centre, or arc centre for CTORUS. Lengths use the AVEVA project length unit. BOX/NBOX require xLength/yLength/zLength; CYLINDER requires equal xLength/yLength diameters and zLength axial length; CTORUS instead requires insideRadius/outsideRadius/sweepAngle (degrees), with its arc in local XY. NBOX must be owned by the positive primitive it cuts. Does not save; removes the new element if setup fails.")]
    public static Task<string> create_primitive(
        [Description("Full parent path. For NBOX, use the host positive primitive, not a sibling structure.")] string parentPath,
        [Description("Full name of the new primitive. Existing names are never overwritten.")] string name,
        [Description("BOX, NBOX, CYLINDER, or CTORUS.")] string elementType,
        [Description("Centre X in owner coordinates.")] double x,
        [Description("Centre Y in owner coordinates.")] double y,
        [Description("Centre Z in owner coordinates.")] double z,
        [Description("Required local X length, or cylinder diameter. Omit for CTORUS.")] double? xLength = null,
        [Description("Required local Y length, or cylinder diameter. Omit for CTORUS.")] double? yLength = null,
        [Description("Required local Z length, or cylinder axial length. Omit for CTORUS.")] double? zLength = null,
        [Description("Local X axis direction X component in owner coordinates.")] double axisXX = 1,
        [Description("Local X axis direction Y component in owner coordinates.")] double axisXY = 0,
        [Description("Local X axis direction Z component in owner coordinates.")] double axisXZ = 0,
        [Description("Local Y axis direction X component in owner coordinates.")] double axisYX = 0,
        [Description("Local Y axis direction Y component in owner coordinates.")] double axisYY = 1,
        [Description("Local Y axis direction Z component in owner coordinates.")] double axisYZ = 0,
        [Description("CTORUS inside radius from arc centre to inner surface; must be positive.")] double? insideRadius = null,
        [Description("CTORUS outside radius from arc centre to outer surface; greater than insideRadius.")] double? outsideRadius = null,
        [Description("CTORUS sweep angle in degrees, greater than 0 and at most 360.")] double? sweepAngle = null,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("create_primitive",
            new { parentPath, name, elementType, x, y, z, xLength, yLength, zLength,
                axisXX, axisXY, axisXZ, axisYX, axisYY, axisYZ,
                insideRadius, outsideRadius, sweepAngle }, cancellationToken);

    [McpServerTool, Description("Create a closed triangular POLYHEDRON mesh, automatically falling back to POHEDRON/POGO if UE rejects POLYHE creation or volume. Reports actual type, fallbackUsed and fallbackReason. Coordinates are in the parent's frame and AVEVA project length units. Requires outward, consistently wound closed faces, even for fallback. No overwrite. Cleans up failed geometry before fallback; aborts if cleanup fails. Leaves changes unsaved.")]
    public static Task<string> create_polyhe(
        [Description("Existing legal POLYHEDRON or POHEDRON owner, such as EQUIPMENT. A ZONE may require an EQUIPMENT child first.")] string parentPath,
        [Description("Unique full name beginning with /.")] string name,
        [Description("3-10000 vertices, each an [x,y,z] array in parent coordinates. Add any Revit shared origin and convert units/coordinates before calling.")] double[][] vertices,
        [Description("1-20000 triangular faces, each containing three zero-based vertex indices. Every vertex must be used. Closed, outward-wound meshes use POLYHE; valid open/non-manifold surfaces use POHE.")] int[][] faces,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("create_polyhe", new { parentPath, name, vertices, faces }, cancellationToken);

    [McpServerTool, Description("Import a Revit element's mesh into UE. Reads all get_element_mesh pages directly from the local RevitMCP add-in (Revit must be running with a model open), reconstructs shared coordinates (originShared + relative, mm), welds vertices, and treats Revit shared E/N/Elev as UE world X/Y/Z, converting into the ZONE's frame. Creates a new EQUIPMENT under zonePath, positioned at the first vertex, owning a POLYHEDRON for closed outward meshes or a POHEDRON surface otherwise. Verifies the mesh world position. No overwrite; removes the new EQUIPMENT on failure. Leaves changes unsaved.")]
    public static async Task<string> import_revit_mesh(
        [Description("Positive 64-bit Revit element ID with mesh_fallback status.")] long revitElementId,
        [Description("Existing ZONE path that will own the new EQUIPMENT.")] string zonePath,
        [Description("Full name for the new EQUIPMENT. Defaults to /REVIT-<revitElementId>.")] string? equipmentName = null,
        [Description("Full name for the mesh element. Defaults to <equipmentName>-MESH.")] string? meshName = null,
        [Description("Vertex weld tolerance in mm, greater than 0 and at most 10.")] double weldTolerance = 0.1,
        [Description("Revit PID; only needed when several Revit processes are running.")] int? revitProcessId = null,
        CancellationToken cancellationToken = default)
    {
        equipmentName ??= $"/REVIT-{revitElementId}";
        meshName ??= equipmentName + "-MESH";
        try
        {
            return await ImportRevitMeshCore(revitElementId, zonePath, equipmentName, meshName, weldTolerance,
                revitProcessId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not McpException)
        {
            throw new McpException(exception.Message, exception);
        }
    }

    private static async Task<string> ImportRevitMeshCore(long revitElementId, string zonePath, string equipmentName,
        string meshName, double weldTolerance, int? revitProcessId, CancellationToken cancellationToken)
    {
        RevitMesh mesh = await RevitMeshSource.ReadAsync(revitElementId, revitProcessId, cancellationToken);
        var source = new
        {
            revitElementId = mesh.ElementId,
            uniqueId = mesh.UniqueId,
            name = mesh.Name,
            category = mesh.Category,
            originShared = mesh.OriginShared,
            materialCount = mesh.MaterialCount
        };
        string description = $"Revit {mesh.ElementId} {mesh.Name} ({mesh.UniqueId})";
        if (description.Length > 120)
            description = description[..120];
        return await BridgeClient.CallAsync("import_revit_mesh", new
        {
            zonePath,
            equipmentName,
            meshName,
            weldTolerance,
            triangles = mesh.Triangles,
            description,
            source
        }, cancellationToken);
    }

    [McpServerTool(ReadOnly = true), Description("Read actual position, local X/Y/Z axes and dimensions of BOX, NBOX, CYLINDER, or CTORUS in owner coordinates. CTORUS reports insideRadius, outsideRadius, sweepAngle; others report xLength/yLength/zLength.")]
    public static Task<string> get_primitive_geometry(
        [Description("Full primitive path.")] string path,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("get_primitive_geometry", new { path }, cancellationToken);

    [McpServerTool, Description("Delete an element only when confirmPath exactly matches path. This changes active work but does not save it.")]
    public static Task<string> delete_element(
        [Description("Full AVEVA element path to delete. Root elements cannot be deleted.")] string path,
        [Description("Repeat the exact path to confirm deletion.")] string confirmPath,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("delete_element", new { path, confirmPath }, cancellationToken);

    [McpServerTool, Description("Explicitly save current MDB work using the supplied audit comment.")]
    public static Task<string> save_work(
        [Description("Non-empty comment recorded with the save.")] string comment,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("save_work", new { comment }, cancellationToken);

    [McpServerTool, Description("Discard all unsaved work in the active MDB. Requires confirmation value DISCARD_CURRENT_WORK.")]
    public static Task<string> discard_work(
        [Description("Must be exactly DISCARD_CURRENT_WORK. This discards all unsaved changes in the active MDB.")] string confirmation,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("discard_work", new { confirmation }, cancellationToken);
}
