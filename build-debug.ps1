[CmdletBinding()]
param(
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'

$buildArgs = @('build', (Join-Path $PSScriptRoot 'AvevaMcp.sln'), '--configuration', 'Debug', '/p:PostBuildEvent=')
if ($Rebuild) {
    $buildArgs += '--no-incremental'
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$outputDirectory = Join-Path $PSScriptRoot 'src\AvevaMcp.CafAddin\bin\Debug'
$artifacts = @(
    (Join-Path $outputDirectory 'AvevaMcp.AvevaMcpAddin.dll'),
    (Join-Path $outputDirectory 'AvevaMcp.AvevaMcpAddin.pdb')
)
foreach ($artifact in $artifacts) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Build artifact not found: $artifact"
    }
}

$destination = 'D:\MyUE\addins'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $artifacts -Destination $destination -Force
Write-Host "Copied Debug add-in DLL and PDB to $destination"
exit 0
