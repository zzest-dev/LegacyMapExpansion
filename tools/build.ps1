param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$installation = (Resolve-Path -LiteralPath $GameDir).Path
& python (Join-Path $PSScriptRoot 'verify_resources.py') --runtime-only
if ($LASTEXITCODE -ne 0) { throw 'Resource verification failed.' }
& dotnet build (Join-Path $repository 'LegacyMapExpansion.csproj') -c $Configuration --disable-build-servers '-p:UseSharedCompilation=false' "-p:GameDir=$installation"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
