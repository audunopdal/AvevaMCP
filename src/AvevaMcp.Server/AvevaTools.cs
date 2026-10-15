using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AvevaMcp.Server;

[McpServerToolType]
public static class AvevaTools
{
    [McpServerTool, Description("Return the active UE project, user, and MDB. Requires a running UE session with the CAF add-in loaded.")]
    public static Task<string> get_context(CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("get_context", new { }, cancellationToken);

    [McpServerTool, Description("Get the full name and element type for an AVEVA element path.")]
    public static Task<string> get_element(
        [Description("Full AVEVA element path, for example /* or /SITE/ZONE/EQUIPMENT.")] string path,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("get_element", new { path }, cancellationToken);

    [McpServerTool, Description("List direct children of an AVEVA element. The maximum page size is 500.")]
    public static Task<string> list_children(
        [Description("Full AVEVA parent element path.")] string path,
        [Description("Maximum number of children to return (1-500).")] int limit = 100,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("list_children", new { path, limit }, cancellationToken);

    [McpServerTool, Description("Search element names below a root element path. Results are capped at 500.")]
    public static Task<string> search_elements(
        [Description("Full AVEVA root path to search below.")] string rootPath,
        [Description("Case-insensitive text to match in element names.")] string nameContains,
        [Description("Maximum number of results (1-500).")] int limit = 100,
        CancellationToken cancellationToken = default) =>
        BridgeClient.CallAsync("search_elements", new { rootPath, nameContains, limit }, cancellationToken);

    [McpServerTool, Description("Read a string, integer, double, or boolean AVEVA attribute.")]
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
