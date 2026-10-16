using System.ComponentModel;
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
     Description("Read a string, integer, double, or boolean AVEVA attribute.")]
    public static Task<string> read_attribute(
        [Description("Full AVEVA element path.")] string path,
        [Description("Attribute name such as DESC, REV, PRES, or BUIL.")] string attribute,
        [Description("One of: string, integer, double, boolean.")] string valueType,
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
