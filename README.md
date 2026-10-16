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

Add `-Rebuild` to force recompilation, for example `.\build-debug.ps1 -Rebuild`.
Both scripts locate the solution relative to the script, so they also work when invoked from another directory. They restore dependencies as needed and return the build's exit code.
Release builds select Release for both projects and enable optimization for the CAF add-in.

Stop the MCP server before building if it locks its executable. The scripts do not stop processes or restart the server. The existing add-in post-build copy still runs when `CAF_ADDINS_PATH` is set; unset it for a build without deployment, or close UE before replacing a loaded add-in.

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

| Tool | Purpose |
| --- | --- |
| `get_context` | Get the current project, user, and MDB names. |
| `get_element` | Read an element's path and type. |
| `list_children` | List direct children of an element (maximum 500). |
| `search_elements` | Search descendant element names below a path (maximum 500 results). |
| `read_attribute` | Read a string, integer, double, or boolean attribute. |
| `set_attribute` | Set a string, integer, double, or boolean attribute. |
| `create_element` | Create an element under a parent and assign its full AVEVA name. |
| `delete_element` | Delete an element only when `confirmPath` exactly matches its path. |
| `save_work` | Explicitly save current MDB work with a comment. |
| `discard_work` | Explicitly discard current MDB work; requires `DISCARD_CURRENT_WORK`. |

Changes are not automatically saved. Review edits in UE and call `save_work` explicitly. `discard_work` discards all unsaved changes in the current MDB, not only changes made through MCP.

## Limitations

- A compatible UE session with the add-in loaded is required; the pipe is not a REST service and does not work when UE is closed.
- CAF operations are performed on UE's application synchronization context. Calls fail clearly if the add-in cannot capture that context.
- Attribute access supports string, integer, double, and boolean values. Geometry, references, rules, transactions, and other CAF attribute types are not yet exposed.
- No operation silently substitutes a mock result when UE is unavailable.

## References

- [AVEVA CAF API documentation](https://help.aveva.com/API/CAF/index.html#topic1.html)
- [AVEVA Unified Engineering customization framework](https://github.com/AVEVA/UnifiedEngineeringCustomizationFramework)
