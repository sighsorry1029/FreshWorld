param(
    [Parameter(Mandatory)][string]$ManagedDir,
    [Parameter(Mandatory)][string]$BepInExDir,
    [Parameter(Mandatory)][string]$PluginPath,
    [Parameter(Mandatory)][string]$ReportPath
)

# Read original metadata only. This does not load Unity, patch the game, or validate gameplay.
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $BepInExDir 'Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Resolve-Path $ManagedDir).Path)
$resolver.AddSearchDirectory((Resolve-Path $BepInExDir).Path)
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$plugin = [Mono.Cecil.ModuleDefinition]::ReadModule((Resolve-Path $PluginPath).Path, $parameters)
$game = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $ManagedDir 'assembly_valheim.dll'), $parameters)
$utils = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $ManagedDir 'assembly_utils.dll'), $parameters)
$soft = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $ManagedDir 'SoftReferenceableAssets.dll'), $parameters)
$checks = [Collections.Generic.List[object]]::new()
function Require-Field($type, [string]$name, [string]$expected) {
    $field = @($type.Fields | Where-Object Name -EQ $name)
    if ($field.Count -ne 1 -or $field[0].FieldType.FullName -ne $expected) {
        throw "Field contract mismatch: $($type.FullName)::$name ($expected)"
    }
    $checks.Add(@{ kind = 'field'; name = $field[0].FullName; attributes = "$($field[0].Attributes)" })
}
function Require-Method($type, [string]$name, [string]$arguments, [string]$returns) {
    $method = @($type.Methods | Where-Object {
        $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $arguments -and $_.ReturnType.FullName -eq $returns
    })
    if ($method.Count -ne 1) { throw "Method contract mismatch: $($type.FullName)::$name($arguments) -> $returns" }
    $checks.Add(@{ kind = 'method'; name = $method[0].FullName; attributes = "$($method[0].Attributes)" })
}
try {
    $zone = $game.GetType('ZoneSystem')
    $zdo = $game.GetType('ZDOMan')
    Require-Field $zone 'm_generatedZones' 'System.Collections.Generic.HashSet`1<Vector2s>'
    Require-Field $zone 'm_zones' 'System.Collections.Generic.Dictionary`2<Vector2s,ZoneSystem/ZoneData>'
    Require-Field $zone 'm_tempSpawnedObjects' 'System.Collections.Generic.List`1<UnityEngine.GameObject>'
    Require-Field ($game.GetType('ZoneSystem/ZoneData')) 'm_root' 'UnityEngine.GameObject'
    Require-Field $zdo 'm_objectsByID' 'System.Collections.Generic.Dictionary`2<ZDOID,ZDO>'
    Require-Field $zdo 'm_destroySendList' 'System.Collections.Generic.List`1<ZDOID>'
    # Empty-chunk save repair uses Harmony field injection and cached reflection for SaveData.
    Require-Field $zdo 'm_chunkSaveMapping' 'ChunkSaveMapping'
    Require-Field $zdo 'm_dirtyChunks' 'System.Collections.Generic.HashSet`1<ZoneSystem/ChunkIndex>[]'
    Require-Field $zdo 'm_objectsBySector' 'System.Collections.Generic.List`1<ZDO>[]'
    Require-Field $zdo 'm_saveData' 'ZDOMan/SaveData'
    Require-Field ($game.GetType('ZDOMan/SaveData')) 'm_numFiles' 'System.Int32'
    Require-Field ($game.GetType('ZDOMan/SaveData')) 'm_dirtyFiles' 'System.Int32'
    Require-Field ($game.GetType('ZNetScene')) 'm_instances' 'System.Collections.Generic.Dictionary`2<ZDO,ZNetView>'
    Require-Field ($game.GetType('Heightmap')) 's_heightmaps' 'System.Collections.Generic.List`1<Heightmap>'
    Require-Field ($game.GetType('Heightmap')) 'm_buildData' 'HeightmapBuilder/HMBuildData'
    Require-Field ($game.GetType('ZNetView')) 'm_ghostInit' 'System.Boolean'
    Require-Field ($game.GetType('WearNTear')) 'm_randomInitialDamage' 'System.Boolean'
    Require-Field ($game.GetType('Terminal')) 'commands' 'System.Collections.Generic.Dictionary`2<System.String,Terminal/ConsoleCommand>'
    Require-Field ($game.GetType('Terminal')) 'm_commandList' 'System.Collections.Generic.List`1<System.String>'
    Require-Field ($soft.GetType('SoftReferenceableAssets.SoftReference`1')) 'm_name' 'System.String'
    Require-Field ($utils.GetType('Vector2s')) 'x' 'System.Int16'
    Require-Field ($utils.GetType('Vector2s')) 'y' 'System.Int16'
    Require-Method $zone 'PokeLocalZone' 'Vector2s' 'System.Boolean'
    Require-Method ($game.GetType('ZoneSystem/ClearArea')) '.ctor' 'UnityEngine.Vector3,System.Single' 'System.Void'
    foreach ($name in @('PlaceVegetation', 'PlaceLocations')) {
        Require-Method $zone $name 'Vector2s,UnityEngine.Vector3,UnityEngine.Transform,Heightmap,System.Collections.Generic.List`1<ZoneSystem/ClearArea>,ZoneSystem/SpawnMode,System.Collections.Generic.List`1<UnityEngine.GameObject>' 'System.Void'
    }
    Require-Method $zone 'PokeCanSpawnLocation' 'ZoneSystem/ZoneLocation,System.Boolean' 'System.Boolean'
    Require-Method ($game.GetType('Minimap')) 'UpdateLocationPins' 'System.Single' 'System.Void'
    Require-Method $zdo 'FindSectorObjects' 'Vector2s,SimulationDistance,System.Collections.Generic.List`1<ZDO>,System.Collections.Generic.List`1<ZDO>' 'System.Void'
    Require-Method ($game.GetType('Heightmap')) 'Poke' 'System.Int32,System.Boolean' 'System.Void'
    Require-Method ($game.GetType('Terminal/ConsoleCommand')) '.ctor' 'System.String,System.String,Terminal/ConsoleEvent,System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.Boolean,Terminal/ConsoleOptionsFetcher,System.Boolean,System.Boolean,System.Boolean' 'System.Void'
    # Every native Harmony target in the plugin, including non-public methods.
    Require-Method $zdo 'SendDestroyed' '' 'System.Void'
    Require-Method $zdo 'GetSaveClonePerChunk' '' 'System.Collections.Generic.List`1<System.Tuple`2<ZoneSystem/ChunkIndex,System.Collections.Generic.List`1<ZDO>>>'
    Require-Method ($game.GetType('ZNet')) 'InternalCommand' 'ZRpc,System.String' 'System.Void'
    Require-Method ($game.GetType('ZNet')) 'Shutdown' 'System.Boolean' 'System.Void'
    Require-Method ($game.GetType('ZNet')) 'ShutdownWithoutSave' 'System.Boolean' 'System.Void'
    Require-Method $zone 'GetGroundData' 'UnityEngine.Vector3&,UnityEngine.Vector3&,Heightmap/Biome&,Heightmap/BiomeArea&,Heightmap&' 'System.Void'
    Require-Method $zone 'GetGroundHeight' 'UnityEngine.Vector3' 'System.Single'
    $everybody = $game.GetType('ZRoutedRpc').Fields | Where-Object Name -EQ 'Everybody'
    if (!$everybody.IsLiteral -or $everybody.Constant -ne 0) { throw 'Unexpected broadcast destination contract.' }

    $references = @($plugin.GetMemberReferences() | Where-Object {
        $_.DeclaringType.GetElementType().Scope.Name -in @('assembly_valheim', 'assembly_utils', 'SoftReferenceableAssets')
    })
    foreach ($reference in $references) {
        if ($null -eq $reference.Resolve()) { throw "Unresolved compiled game member: $($reference.FullName)" }
    }
    $constructor = @($references | Where-Object { $_.Name -eq '.ctor' -and $_.DeclaringType.FullName -eq 'Terminal/ConsoleCommand' })
    if ($constructor.Count -ne 1 -or $constructor[0].Parameters.Count -ne 13) { throw 'Plugin does not call the 1.0.7 console constructor.' }
    $forbidden = @($references | Where-Object {
        $_.FullName -match 'm_objectsByOutsideSector|ZDOMan::SectorToIndex|ZRoutedRpc::Everybody|Vector2i'
    })
    if ($forbidden.Count) { throw "Obsolete compiled references: $($forbidden.FullName -join '; ')" }
    $report = [ordered]@{
        verifiedUtc = [DateTime]::UtcNow.ToString('o')
        kind = 'Original metadata and compiled member resolution; not game execution'
        managedDir = (Resolve-Path $ManagedDir).Path
        gameSha256 = (Get-FileHash (Join-Path $ManagedDir 'assembly_valheim.dll')).Hash
        pluginSha256 = (Get-FileHash $PluginPath).Hash
        reflectedAndPatchedContracts = $checks.ToArray()
        resolvedCompiledMembers = $references.FullName
        consoleConstructor = $constructor[0].FullName
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    Write-Output "PASS $($checks.Count) native contracts; $($references.Count) compiled game members; 13-argument console constructor."
} finally {
    $plugin.Dispose(); $game.Dispose(); $utils.Dispose(); $soft.Dispose(); $resolver.Dispose()
}
