[CmdletBinding()]
param(
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'

$buildArgs = @('build', (Join-Path $PSScriptRoot 'AvevaMcp.sln'), '--configuration', 'Release', '/p:PostBuildEvent=')
if ($Rebuild) {
    $buildArgs += '--no-incremental'
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$outputDirectory = Join-Path $PSScriptRoot 'src\AvevaMcp.CafAddin\bin\Release'
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
Write-Host "Copied Release add-in DLL and PDB to $destination"
exit 0
