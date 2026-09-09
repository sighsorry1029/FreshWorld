using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld.Engine;

/// <summary>Cached access to native placement, preserving shared state if a generation hook throws.</summary>
internal static class NativePlacement
{
    // ClearArea is a private nested game type, so placement uses cached reflection without
    // modifying/publicizing game assemblies or exposing their inaccessible types in our API.
    private static readonly Type ClearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea")
        ?? throw new TypeLoadException("ZoneSystem.ClearArea");
    private static readonly Type ClearAreasType = typeof(List<>).MakeGenericType(ClearAreaType);
    private static readonly ConstructorInfo ClearAreaConstructor = AccessTools.Constructor(ClearAreaType, new[] { typeof(Vector3), typeof(float) })
        ?? throw new MissingMethodException("ZoneSystem.ClearArea constructor");
    private static readonly MethodInfo Vegetation = AccessTools.Method(typeof(ZoneSystem), "PlaceVegetation")
        ?? throw new MissingMethodException("ZoneSystem.PlaceVegetation");
    private static readonly MethodInfo Locations = AccessTools.Method(typeof(ZoneSystem), "PlaceLocations")
        ?? throw new MissingMethodException("ZoneSystem.PlaceLocations");
    private static readonly Func<ZoneSystem, ZoneSystem.ZoneLocation, bool, bool> PokeLocation =
        AccessTools.MethodDelegate<Func<ZoneSystem, ZoneSystem.ZoneLocation, bool, bool>>(
            AccessTools.Method(typeof(ZoneSystem), "PokeCanSpawnLocation") ?? throw new MissingMethodException("ZoneSystem.PokeCanSpawnLocation"));
    private static readonly AccessTools.FieldRef<ZoneSystem, List<GameObject>> Temporary =
        AccessTools.FieldRefAccess<ZoneSystem, List<GameObject>>("m_tempSpawnedObjects");
    private static readonly FieldInfo GhostInit = AccessTools.Field(typeof(ZNetView), "m_ghostInit")
        ?? throw new MissingFieldException("ZNetView.m_ghostInit");
    private static readonly FieldInfo RandomInitialDamage = AccessTools.Field(typeof(WearNTear), "m_randomInitialDamage")
        ?? throw new MissingFieldException("WearNTear.m_randomInitialDamage");
    private static readonly FieldInfo CachedPrefabName = AccessTools.Field(
        AccessTools.Field(typeof(ZoneSystem.ZoneLocation), "m_prefab").FieldType, "m_name")
        ?? throw new MissingFieldException("SoftReference.m_name");

    public static List<GameObject> TemporaryObjects(ZoneSystem zones) => Temporary(zones);
    public static IList CreateClearAreas() => (IList)Activator.CreateInstance(ClearAreasType)!;
    public static object CreateClearArea(Vector3 center, float radius) => ClearAreaConstructor.Invoke(new object[] { center, radius });

    public static bool IsValidLocationPrefab(ZoneSystem.ZoneLocation location) =>
        location != null && (location.m_prefab.IsValid ||
            CachedPrefabName.GetValue(location.m_prefab) is string name && !string.IsNullOrWhiteSpace(name));

    public static bool CanSpawnLocation(ZoneSystem zones, ZoneSystem.ZoneLocation location) =>
        PokeLocation(zones, location, true);

    public static void PlaceVegetation(ZoneSystem zones, Vector2i zone, Transform parent, Heightmap heightmap,
        IList clearAreas, List<GameObject> objects) =>
        Run(() => Vegetation.Invoke(zones, new object[] { zone, ZoneSystem.GetZonePos(zone), parent, heightmap, clearAreas, ZoneSystem.SpawnMode.Ghost, objects }));

    public static void PlaceLocations(ZoneSystem zones, Vector2i zone, Transform parent, Heightmap heightmap,
        IList clearAreas, List<GameObject> objects)
    {
        var template = LocationTemplateState.Capture(zones, zone);
        try
        {
            Run(() => Locations.Invoke(zones, new object[] { zone, ZoneSystem.GetZonePos(zone), parent, heightmap, clearAreas, ZoneSystem.SpawnMode.Ghost, objects }));
        }
        finally { template?.Restore(); }
    }

    private static void Run(Action placement)
    {
        var random = UnityEngine.Random.state;
        var ghost = (bool)GhostInit.GetValue(null)!;
        var randomDamage = (bool)RandomInitialDamage.GetValue(null)!;
        try { placement(); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        finally
        {
            UnityEngine.Random.state = random;
            GhostInit.SetValue(null, ghost);
            RandomInitialDamage.SetValue(null, randomDamage);
        }
    }

    // Native SpawnLocation temporarily moves the source prefab/interior transforms and changes
    // RandomSpawn child activation. Restore that known template state even if generation throws.
    private sealed class LocationTemplateState
    {
        private readonly Transform _root;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly TemplateTransform[] _transforms;

        private LocationTemplateState(GameObject prefab)
        {
            _root = prefab.transform;
            _position = _root.position;
            _rotation = _root.rotation;
            _transforms = prefab.GetComponentsInChildren<Transform>(true).Select(transform => new TemplateTransform(transform)).ToArray();
        }

        public static LocationTemplateState? Capture(ZoneSystem zones, Vector2i zone)
        {
            if (!zones.m_locationInstances.TryGetValue(zone, out var location) || location.m_location == null) return null;
            var reference = location.m_location.m_prefab;
            // Named mod references may supply their own spawn hook. Never request an additional
            // asset load or resolve an unavailable asset merely to construct this optional snapshot.
            if (!reference.IsValid || !reference.IsLoaded) return null;
            var prefab = reference.Asset;
            return prefab != null ? new LocationTemplateState(prefab) : null;
        }

        public void Restore()
        {
            foreach (var transform in _transforms) transform.RestoreTransform();
            if (_root != null) { _root.position = _position; _root.rotation = _rotation; }
            foreach (var transform in _transforms) transform.RestoreActive();
        }

        private sealed class TemplateTransform
        {
            private readonly Transform _transform;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;
            private readonly bool _active;
            public TemplateTransform(Transform transform)
            {
                _transform = transform;
                _position = transform.localPosition;
                _rotation = transform.localRotation;
                _scale = transform.localScale;
                _active = transform.gameObject.activeSelf;
            }
            public void RestoreTransform()
            {
                if (_transform == null) return;
                _transform.localPosition = _position;
                _transform.localRotation = _rotation;
                _transform.localScale = _scale;
            }
            public void RestoreActive()
            {
                if (_transform != null && _transform.gameObject.activeSelf != _active)
                    _transform.gameObject.SetActive(_active);
            }
        }
    }

    public static HashSet<string> RequireIds(HashSet<string> ids)
    {
        if (ids == null || ids.Count == 0 || ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one exact, nonempty prefab ID is required.", nameof(ids));
        return new HashSet<string>(ids, StringComparer.Ordinal);
    }

    public static void DestroyCreatedObjects(List<GameObject> temporary, HashSet<GameObject> original)
    {
        foreach (var obj in temporary.Where(obj => !original.Contains(obj)).ToArray())
        {
            if (obj != null) UnityEngine.Object.Destroy(obj);
            temporary.Remove(obj!);
        }
    }
}
