[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$GameDir,
    [string]$ValheimDedicatedDir,
    [string]$ManagedDir,
    [string]$BepInExDir
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$buildArguments = @('build', (Join-Path $workspaceRoot 'FreshWorld.sln'), '-c', $Configuration, '--nologo')
foreach ($propertyName in @('GameDir', 'ValheimDedicatedDir', 'ManagedDir', 'BepInExDir')) {
    $propertyValue = Get-Variable -Name $propertyName -ValueOnly
    if (-not [string]::IsNullOrWhiteSpace($propertyValue)) {
        $buildArguments += "-p:${propertyName}=$propertyValue"
    }
}

& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

foreach ($testProject in @('FreshWorld.Core.Tests/FreshWorld.Core.Tests.csproj', 'FreshWorld.Runtime.Tests/FreshWorld.Runtime.Tests.csproj', 'FreshWorld.Backend.Tests/FreshWorld.Backend.Tests.csproj', 'FreshWorld.Terrain.Tests/FreshWorld.Terrain.Tests.csproj', 'FreshWorld.Harmony.Tests/FreshWorld.Harmony.Tests.csproj', 'FreshWorld.World.Tests/FreshWorld.World.Tests.csproj', 'FreshWorld.Generation.Tests/FreshWorld.Generation.Tests.csproj', 'FreshWorld.Command.Tests/FreshWorld.Command.Tests.csproj', 'FreshWorld.Plugin.Tests/FreshWorld.Plugin.Tests.csproj', 'FreshWorld.Save.Tests/FreshWorld.Save.Tests.csproj')) {
    & dotnet run --project (Join-Path $workspaceRoot $testProject) -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "$testProject failed with exit code $LASTEXITCODE." }
}

Write-Host "Build and all test harnesses passed. Plugin: FreshWorld/bin/$Configuration/netstandard2.1/FreshWorld.dll"

