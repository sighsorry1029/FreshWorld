using System.Reflection;
using BepInEx.Configuration;
using FreshWorld.Configuration;
using FreshWorld.Engine;
using UnityEngine;

var tests = new (string Name, Action Body)[]
{
    ("sector and world enumeration return snapshots", Snapshots),
    ("native sector query includes portals and isolates shared out-of-map buckets", SectorPortals),
    ("short coordinate edges do not wrap occupied or protected zones and repair borders", CoordinateEdges),
    ("restricted generated snapshots preserve distance ties and remain independent of live sets", GeneratedSnapshots),
    ("player prefab and registered character IDs are protected", PlayerProtection),
    ("player-zone collection uses the local transform and native zone boundaries", LocalPlayerZones),
    ("connected remote character records take priority over private reference and stale map positions", RemotePlayerZones),
    ("player-zone collection appends movement and retains observations after disconnect", MovingPlayerZones),
    ("ready peers use their reference positions during loading and invalid character data", PlayerZoneFallback),
    ("unready disconnected server peers and invalid positions do not invent occupied zones", InvalidPlayerZones),
    ("player-zone collection requires an active world host", PlayerZonesRequireHost),
    ("spawned connection cycles delete each object once", SpawnedCycle),
    ("loaded object deletion uses the scene destruction path", LoadedDeletion),
    ("manual release unspawns scene objects without deleting their ZDOs", OwnedRelease),
    ("loading proxies keep their zone root until they finish", DeferredReleaseLoading),
    ("deferred cleanup never removes a replacement zone root", DeferredReleaseReplacement),
    ("a player entering a deferred zone keeps its root", DeferredPlayerArrival),
    ("old proxy destruction preserves new proxy loading in the same zone", DeferredResetLoading),
    ("naturally loading zones are never claimed or released", ExistingLoad),
    ("a player entering a manual zone keeps it loaded", PlayerEnters),
    ("zone reset updates generation/location state and all eight repair edges", ResetZoneAndBorders),
    ("partial deletion failure retains border repairs before zone state changes", PartialDeletionBorders),
    ("terrain recalculation invalidates build data", TerrainRebuild),
    ("large destruction notifications are limited to ten thousand IDs", DestroyBatch),
    ("placed markers require creator metadata while unconditional markers do not", MarkerCreators),
    ("safe zone sizes zero one and two have the expected protection radius", MarkerRadius),
    ("changing marker configuration invalidates the cached protection", MarkerReconfigure),
    ("new cfg defaults protect player-built Pieces and tombstones but exclude campfires", DefaultProtection),
    ("automatic ship markers require creator metadata and follow safe zone ranges", DefaultShipProtection),
    ("editable blacklist removes and restores Piece markers while preserving snapshots and tombstones", EditableConfiguredProtection),
    ("empty blacklist protects all player-built Pieces including campfires", EmptyConfiguredProtection),
    ("blacklisted Pieces share other markers' zones without expanding protection themselves", BlacklistedZoneSharing),
    ("automatic Piece protection covers unloaded builds without accepting natural or merely owned objects", AutomaticPieceProtection),
    ("prefab classification is bounded by prefab types and reused within each protection scan", PrefabClassificationCache),
    ("fresh protection scans observe late registration prefab replacement and a new world", PrefabClassificationRefresh),
    ("missing prefab registry prevents an unprotected scan while safe zone zero remains valid", MissingPrefabRegistry),
    ("overlapping and duplicate markers in outside sectors deduplicate their zones", OutsideMarkers),
    ("explicit invalidation refreshes recently changed marker data", MarkerInvalidation),
    ("expired marker cache refreshes within the same safe zone size", MarkerExpiry),
    ("EpicLoot protects only unfound treasure and pending treasure spawns without Piece or creator", TreasureMarkers),
    ("treasure spawners require opaque payload and exclude bounty uninitialized and placed controllers", TreasureSpawners),
    ("treasure observations survive completion for one run and refresh on the next", TreasureLifetime),
    ("treasure sector checks see new and moved targets without protecting neighbors", TreasureMovement),
    ("treasure protection stays in its own sector at native coordinate edges", TreasureSectorEdges),
    ("spawned-child recursion cannot delete a treasure in a different sector", TreasureSpawnedChild),
    ("disabled EpicLoot protection permits chest and spawned-child deletion", TreasureDeletionDisabled),
    ("border finalization still repairs neighboring treasure sectors", TreasureBorders)
};
var failed = 0;
foreach (var (name, body) in tests)
{
    Reset();
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} native world tests passed.");
return failed == 0 ? 0 : 1;

static void Reset()
{
    ZDOMan.instance = new(); ZNetScene.instance = new(); ZNet.instance = new();
    ZoneSystem.instance = new(); Player.m_localPlayer = null;
    Minimap.instance = new(); ClutterSystem.instance = new(); ZRoutedRpc.instance = new();
    Heightmap.Reset(); TerrainResetter.Borders = null;
    UnityEngine.Object.ResetDestroyCallbacks();
    BaseProtection.Configure([], []);
}

static void Snapshots()
{
    ZDOMan.instance.Add(new(1, "stone", new(0, 0, 0)));
    ZDOMan.instance.Add(new(2, "tree", new(128, 0, 0)));
    var center = GameWorld.GetZDOs(new(0, 0));
    var outside = GameWorld.GetZDOs(new(2, 0));
    var all = GameWorld.AllZDOs();
    ZDOMan.instance.ClearObjects();
    Equal(1, center.Count); Equal(1, outside.Count); Equal(2, all.Count());
}

static void SectorPortals()
{
    var rock = new ZDO(1, "stone", new(300 * 64, 0, 0));
    var portal = new ZDO(2, "portal", new(300 * 64, 0, 0));
    var other = new ZDO(3, "stone", new(301 * 64, 0, 0));
    var otherPortal = new ZDO(4, "portal", new(301 * 64, 0, 0));
    var corner = new ZDO(5, "tree", new(-256 * 64, 0, -256 * 64));
    ZDOMan.instance.Add(rock); ZDOMan.instance.Add(portal, portal: true);
    ZDOMan.instance.Add(other); ZDOMan.instance.Add(otherPortal, portal: true);
    ZDOMan.instance.Add(corner);
    True(GameWorld.GetZDOs(new(300, 0)).ToHashSet().SetEquals([rock, portal]));
    True(new ProbeReset().Run(new(300, 0)));
    True(!rock.Valid && !portal.Valid && other.Valid && otherPortal.Valid && corner.Valid);
}

static void CoordinateEdges()
{
    var zones = new HashSet<Vector2s>();
    Player.m_localPlayer = new();
    foreach (var position in new[] { 32768 * 64 - 32f, -32768 * 64 - 33f })
    {
        Player.m_localPlayer.transform.position = new(position, 0, 0);
        GameWorld.CollectPlayerZones(zones);
    }
    Equal(0, zones.Count);
    Player.m_localPlayer.transform.position = new(32767 * 64 + 16f, 0, 0);
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new((int)short.MaxValue, 0)]));
    Player.m_localPlayer = null;

    BaseProtection.Configure([], ["Player_tombstone"]);
    ZDOMan.instance.Add(new(1, "Player_tombstone", new(short.MaxValue * 64, 0, short.MaxValue * 64)));
    var protectedZones = BaseProtection.GetExcluded(2);
    Equal(4, protectedZones.Count);
    True(protectedZones.All(zone => zone.x > 0 && zone.y > 0));

    for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++) ZoneSystem.instance.AddGenerated(new(short.MaxValue + x, short.MaxValue + y));
    var operation = new ProbeReset();
    operation.Run(new(short.MaxValue, short.MaxValue));
    operation.Finish();
    Equal(3, TerrainResetter.Borders!.Count);
    True(TerrainResetter.Borders.Keys.All(zone => zone.x > 0 && zone.y > 0));
}

static void GeneratedSnapshots()
{
    Vector2s[] inserted = [new(10, 0), new(0, -1), new(-1, 0), new(0, 0), new(1, 0), new(0, 1), new(-2, -2)];
    foreach (var zone in inserted) ZoneSystem.instance.AddGenerated(zone);
    var candidates = new HashSet<Vector2s>([new(0, 1), new(-2, -2), new(-1, 0), new(0, -1), new(100, 0)]);
    var expected = inserted.OrderBy(zone => (long)zone.x * zone.x + (long)zone.y * zone.y)
        .Where(candidates.Contains).ToArray();
    var restricted = GameWorld.GeneratedSnapshot(candidates);
    True(expected.SequenceEqual(restricted));
    Equal(0, GameWorld.GeneratedSnapshot([]).Length);
    Equal(inserted.Length, GameWorld.GeneratedSnapshot().Length);

    var set = GameWorld.GeneratedSetSnapshot();
    True(set.SetEquals(inserted));
    set.Clear();
    candidates.Clear();
    ZoneSystem.instance.AddGenerated(new(3, 3));
    True(expected.SequenceEqual(restricted));
    Equal(inserted.Length + 1, GameWorld.GeneratedSetSnapshot().Count);
}

static void PlayerProtection()
{
    var player = new ZDO(1, "Player", new());
    var local = new ZDO(2, "custom_avatar", new());
    var remote = new ZDO(3, "remote_avatar", new());
    var peer = new ZDO(4, "peer_avatar", new());
    Player.m_localPlayer = new() { Id = local.m_uid };
    ZNet.instance.Players.Add(new() { m_characterID = remote.m_uid });
    ZNet.instance.Peers.Add(new() { m_characterID = peer.m_uid });
    foreach (var zdo in new[] { player, local, remote, peer })
    {
        ZDOMan.instance.Add(zdo); GameWorld.RemoveZDO(zdo); True(zdo.Valid);
    }
    Equal(0, ZDOMan.instance.Destroyed.Count);
}

static void LocalPlayerZones()
{
    Player.m_localPlayer = new() { transform = new() { position = new(-32, 70, 96) } };
    var zones = new HashSet<Vector2s> { new(99, 99) };
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(99, 99), new(0, 2)]));
    // No character ZDO is required for the host's current transform, including a real origin.
    Player.m_localPlayer.transform.position = new(0, 0, 0);
    GameWorld.CollectPlayerZones(zones);
    True(zones.Contains(new(0, 0)));
    Player.m_localPlayer.transform.position = new(-32.1f, 70, -32.1f);
    GameWorld.CollectPlayerZones(zones);
    True(zones.Contains(new(-1, -1)));
}

static void RemotePlayerZones()
{
    ZNet.instance.Dedicated = true;
    Player.m_localPlayer = new() { transform = new() { position = new(640, 0, 0) } };
    var remote = new ZDO(21, "custom_avatar", new(128, 5000, -64));
    ZDOMan.instance.Add(remote);
    ZNet.instance.Peers.Add(new() { m_characterID = remote.m_uid, m_refPos = new(320, 0, 0), m_publicRefPos = false });
    // Neither an unconnected character record nor the public map list is an occupancy source.
    var stale = new ZDO(22, "Player", new(384, 0, 0));
    ZDOMan.instance.Add(stale);
    ZNet.instance.Players.Add(new() { m_characterID = stale.m_uid, m_position = new(448, 0, 0) });
    var zones = new HashSet<Vector2s>();
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(2, -1)]));
}

static void MovingPlayerZones()
{
    Player.m_localPlayer = new() { transform = new() { position = new(64, 0, 0) } };
    var remote = new ZDO(21, "Player", new(128, 0, 0));
    ZDOMan.instance.Add(remote);
    var peer = new ZNetPeer { m_characterID = remote.m_uid, m_refPos = new(640, 0, 0) };
    ZNet.instance.Peers.Add(peer);
    var zones = new HashSet<Vector2s>();
    GameWorld.CollectPlayerZones(zones);
    Player.m_localPlayer.transform.position = new(192, 0, 0);
    remote.SetPosition(new(256, 0, 0));
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(1, 0), new(2, 0), new(3, 0), new(4, 0)]));

    peer.m_socket!.Connected = false;
    remote.SetPosition(new(320, 0, 0));
    Player.m_localPlayer = null;
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(1, 0), new(2, 0), new(3, 0), new(4, 0)]));
    var nextRun = new HashSet<Vector2s>();
    GameWorld.CollectPlayerZones(nextRun);
    Equal(0, nextRun.Count);

    // Removing a peer also stops collection even if its old socket and ZDO remain valid.
    peer.m_socket.Connected = true;
    ZNet.instance.Peers.Clear();
    GameWorld.CollectPlayerZones(nextRun);
    Equal(0, nextRun.Count);
}

static void PlayerZoneFallback()
{
    ZNet.instance.Peers.Add(new() { m_characterID = ZDOID.None, m_refPos = new(64, 0, 0) });
    ZNet.instance.Peers.Add(new() { m_characterID = new(20), m_refPos = new(128, 0, 0) });
    var invalid = new ZDO(21, "Player", new(640, 0, 0)) { Valid = false };
    var malformed = new ZDO(22, "Player", new(float.NaN, 0, 0));
    ZDOMan.instance.Add(invalid);
    ZDOMan.instance.Add(malformed, new(20, 0));
    ZNet.instance.Peers.Add(new() { m_characterID = invalid.m_uid, m_refPos = new(192, 0, 0) });
    ZNet.instance.Peers.Add(new() { m_characterID = malformed.m_uid, m_refPos = new(256, 0, 0) });
    var zones = new HashSet<Vector2s>();
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(1, 0), new(2, 0), new(3, 0), new(4, 0)]));

    ZDOMan.instance = null!;
    zones.Clear();
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(1, 0), new(2, 0), new(3, 0), new(4, 0)]));
}

static void InvalidPlayerZones()
{
    var remote = new ZDO(21, "Player", new(64, 0, 0));
    ZDOMan.instance.Add(remote);
    ZNet.instance.Peers.Add(new() { m_characterID = remote.m_uid, m_uid = 0, m_refPos = new(128, 0, 0) });
    ZNet.instance.Peers.Add(new() { m_characterID = remote.m_uid, m_socket = new() { Connected = false }, m_refPos = new(192, 0, 0) });
    ZNet.instance.Peers.Add(new() { m_characterID = remote.m_uid, m_server = true, m_refPos = new(256, 0, 0) });
    ZNet.instance.Peers.Add(new() { m_characterID = remote.m_uid, m_socket = null, m_refPos = new(320, 0, 0) });
    ZNet.instance.Peers.Add(null!);
    var invalidPositions = new[]
    {
        new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0),
        new Vector3(0, 0, float.NegativeInfinity), new Vector3(float.MaxValue, 0, 0),
        new Vector3(0, 0, float.MinValue)
    };
    var zones = new HashSet<Vector2s> { new(99, 99) };
    foreach (var position in invalidPositions)
    {
        Player.m_localPlayer = new() { transform = new() { position = position } };
        ZNet.instance.Peers.Add(new() { m_refPos = position });
        GameWorld.CollectPlayerZones(zones);
    }
    True(zones.SetEquals([new(99, 99)]));
}

static void PlayerZonesRequireHost()
{
    Player.m_localPlayer = new() { transform = new() { position = new(64, 0, 0) } };
    ZNet.instance.Peers.Add(new() { m_refPos = new(128, 0, 0) });
    var zones = new HashSet<Vector2s> { new(99, 99) };
    ZNet.instance.Server = false;
    GameWorld.CollectPlayerZones(zones);
    ZNet.instance.Server = true;
    ZNet.instance.HaveStopped = true;
    GameWorld.CollectPlayerZones(zones);
    ZNet.instance = null!;
    GameWorld.CollectPlayerZones(zones);
    True(zones.SetEquals([new(99, 99)]));
    Throws<ArgumentNullException>(() => GameWorld.CollectPlayerZones(null!));
}

static void SpawnedCycle()
{
    var first = new ZDO(1, "spawner", new());
    var second = new ZDO(2, "spawned", new());
    first.Spawned = second.m_uid; second.Spawned = first.m_uid;
    ZDOMan.instance.Add(first); ZDOMan.instance.Add(second);
    GameWorld.RemoveZDO(first);
    Equal(2, ZDOMan.instance.Destroyed.Count);
    True(!first.Valid && !second.Valid);
    Equal(987L, first.Owner); Equal(987L, second.Owner);
}

static void LoadedDeletion()
{
    var zdo = new ZDO(1, "stone", new());
    var view = new ZNetView(zdo);
    ZDOMan.instance.Add(zdo); ZNetScene.instance.Add(zdo, view);
    GameWorld.RemoveZDO(zdo);
    Equal(1, ZNetScene.instance.DestroyCalls);
    Equal(0, ZDOMan.instance.Destroyed.Count);
    True(view.gameObject.Destroyed && !zdo.Valid);
}

static void OwnedRelease()
{
    var zone = new Vector2s(0, 0);
    GameWorld.PokeZone(zone);
    True(GameWorld.TryGetRoot(zone, out var root));
    var zdo = new ZDO(1, "ore", new());
    var view = new ZNetView(zdo);
    ZDOMan.instance.Add(zdo); ZNetScene.instance.Add(zdo, view);
    GameWorld.ReleaseZone(zone); GameWorld.ReleaseZone(zone);
    True(view.Reset && view.gameObject.Destroyed && root.Destroyed);
    True(zdo.Valid && !ZNetScene.instance.Has(zdo));
    True(!ZoneSystem.instance.HasRoot(zone));
    Equal(0, ZDOMan.instance.Destroyed.Count);
}

static void ExistingLoad()
{
    var zone = new Vector2s(0, 0);
    var root = ZoneSystem.instance.AddRoot(zone, loaded: false);
    GameWorld.PokeZone(zone); GameWorld.ReleaseZone(zone);
    True(!root.Destroyed && ZoneSystem.instance.HasRoot(zone));
}

static void DeferredReleaseLoading()
{
    var zone = new Vector2s(0, 0);
    GameWorld.PokeZone(zone);
    var proxy = new ZDO(1, "LocationProxy", new());
    var view = new ZNetView(proxy);
    ZDOMan.instance.Add(proxy); ZNetScene.instance.Add(proxy, view);
    ZoneSystem.instance.SetLoadingInZone(proxy);
    view.gameObject.OnDestroy = () => ZoneSystem.instance.UnsetLoadingInZone(proxy);
    GameWorld.ReleaseZone(zone);
    True(ZoneSystem.instance.HasRoot(zone));
    True(ZoneSystem.instance.IsLoading(zone));
    True(!view.gameObject.Destroyed);
    Equal(0, GameWorld.ProcessDeferredReleases());
    True(ZoneSystem.instance.HasRoot(zone));
    ZoneSystem.instance.UnsetLoadingInZone(proxy);
    view.gameObject.OnDestroy = null; // Native proxy clears its registration after loading.
    Equal(1, GameWorld.ProcessDeferredReleases());
    True(!ZoneSystem.instance.HasRoot(zone) && view.gameObject.Destroyed);
    UnityEngine.Object.FlushDestroyCallbacks();
    True(!ZoneSystem.instance.IsLoading(zone));
    True(proxy.Valid); // Unloading preserved the persistent proxy record.
    GameWorld.PokeZone(zone);
    True(ZoneSystem.instance.HasRoot(zone) && ZoneSystem.instance.IsZoneLoaded(zone));
}

static void DeferredReleaseReplacement()
{
    var zone = new Vector2s(0, 0);
    GameWorld.PokeZone(zone);
    True(GameWorld.TryGetRoot(zone, out var original));
    var proxy = new ZDO(1, "LocationProxy", new());
    ZoneSystem.instance.SetLoadingInZone(proxy);
    GameWorld.ReleaseZone(zone);
    var replacement = ZoneSystem.instance.AddRoot(zone);
    ZoneSystem.instance.UnsetLoadingInZone(proxy);
    Equal(1, GameWorld.ProcessDeferredReleases());
    True(!replacement.Destroyed && ZoneSystem.instance.HasRoot(zone));
    True(!original.Destroyed);
    Equal(0, GameWorld.ProcessDeferredReleases());
}

static void DeferredPlayerArrival()
{
    var zone = new Vector2s(0, 0);
    GameWorld.PokeZone(zone);
    True(GameWorld.TryGetRoot(zone, out var root));
    var proxy = new ZDO(1, "LocationProxy", new());
    ZoneSystem.instance.SetLoadingInZone(proxy);
    GameWorld.ReleaseZone(zone);
    ZDOMan.instance.Add(new(2, "Player", new()));
    ZoneSystem.instance.UnsetLoadingInZone(proxy);
    Equal(1, GameWorld.ProcessDeferredReleases());
    True(!root.Destroyed && ZoneSystem.instance.HasRoot(zone));
}

static void DeferredResetLoading()
{
    var zone = new Vector2s(0, 0);
    ZoneSystem.instance.AddGenerated(zone);
    ZoneSystem.instance.AddRoot(zone);
    var oldProxy = new ZDO(1, "LocationProxy", new());
    var oldView = new ZNetView(oldProxy);
    ZDOMan.instance.Add(oldProxy); ZNetScene.instance.Add(oldProxy, oldView);
    ZoneSystem.instance.SetLoadingInZone(oldProxy);
    oldView.gameObject.OnDestroy = () => ZoneSystem.instance.UnsetLoadingInZone(oldProxy);
    True(new ProbeReset().Run(zone));
    True(!ZoneSystem.instance.HasRoot(zone) && ZoneSystem.instance.IsLoading(zone));
    // A replacement root may start loading before Unity dispatches the old object's OnDestroy.
    GameWorld.PokeZone(zone);
    var newProxy = new ZDO(2, "LocationProxy", new());
    ZDOMan.instance.Add(newProxy);
    ZoneSystem.instance.SetLoadingInZone(newProxy);
    UnityEngine.Object.FlushDestroyCallbacks();
    True(ZoneSystem.instance.IsLoading(zone) && !ZoneSystem.instance.IsZoneLoaded(zone));
    ZoneSystem.instance.UnsetLoadingInZone(newProxy);
    True(!ZoneSystem.instance.IsLoading(zone) && ZoneSystem.instance.IsZoneLoaded(zone));
}

static void PlayerEnters()
{
    var zone = new Vector2s(0, 0);
    GameWorld.PokeZone(zone);
    ZDOMan.instance.Add(new(1, "Player", new()));
    GameWorld.ReleaseZone(zone);
    True(ZoneSystem.instance.HasRoot(zone));
}

static void ResetZoneAndBorders()
{
    var zone = new Vector2s(0, 0);
    for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++) ZoneSystem.instance.AddGenerated(new(x, y));
    var root = ZoneSystem.instance.AddRoot(zone, loaded: false);
    ZoneSystem.instance.m_locationInstances[zone] = new() { m_placed = true, m_position = new(0, -10, 0) };
    var rock = new ZDO(1, "stone", new());
    var player = new ZDO(2, "Player", new());
    var outside = new ZDO(3, "tree", new(64, 0, 0));
    ZDOMan.instance.Add(rock); ZDOMan.instance.Add(player); ZDOMan.instance.Add(outside, zone);
    var operation = new ProbeReset();
    True(operation.Run(zone));
    operation.Finish();
    True(!GameWorld.IsGenerated(zone) && root.Destroyed && !ZoneSystem.instance.IsLoading(zone));
    True(!rock.Valid && player.Valid && outside.Valid);
    Equal(false, ZoneSystem.instance.m_locationInstances[zone].m_placed);
    Equal(77f, ZoneSystem.instance.m_locationInstances[zone].m_position.y);
    Equal(8, GameWorld.GeneratedSnapshot().Length);
    var expected = new Dictionary<Vector2s, BorderDirection>
    {
        [new(0, -1)] = BorderDirection.North, [new(-1, 0)] = BorderDirection.East,
        [new(0, 1)] = BorderDirection.South, [new(1, 0)] = BorderDirection.West,
        [new(1, -1)] = BorderDirection.NorthWest, [new(-1, -1)] = BorderDirection.NorthEast,
        [new(1, 1)] = BorderDirection.SouthWest, [new(-1, 1)] = BorderDirection.SouthEast
    };
    Equal(8, TerrainResetter.Borders!.Count);
    foreach (var (key, value) in expected) Equal(value, TerrainResetter.Borders[key]);
    Equal(1, ClutterSystem.instance.Cleared); Equal(1, Minimap.instance.Refreshes);
}

static void TerrainRebuild()
{
    var map = Heightmap.Add();
    GameWorld.RecalculateTerrain();
    True(!map.HasBuildData); Equal(1, map.Pokes);
    Equal(1, map.LastDelay); Equal(false, map.LastPaintOnly);
}

static void PartialDeletionBorders()
{
    var zone = new Vector2s(0, 0);
    for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++) ZoneSystem.instance.AddGenerated(new(x, y));
    var compiler = new ZDO(1, "_TerrainCompiler", new());
    var remaining = new ZDO(2, "stone", new());
    ZDOMan.instance.Add(compiler); ZDOMan.instance.Add(remaining);
    ZDOMan.instance.AfterDestroy = _ => throw new InvalidOperationException("mod hook failed after deletion");
    var operation = new ProbeReset();
    Throws<InvalidOperationException>(() => operation.Run(zone));
    True(!compiler.Valid && remaining.Valid);
    True(GameWorld.IsGenerated(zone)); // Failure preceded the generated-state update.
    operation.Finish(); // The tracking wrapper calls OnEnd during partial-operation cleanup.
    True(TerrainResetter.Borders != null);
    Equal(8, TerrainResetter.Borders!.Count);
    Equal(BorderDirection.North, TerrainResetter.Borders[new(0, -1)]);
    Equal(BorderDirection.SouthWest, TerrainResetter.Borders[new(1, 1)]);
}

static void DestroyBatch()
{
    var method = typeof(GameWorld).GetNestedType("DestroyBatchPatch", BindingFlags.NonPublic)!
        .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
    for (var i = 0; i < 10001; i++) ZDOMan.instance.DestructionQueue.Add(new(i + 1));
    Equal(false, (bool)method.Invoke(null, [ZDOMan.instance])!);
    Equal(1, ZDOMan.instance.DestructionQueue.Count);
    Equal(10001L, ZDOMan.instance.DestructionQueue[0].Id);
    Equal(1, ZRoutedRpc.instance.Sent.Count);
    Equal(10000, ZRoutedRpc.instance.Sent[0].Count);
    Equal(10000, ZRoutedRpc.instance.Sent[0].Ids.Count);
    Equal(true, (bool)method.Invoke(null, [ZDOMan.instance])!);
}

static void MarkerCreators()
{
    RegisterPiece("piece_workbench");
    BaseProtection.Configure([], ["Player_tombstone"]);
    ZDOMan.instance.Add(new(1, "piece_workbench", new(0, 0, 0)));
    ZDOMan.instance.Add(new ZDO(2, "piece_workbench", new(128, 0, 0)) { Creator = 55 });
    ZDOMan.instance.Add(new(3, "Player_tombstone", new(192, 0, 0)));
    ZDOMan.instance.Add(new ZDO(4, "unlisted_piece", new(256, 0, 0)) { Creator = 55 });
    var protectedZones = BaseProtection.GetExcluded(1);
    Equal(2, protectedZones.Count);
    True(protectedZones.Contains(new(2, 0)) && protectedZones.Contains(new(3, 0)));
    True(!protectedZones.Contains(new(0, 0)) && !protectedZones.Contains(new(4, 0)));
}

static void MarkerRadius()
{
    RegisterPiece("piece_workbench");
    BaseProtection.Configure([], []);
    ZDOMan.instance.Add(new ZDO(1, "piece_workbench", new(320, 0, 0)) { Creator = 55 });
    Equal(0, BaseProtection.GetExcluded(0).Count);
    var one = BaseProtection.GetExcluded(1);
    Equal(1, one.Count); True(one.Contains(new(5, 0)));
    var two = BaseProtection.GetExcluded(2);
    Equal(9, two.Count);
    for (var x = 4; x <= 6; x++)
        for (var y = -1; y <= 1; y++) True(two.Contains(new(x, y)));
    Equal(1, BaseProtection.GetExcluded(1).Count);
    Throws<ArgumentOutOfRangeException>(() => BaseProtection.GetExcluded(-1));
}

static void MarkerReconfigure()
{
    RegisterPiece("piece_workbench");
    RegisterPiece("forge");
    ZDOMan.instance.Add(new ZDO(1, "piece_workbench", new()) { Creator = 55 });
    ZDOMan.instance.Add(new ZDO(2, "forge", new(256, 0, 0)) { Creator = 55 });
    BaseProtection.Configure(["forge"], []);
    var previous = BaseProtection.GetExcluded(1);
    True(previous.Contains(new(0, 0)));
    BaseProtection.Configure(["piece_workbench"], []);
    var current = BaseProtection.GetExcluded(1);
    Equal(1, current.Count); True(current.Contains(new(4, 0)));
    True(!current.Contains(new(0, 0)));
    // Existing operation snapshots must not be modified when configuration replaces the cache.
    Equal(1, previous.Count); True(previous.Contains(new(0, 0)));
}

static void DefaultProtection()
{
    RegisterPiece("piece_workbench");
    RegisterPiece("piece_chest_wood");
    RegisterPiece("woodwall");
    RegisterPiece("fire_pit");
    var settings = new FreshWorldConfig(new ConfigFile()).Capture();
    var options = settings.Options;
    Equal(1, options.ZoneSafeZones);
    True(options.PieceBlacklist.SequenceEqual(["fire_pit"]));
    True(options.ProtectedObjects.Contains("Player_tombstone"));
    BaseProtection.Configure(options.PieceBlacklist, options.ProtectedObjects);
    ZDOMan.instance.Add(new(1, "piece_workbench", new())); // Unclaimed world object is not a player marker.
    ZDOMan.instance.Add(new ZDO(2, "piece_chest_wood", new(64, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new(3, "Player_tombstone", new(128, 0, 0)));
    ZDOMan.instance.Add(new ZDO(4, "woodwall", new(192, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(5, "fire_pit", new(640, 0, 0)) { Creator = 77 });
    var protectedZones = BaseProtection.GetExcluded(options.ZoneSafeZones);
    Equal(3, protectedZones.Count);
    True(protectedZones.SetEquals([new(1, 0), new(2, 0), new(3, 0)]));
    True(!BaseProtection.GetExcluded(2).Contains(new(10, 0)));
}

static void DefaultShipProtection()
{
    var options = new FreshWorldConfig(new ConfigFile()).Capture().Options;
    string[] ships = ["Raft", "Karve", "VikingShip", "VikingShip_Ashlands"];
    BaseProtection.Configure(options.PieceBlacklist, options.ProtectedObjects);
    for (var i = 0; i < ships.Length; i++)
    {
        RegisterPiece(ships[i]);
        ZDOMan.instance.Add(new ZDO(i * 2 + 1, ships[i], new(i * 512, 0, 0)) { Creator = 77 });
        ZDOMan.instance.Add(new(i * 2 + 2, ships[i], new(i * 512 + 256, 0, 0)));
    }
    Equal(0, BaseProtection.GetExcluded(0).Count);
    True(BaseProtection.GetExcluded(1).SetEquals([new(0, 0), new(8, 0), new(16, 0), new(24, 0)]));
    var protectedZones = BaseProtection.GetExcluded(2);
    Equal(36, protectedZones.Count);
    True(protectedZones.Contains(new(-1, -1)) && protectedZones.Contains(new(25, 1)));
    True(!protectedZones.Contains(new(4, 0)) && !protectedZones.Contains(new(12, 0)) &&
        !protectedZones.Contains(new(20, 0)) && !protectedZones.Contains(new(28, 0)));
}

static void EditableConfiguredProtection()
{
    RegisterPiece("piece_workbench");
    RegisterPiece("woodwall");
    RegisterPiece("fire_pit");
    ZNetScene.instance.RegisterPrefab("not_a_piece", new());
    var file = new ConfigFile();
    var config = new FreshWorldConfig(file);
    var initial = config.Capture().Options;
    BaseProtection.Configure(initial.PieceBlacklist, initial.ProtectedObjects);
    ZDOMan.instance.Add(new ZDO(1, "piece_workbench", new()) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(2, "fire_pit", new(64, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new(3, "woodwall", new(128, 0, 0)));
    ZDOMan.instance.Add(new(4, "Player_tombstone", new(192, 0, 0)));
    ZDOMan.instance.Add(new ZDO(5, "woodwall", new(256, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(6, "not_a_piece", new(320, 0, 0)) { Creator = 77 });
    var originalZones = BaseProtection.GetExcluded(1);
    True(originalZones.SetEquals([new(0, 0), new(3, 0), new(4, 0)]));

    file.Set("Protection", "PieceBlacklist", "fire_pit,piece_workbench,Player_tombstone");
    var updated = config.Capture().Options;
    BaseProtection.Configure(updated.PieceBlacklist, updated.ProtectedObjects);
    var protectedZones = BaseProtection.GetExcluded(1);
    True(protectedZones.SetEquals([new(3, 0), new(4, 0)]));

    // Replacing the blacklist restores removed IDs; it does not merge exclusions back from defaults.
    file.Set("Protection", "PieceBlacklist", "woodwall");
    updated = config.Capture().Options;
    BaseProtection.Configure(updated.PieceBlacklist, updated.ProtectedObjects);
    protectedZones = BaseProtection.GetExcluded(1);
    True(protectedZones.SetEquals([new(0, 0), new(1, 0), new(3, 0)]));
    True(updated.PieceBlacklist.SequenceEqual(["woodwall"]));

    file.Set("Protection", "PieceBlacklist", "");
    updated = config.Capture().Options;
    BaseProtection.Configure(updated.PieceBlacklist, updated.ProtectedObjects);
    True(BaseProtection.GetExcluded(1).SetEquals([new(0, 0), new(1, 0), new(3, 0), new(4, 0)]));
    True(originalZones.SetEquals([new(0, 0), new(3, 0), new(4, 0)])); // The original snapshot remains unchanged.
}

static void EmptyConfiguredProtection()
{
    RegisterPiece("piece_workbench");
    RegisterPiece("fire_pit");
    var file = new ConfigFile();
    var config = new FreshWorldConfig(file);
    file.Set("Protection", "PieceBlacklist", "");
    var options = config.Capture().Options;
    Equal(0, options.PieceBlacklist.Length);
    BaseProtection.Configure(options.PieceBlacklist, options.ProtectedObjects);
    ZDOMan.instance.Add(new ZDO(1, "piece_workbench", new()) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(2, "fire_pit", new(64, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new(3, "Player_tombstone", new(128, 0, 0))); // No creator metadata required.
    var protectedZones = BaseProtection.GetExcluded(1);
    True(protectedZones.SetEquals([new(0, 0), new(1, 0), new(2, 0)]));
    Equal(0, BaseProtection.GetExcluded(0).Count);
    Equal(15, BaseProtection.GetExcluded(2).Count);
}

static void BlacklistedZoneSharing()
{
    RegisterPiece("fire_pit");
    RegisterPiece("piece_workbench");
    var options = new FreshWorldConfig(new ConfigFile()).Capture().Options;
    BaseProtection.Configure(options.PieceBlacklist, options.ProtectedObjects);
    ZDOMan.instance.Add(new ZDO(1, "fire_pit", new()) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(2, "fire_pit", new(256, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(3, "piece_workbench", new(256, 0, 0)) { Creator = 77 });
    True(BaseProtection.GetExcluded(1).SetEquals([new(4, 0)]));
    var surrounding = BaseProtection.GetExcluded(2);
    Equal(9, surrounding.Count);
    True(surrounding.Contains(new(4, 0)) && !surrounding.Contains(new(0, 0)));
    True(!surrounding.Contains(new(1, 0)) && !surrounding.Contains(new(-1, 0)));
    Equal(0, BaseProtection.GetExcluded(0).Count);
}

static GameObject RegisterPiece(string name)
{
    var prefab = new GameObject();
    prefab.AddComponent(new Piece());
    ZNetScene.instance.RegisterPrefab(name, prefab);
    return prefab;
}

static void AutomaticPieceProtection()
{
    string[] pieces = ["woodwall", "stone_wall", "blackmarble_wall", "modded_piece", "Raft",
        "Cart", "piece_trap_troll", "piece_sapcollector", "planted_piece"];
    for (var i = 0; i < pieces.Length; i++)
    {
        RegisterPiece(pieces[i]);
        ZDOMan.instance.Add(new ZDO(i + 1, pieces[i], new(i * 256, 0, 0)) { Creator = 77 });
    }
    // Creator and network owner are separate; a natural Piece can be owned without being built.
    ZDOMan.instance.Add(new ZDO(100, "woodwall", new(-512, 0, 0)) { Owner = 987 });
    ZNetScene.instance.RegisterPrefab("not_a_piece", new());
    ZDOMan.instance.Add(new ZDO(101, "not_a_piece", new(-1024, 0, 0)) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(102, "unknown_prefab", new(-1536, 0, 0)) { Creator = 77 });
    Equal(0, BaseProtection.GetExcluded(0).Count);
    Equal(0, ZNetScene.instance.PrefabQueries.Count);
    var one = BaseProtection.GetExcluded(1);
    True(one.SetEquals(Enumerable.Range(0, pieces.Length).Select(i => new Vector2s(i * 4, 0))));
    var two = BaseProtection.GetExcluded(2);
    Equal(pieces.Length * 9, two.Count);
    for (var i = 0; i < pieces.Length; i++)
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++) True(two.Contains(new(i * 4 + x, y)));
    Equal(0, ZoneSystem.instance.Pokes); // No scene/zone loading is needed to protect these builds.
}

static void PrefabClassificationCache()
{
    var campfire = RegisterPiece("fire_pit");
    BaseProtection.Configure(["fire_pit"], []);
    var piece = RegisterPiece("woodwall");
    var natural = new GameObject();
    ZNetScene.instance.RegisterPrefab("tree", natural);
    for (var i = 0; i < 2000; i++)
    {
        ZDOMan.instance.Add(new ZDO(i * 3 + 1, "woodwall", new((i % 2) * 64, 0, 0)) { Creator = 77 });
        ZDOMan.instance.Add(new(i * 3 + 2, "tree", new(512, 0, 0)));
        ZDOMan.instance.Add(new(i * 3 + 3, "missing_prefab", new(1024, 0, 0)));
        ZDOMan.instance.Add(new ZDO(10000 + i, "fire_pit", new(1536, 0, 0)) { Creator = 77 });
    }
    var protection = BaseProtection.GetExcluded(2);
    Equal(12, protection.Count);
    Equal(3, ZNetScene.instance.PrefabQueries.Count);
    True(ZNetScene.instance.PrefabQueries.Values.All(count => count == 1));
    Equal(1, piece.ComponentQueries); Equal(1, natural.ComponentQueries);
    Equal(0, campfire.ComponentQueries);
    True(ReferenceEquals(protection, BaseProtection.GetExcluded(2)));
    True(ZNetScene.instance.PrefabQueries.Values.All(count => count == 1));
}

static void PrefabClassificationRefresh()
{
    ZDOMan.instance.Add(new ZDO(1, "late_piece", new()) { Creator = 77 });
    ZDOMan.instance.Add(new ZDO(2, "changed_prefab", new(128, 0, 0)) { Creator = 77 });
    ZNetScene.instance.RegisterPrefab("changed_prefab", new());
    Equal(0, BaseProtection.GetExcluded(1).Count);
    RegisterPiece("late_piece"); RegisterPiece("changed_prefab");
    typeof(BaseProtection).GetField("calculatedAt", BindingFlags.Static | BindingFlags.NonPublic)!
        .SetValue(null, DateTime.UtcNow.AddSeconds(-11));
    True(BaseProtection.GetExcluded(1).SetEquals([new(0, 0), new(2, 0)]));

    ZNetScene.instance.RegisterPrefab("changed_prefab", new());
    BaseProtection.InvalidateCache();
    True(BaseProtection.GetExcluded(1).SetEquals([new(0, 0)]));

    // A new run/world must not reuse either positive or negative prefab classifications.
    ZNetScene.instance = new();
    BaseProtection.Configure([], []);
    Equal(0, BaseProtection.GetExcluded(1).Count);
}

static void MissingPrefabRegistry()
{
    ZNetScene.instance = null!;
    Equal(0, BaseProtection.GetExcluded(0).Count);
    Throws<InvalidOperationException>(() => BaseProtection.GetExcluded(1));
}

static void OutsideMarkers()
{
    RegisterPiece("piece_workbench");
    BaseProtection.Configure([], ["Player_tombstone"]);
    ZDOMan.instance.Add(new ZDO(1, "piece_workbench", new(-192, 0, 128)) { Creator = 55 });
    ZDOMan.instance.Add(new ZDO(2, "piece_workbench", new(-192, 0, 128)) { Creator = 66 });
    ZDOMan.instance.Add(new(3, "Player_tombstone", new(-128, 0, 128)));
    var protectedZones = BaseProtection.GetExcluded(2);
    Equal(12, protectedZones.Count);
    for (var x = -4; x <= -1; x++)
        for (var y = 1; y <= 3; y++) True(protectedZones.Contains(new(x, y)));
}

static void MarkerInvalidation()
{
    BaseProtection.Configure([], ["Player_tombstone"]);
    ZDOMan.instance.Add(new(1, "Player_tombstone", new()));
    Equal(1, BaseProtection.GetExcluded(1).Count);
    ZDOMan.instance.Add(new(2, "Player_tombstone", new(320, 0, 0)));
    Equal(1, BaseProtection.GetExcluded(1).Count); // Intentional ten-second scan cache.
    BaseProtection.InvalidateCache();
    Equal(2, BaseProtection.GetExcluded(1).Count);
}

static void MarkerExpiry()
{
    BaseProtection.Configure([], ["Player_tombstone"]);
    ZDOMan.instance.Add(new(1, "Player_tombstone", new()));
    Equal(1, BaseProtection.GetExcluded(1).Count);
    ZDOMan.instance.Add(new(2, "Player_tombstone", new(320, 0, 0)));
    // Advance the private cache timestamp deterministically instead of sleeping ten seconds.
    typeof(BaseProtection).GetField("calculatedAt", BindingFlags.Static | BindingFlags.NonPublic)!
        .SetValue(null, DateTime.UtcNow.AddSeconds(-11));
    Equal(2, BaseProtection.GetExcluded(1).Count);
}

static ZDO TreasureChest(long id, Vector3 position, bool found = false)
{
    var zdo = new ZDO(id, "loot_chest_stone", position);
    zdo.Strings["TreasureMapChest.Biome".GetStableHashCode()] = "BlackForest";
    zdo.Bools["TreasureMapChest.HasBeenFound".GetStableHashCode()] = found;
    return zdo;
}

static ZDO TreasureSpawner(long id, Vector3 position)
{
    var zdo = new ZDO(id, "EL_SpawnController", position);
    // An opaque marker, not a BinaryFormatter fixture: protection must never deserialize it.
    zdo.Bytes["treasure_spawn".GetStableHashCode()] = [255];
    return zdo;
}

static void TreasureMarkers()
{
    var spawner = TreasureSpawner(1, new());
    var chest = TreasureChest(2, new());
    var found = TreasureChest(3, new(), found: true);
    var ordinary = new ZDO(4, "loot_chest_stone", new()) { Creator = 123 };
    var leader = new ZDO(5, "Troll", new());
    var add = new ZDO(6, "Greydwarf", new());
    leader.Strings["BountyID".GetStableHashCode()] = "owner/interval/biome";
    add.Strings["BountyID".GetStableHashCode()] = "owner/interval/biome";
    add.Bools["IsAdd".GetStableHashCode()] = true;
    foreach (var target in new[] { spawner, chest })
    {
        True(EpicLootProtection.IsTreasureObject(target));
        Equal(0L, target.Creator);
        GameWorld.RemoveZDO(target);
        True(target.Valid); Equal(0L, target.Owner);
    }
    foreach (var target in new[] { found, ordinary, leader, add, new ZDO(7, "Troll", new()) })
    {
        True(!EpicLootProtection.IsTreasureObject(target));
        GameWorld.RemoveZDO(target); True(!target.Valid);
    }
    spawner.Valid = false;
    True(!EpicLootProtection.IsTreasureObject(spawner));
}

static void TreasureSpawners()
{
    foreach (var kind in new[] { "pending", "uninitialized", "empty", "bounty", "placed", "other prefab" })
    {
        var zdo = kind == "other prefab" ? new ZDO(1, "other", new()) : TreasureSpawner(1, new());
        if (kind == "uninitialized") zdo.Bytes.Clear();
        if (kind == "empty") zdo.Bytes["treasure_spawn".GetStableHashCode()] = [];
        if (kind == "bounty") zdo.Bools["isBounty".GetStableHashCode()] = true;
        if (kind == "placed") zdo.Bools["placed".GetStableHashCode()] = true;
        if (kind == "other prefab") zdo.Bytes["treasure_spawn".GetStableHashCode()] = [255];
        Equal(kind == "pending", EpicLootProtection.IsTreasureObject(zdo));
        ZDOMan.instance.ClearObjects(); ZDOMan.instance.Add(zdo);
        var protection = new EpicLootProtection();
        Equal(kind == "pending" ? 1 : 0, protection.Capture());
        Equal(kind != "pending", protection.CanResetZone(new(0, 0)));
        GameWorld.RemoveZDO(zdo);
        Equal(kind == "pending", zdo.Valid);
    }
}

static void TreasureLifetime()
{
    var chest = TreasureChest(1, new()); ZDOMan.instance.Add(chest);
    var protection = new EpicLootProtection();
    Equal(1, protection.Capture());
    chest.Bools["TreasureMapChest.HasBeenFound".GetStableHashCode()] = true;
    True(!protection.CanResetZone(new(0, 0)));
    var nextRun = new EpicLootProtection();
    Equal(0, nextRun.Capture()); True(nextRun.CanResetZone(new(0, 0)));
}

static void TreasureMovement()
{
    var protection = new EpicLootProtection(); Equal(0, protection.Capture());
    True(protection.CanResetZone(new(0, 0)));
    var chest = TreasureChest(1, new(64, 0, 64)); ZDOMan.instance.Add(chest);
    True(protection.CanResetZone(new(0, 0)));
    True(!protection.CanResetZone(new(1, 1)));
    chest.SetPosition(new(320, 0, 0));
    ZDOMan.instance.ClearObjects(); ZDOMan.instance.Add(chest);
    True(!protection.CanResetZone(new(5, 0)));
    True(!protection.CanResetZone(new(1, 1))); // Previous observation retained.
    True(protection.CanResetZone(new(-2, 0)));
}

static void TreasureSectorEdges()
{
    var chest = TreasureChest(1, new(short.MaxValue * 64f, 0, 0)); ZDOMan.instance.Add(chest);
    var protection = new EpicLootProtection(); protection.Capture();
    True(protection.CanResetZone(new((int)short.MinValue, 0)));
    True(protection.CanResetZone(new(short.MaxValue - 1, 0)));
    True(!protection.CanResetZone(new((int)short.MaxValue, 0)));
}

static void TreasureSpawnedChild()
{
    var child = TreasureChest(2, new(640, 0, 0));
    var parent = new ZDO(1, "spawner", new()) { Spawned = child.m_uid };
    ZDOMan.instance.Add(parent); ZDOMan.instance.Add(child);
    GameWorld.RemoveZDO(parent);
    True(!parent.Valid && child.Valid); Equal(0L, child.Owner);
    Equal(1, ZDOMan.instance.Destroyed.Count);
}

static void TreasureDeletionDisabled()
{
    var chest = TreasureChest(1, new());
    var controller = TreasureSpawner(2, new());
    var child = TreasureChest(4, new(640, 0, 0));
    var parent = new ZDO(3, "spawner", new()) { Spawned = child.m_uid };
    foreach (var zdo in new[] { chest, controller, parent, child }) ZDOMan.instance.Add(zdo);
    GameWorld.RemoveZDO(chest, false);
    GameWorld.RemoveZDO(controller, false);
    GameWorld.RemoveZDO(parent, false);
    True(!chest.Valid && !controller.Valid && !parent.Valid && !child.Valid);
}

static void TreasureBorders()
{
    for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++) ZoneSystem.instance.AddGenerated(new(x, y));
    var operation = new ProbeReset();
    True(operation.Run(new(0, 0)));
    var chest = TreasureChest(1, new(64, 0, 0));
    ZDOMan.instance.Add(chest);
    operation.Finish();
    Equal(8, TerrainResetter.Borders!.Count);
    True(TerrainResetter.Borders.ContainsKey(new(1, 0)));
    True(chest.Valid);
}

static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
sealed class ProbeReset() : ResetZones(_ => { }, new())
{
    public bool Run(Vector2s zone) => ExecuteZone(zone);
    public void Finish() => OnEnd();
}
