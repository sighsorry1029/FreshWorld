// Engine/storage boundaries for the real terrain adapter and codec. Compression is identity here;
// these checks do not execute Unity, native terrain refresh, or the game's compression library.
using System;
using System.Collections.Generic;
using UnityEngine;

public readonly record struct Vector2s(short x, short y)
{
    public Vector2s(int x, int y) : this((short)x, (short)y) { }
}
public static class HashExtensions { public static int GetStableHashCode(this string s) => StringComparer.Ordinal.GetHashCode(s); }
public static class ZDOVars { public const int s_TCData = 1; }
public class ZDO(Vector3 position, byte[] data)
{
    public byte[] Data = data;
    public int Writes;
    public long Owner;
    public int GetPrefab() => "_TerrainCompiler".GetStableHashCode();
    public Vector3 GetPosition() => position;
    public byte[] GetByteArray(int key) => Data;
    public bool IsOwner() => Owner == ZDOMan.GetSessionID();
    public void SetOwner(long owner) => Owner = owner;
    public void Set(int key, byte[] value) { Data = value; Writes++; }
}
public class ZDOMan { public static ZDOMan instance = new(); public static long GetSessionID() => 42; }
public class ZNet
{
    public static ZNet instance = new();
    public bool HaveStopped;
    public bool IsServer() => true;
}
public class ZoneSystem
{
    public static ZoneSystem instance = new();
    public static Vector2s GetZone(Vector3 p) => new((int)Math.Floor((p.x + 32) / 64), (int)Math.Floor((p.z + 32) / 64));
    public static Vector3 GetZonePos(Vector2s zone) => new(zone.x * 64, 0, zone.y * 64);
    public void GetGroundData() { }
    public float GetGroundHeight(Vector3 p) => 0;
}
public class WorldGenerator { public static WorldGenerator instance = new(); public float GetHeight(float x, float z) => 0; }
public class ZPackage(byte[] data) { public byte[] GetArray() => data; }
public static class Utils
{
    public static byte[] Compress(byte[] data) => data;
    public static byte[] Decompress(byte[] data) => data;
}
namespace UnityEngine { public struct Vector3(float x, float y, float z) { public float x = x, y = y, z = z; } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(string method, params Type[] arguments) { }
    }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
}
namespace FreshWorld.Engine
{
    public static class GameWorld
    {
        public static readonly List<ZDO> Objects = new();
        public static IEnumerable<ZDO> AllZDOs() => Objects;
    }
}
