using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine
{
    /// <summary>
    /// Native terrain adapter. Only _TerrainCompiler TCData is edited; player piece ZDOs and
    /// terrain changes outside the selected circle/border remain intact. Call on the host main thread.
    /// </summary>
    internal static class TerrainResetter
    {
        private static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();
        private static ILookup<Vector2s, ZDO>? cache;
        private static ZDOMan? cacheManager;
        private static ZoneSystem? cacheZones;
        private static DateTime cacheTime;
        public static bool Active;

        // Keep Harmony's convention-based discovery separate from terrain editing helpers.
        [HarmonyPatch(typeof(ZoneSystem))]
        private static class GroundGenerationPatches
        {
            // Generation needs the unmodified ground height before loaded TerrainComp instances
            // observe their new ZDO revision. These hooks are active only during our generation.
            [HarmonyPatch(nameof(ZoneSystem.GetGroundData)), HarmonyPostfix]
            private static void GroundData(ref Vector3 p)
            {
                if (Active && WorldGenerator.instance != null) p.y = WorldGenerator.instance.GetHeight(p.x, p.z);
            }

            [HarmonyPatch(nameof(ZoneSystem.GetGroundHeight), typeof(Vector3)), HarmonyPrefix]
            private static bool GroundHeight(Vector3 p, ref float __result)
            {
                if (!Active || WorldGenerator.instance == null) return true;
                __result = WorldGenerator.instance.GetHeight(p.x, p.z);
                return false;
            }
        }

        public static void Execute(Vector3 position, float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (radius == 0) return;
            RequireHost();
            var records = TerrainRecords();
            var center = ZoneSystem.GetZone(position);
            var distance = (double)Math.Ceiling(radius / 64.0);
            if (distance > int.MaxValue / 4) throw new ArgumentOutOfRangeException(nameof(radius));
            var range = (int)distance;
            var pending = new List<KeyValuePair<ZDO, byte[]>>();
            void PrepareZone(Vector2s zone)
            {
                var tile = ZoneSystem.GetZonePos(zone);
                foreach (var zdo in records[zone])
                    PrepareTerrainEdit(zdo, bytes => TerrainDataCodec.ResetCircle(bytes, tile.x, tile.z, position.x, position.z, radius), pending);
            }
            if (range <= 8)
            {
                // Ordinary resource restoration looks up nine nearby buckets, not every world compiler.
                for (var x = Math.Max(short.MinValue, (long)center.x - range); x <= Math.Min(short.MaxValue, (long)center.x + range); x++)
                    for (var z = Math.Max(short.MinValue, (long)center.y - range); z <= Math.Min(short.MaxValue, (long)center.y + range); z++)
                        PrepareZone(new Vector2s((int)x, (int)z));
            }
            else
            {
                // Large-radius requests scan existing records instead of billions of empty coordinates.
                foreach (var group in records)
                    if (Math.Abs((long)group.Key.x - center.x) <= range && Math.Abs((long)group.Key.y - center.y) <= range)
                        PrepareZone(group.Key);
            }
            Apply(pending);
        }

        public static void ResetBorders(IReadOnlyDictionary<Vector2s, BorderDirection> directions)
        {
            if (directions == null) throw new ArgumentNullException(nameof(directions));
            if (directions.Count == 0) return;
            RequireHost();
            var records = TerrainRecords();
            var pending = new List<KeyValuePair<ZDO, byte[]>>();
            foreach (var pair in directions)
            {
                if (pair.Value == BorderDirection.None) continue;
                var tile = ZoneSystem.GetZonePos(pair.Key);
                foreach (var zdo in records[pair.Key])
                    PrepareTerrainEdit(zdo, bytes => TerrainDataCodec.ResetBorders(bytes, pair.Value, tile.x, tile.z), pending);
            }
            Apply(pending);
        }

        public static void InvalidateCache()
        {
            cache = null; cacheManager = null; cacheZones = null; cacheTime = DateTime.MinValue;
        }

        private static ILookup<Vector2s, ZDO> TerrainRecords()
        {
            if (cache == null || !ReferenceEquals(cacheManager, ZDOMan.instance) || !ReferenceEquals(cacheZones, ZoneSystem.instance)
                || DateTime.UtcNow - cacheTime > TimeSpan.FromSeconds(10))
            {
                cache = GameWorld.AllZDOs().Where(zdo => zdo.GetPrefab() == TerrainCompilerHash)
                    .ToLookup(zdo => ZoneSystem.GetZone(zdo.GetPosition()));
                cacheManager = ZDOMan.instance; cacheZones = ZoneSystem.instance; cacheTime = DateTime.UtcNow;
            }
            return cache;
        }

        private static void PrepareTerrainEdit(ZDO zdo, Func<byte[], TerrainEdit> edit, List<KeyValuePair<ZDO, byte[]>> pending)
        {
            var compressed = zdo.GetByteArray(ZDOVars.s_TCData);
            if (compressed == null) return;
            // Use native package/compression boundaries. The pure codec operates on verified ZPackage bytes.
            var package = new ZPackage(Utils.Decompress(compressed));
            var result = edit(package.GetArray());
            if (result.Changed) pending.Add(new KeyValuePair<ZDO, byte[]>(zdo, Utils.Compress(result.Data)));
        }

        private static void Apply(List<KeyValuePair<ZDO, byte[]>> pending)
        {
            // Validate and compress every touched compiler before committing any of this request.
            foreach (var pair in pending)
            {
                if (!pair.Key.IsOwner()) pair.Key.SetOwner(ZDOMan.GetSessionID());
                pair.Key.Set(ZDOVars.s_TCData, pair.Value);
            }
        }

        private static void RequireHost()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.HaveStopped ||
                ZoneSystem.instance == null || ZDOMan.instance == null || WorldGenerator.instance == null)
                throw new InvalidOperationException("Terrain restoration requires the active world host.");
        }
    }
}
