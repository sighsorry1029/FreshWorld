[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$GameDir,
    [string]$ValheimDedicatedDir,
    [string]$ManagedDir,
    [string]$BepInExDir,
    [switch]$SkipBuild,
    [string]$PluginPath
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
if ($Configuration -ne 'Release') { throw 'Distribution packages require a Release build.' }
if (-not $SkipBuild) {
    $buildParameters = @{ Configuration = $Configuration }
    foreach ($propertyName in @('GameDir', 'ValheimDedicatedDir', 'ManagedDir', 'BepInExDir')) {
        $propertyValue = Get-Variable -Name $propertyName -ValueOnly
        if (-not [string]::IsNullOrWhiteSpace($propertyValue)) { $buildParameters[$propertyName] = $propertyValue }
    }
    # The Release target packages the DLL. Do not recurse or create a second archive afterward.
    & (Join-Path $PSScriptRoot 'build.ps1') @buildParameters
    return
}

if ([string]::IsNullOrWhiteSpace($PluginPath)) {
    $PluginPath = Join-Path $workspaceRoot 'FreshWorld/bin/Release/netstandard2.1/FreshWorld.dll'
}
$PluginPath = (Resolve-Path -LiteralPath $PluginPath).ProviderPath
# Read metadata without loading game dependencies. The built assembly is the version authority.
$identity = [System.Reflection.AssemblyName]::GetAssemblyName($PluginPath)
$assemblyVersion = $identity.Version
if ($identity.Name -ne 'FreshWorld' -or $assemblyVersion.Build -lt 0 -or $assemblyVersion.Revision -ne 0) {
    throw 'Expected FreshWorld.dll with a three-part release version and a zero assembly revision.'
}
$packageVersion = $assemblyVersion.ToString(3)
$fileInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($PluginPath)
$fileVersion = [version]::new($fileInfo.FileMajorPart, $fileInfo.FileMinorPart, $fileInfo.FileBuildPart, $fileInfo.FilePrivatePart)
if ($fileVersion -ne $assemblyVersion) { throw 'AssemblyVersion and AssemblyFileVersion must match ModVersion.' }

$packageRoot = Join-Path $workspaceRoot 'Thunderstore'
$manifestPath = Join-Path $packageRoot 'manifest.json'
$files = [ordered]@{
    'FreshWorld.dll' = $PluginPath
    'README.md' = (Join-Path $workspaceRoot 'README.md')
    'CHANGELOG.md' = (Join-Path $workspaceRoot 'CHANGELOG.md')
    'manifest.json' = $manifestPath
    'icon.png' = (Join-Path $packageRoot 'icon.png')
}
foreach ($sourcePath in $files.Values) {
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Package input is missing: $sourcePath" }
}
$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json
if ($manifest.name -ne $identity.Name) { throw 'The Thunderstore manifest name differs from the DLL assembly name.' }
$versionPattern = [regex]'("version_number"\s*:\s*")[^"]*(")'
if ($versionPattern.Matches($manifestText).Count -ne 1) { throw 'Expected exactly one manifest version_number.' }
if ($manifest.version_number -ne $packageVersion) {
    # Change only this value, preserving other manifest fields and their formatting.
    $manifestText = $versionPattern.Replace($manifestText, {
        param($match)
        $match.Groups[1].Value + $packageVersion + $match.Groups[2].Value
    })
    [System.IO.File]::WriteAllText($manifestPath, $manifestText, [System.Text.UTF8Encoding]::new($false))
}

$stagePath = Join-Path $packageRoot ('.package-' + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $packageRoot "FreshWorld_v$packageVersion.zip"
[System.IO.Directory]::CreateDirectory($stagePath) | Out-Null
try {
    # README has one source: copy it only to this temporary package directory.
    foreach ($relativePath in $files.Keys) {
        Copy-Item -LiteralPath $files[$relativePath] -Destination (Join-Path $stagePath $relativePath)
    }
    Compress-Archive -Path (Join-Path $stagePath '*') -DestinationPath $archivePath -Force
}
finally {
    $resolvedStagePath = (Resolve-Path -LiteralPath $stagePath).ProviderPath
    $resolvedPackageRoot = (Resolve-Path -LiteralPath $packageRoot).ProviderPath.TrimEnd([char[]]@('/', '\'))
    $packagePrefix = $resolvedPackageRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStagePath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a staging directory outside Thunderstore: $resolvedStagePath"
    }
    Remove-Item -LiteralPath $resolvedStagePath -Recurse -Force
}

Write-Host "Created $archivePath from DLL assembly version $assemblyVersion"
Write-Host 'Thunderstore package: FreshWorld.dll, README.md, CHANGELOG.md, manifest.json, icon.png.'

