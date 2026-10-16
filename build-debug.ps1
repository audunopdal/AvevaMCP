[CmdletBinding()]
param(
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'

$buildArgs = @('build', (Join-Path $PSScriptRoot 'AvevaMcp.sln'), '--configuration', 'Debug')
if ($Rebuild) {
    $buildArgs += '--no-incremental'
}

& dotnet @buildArgs
exit $LASTEXITCODE
