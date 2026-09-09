// Only the game API boundary is substituted. Harmony discovery uses the installed 0Harmony.dll.
using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct Vector2i(int x, int y)
{
    public readonly int x = x;
    public readonly int y = y;
}
public static class HashExtensions
{
    public static int GetStableHashCode(this string value) => StringComparer.Ordinal.GetHashCode(value);
}
public sealed class ZDO
{
    public byte[]? GetByteArray(int key) => throw new InvalidOperationException("Startup discovery touched terrain data.");
    public int GetPrefab() => throw new InvalidOperationException("Startup discovery inspected a world object.");
    public Vector3 GetPosition() => throw new InvalidOperationException("Startup discovery inspected a world object.");
    public bool IsOwner() => throw new InvalidOperationException("Startup discovery queried world ownership.");
    public void SetOwner(long owner) => throw new InvalidOperationException("Startup discovery changed world ownership.");
    public void Set(int key, byte[] value) => throw new InvalidOperationException("Startup discovery changed terrain data.");
}
public static class ZDOVars { public static readonly int s_TCData = 1; }
public sealed class ZDOMan
{
    public static ZDOMan? instance;
    public static long GetSessionID() => throw new InvalidOperationException("No world session is loaded.");
}
public sealed class ZoneSystem
{
    public static ZoneSystem? instance;
    public static Vector2i GetZone(Vector3 p) => new((int)Math.Floor((p.x + 32) / 64), (int)Math.Floor((p.z + 32) / 64));
    public static Vector3 GetZonePos(Vector2i zone) => new(zone.x * 64, 0, zone.y * 64);
    public void GetGroundData(ref Vector3 p, out Vector3 normal, out Heightmap.Biome biome, out Heightmap.BiomeArea biomeArea, out Heightmap hmap)
    { normal = default; biome = default; biomeArea = default; hmap = null!; }
    public float GetGroundHeight(Vector3 p) => p.y;
    public bool GetGroundHeight(Vector3 p, out float height) { height = p.y; return true; }
}
public sealed class Heightmap
{
    public enum Biome { None }
    public enum BiomeArea { Everything }
}
public sealed class WorldGenerator
{
    public static WorldGenerator? instance;
    public float GetHeight(float x, float z) => throw new InvalidOperationException("No world generator is loaded.");
}
public sealed class ZPackage(byte[] data) { public byte[] GetArray() => data; }
public static class Utils
{
    public static byte[] Decompress(byte[] data) => throw new InvalidOperationException("Startup discovery decompressed terrain data.");
    public static byte[] Compress(byte[] data) => throw new InvalidOperationException("Startup discovery compressed terrain data.");
}
namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x = x;
        public float y = y;
        public float z = z;
    }
}
namespace FreshWorld.Engine
{
    internal static class GameWorld
    {
        public static IEnumerable<ZDO> AllZDOs() => throw new InvalidOperationException("Startup discovery enumerated a world.");
    }
}
