param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('Windows', 'Linux')][string]$Platform = 'Windows',
    [string]$ManagedDir
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$installation = (Resolve-Path -LiteralPath $GameDir).Path
& python (Join-Path $PSScriptRoot 'verify_resources.py') --runtime-only --platform $Platform
if ($LASTEXITCODE -ne 0) { throw 'Resource verification failed.' }
$buildArguments = @('build', (Join-Path $repository 'LegacyMapExpansion.csproj'), '-c', $Configuration, '--disable-build-servers', '-p:UseSharedCompilation=false', "-p:GameDir=$installation", "-p:MapPlatform=$Platform")
if ($ManagedDir) { $buildArguments += "-p:ManagedDir=$((Resolve-Path -LiteralPath $ManagedDir).Path)" }
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
