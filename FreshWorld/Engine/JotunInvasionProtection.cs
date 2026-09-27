namespace FreshWorld.Engine;

/// <summary>Fixed objective prefabs protect their own zones for one run, independent of event metadata.</summary>
internal sealed class JotunInvasionProtection
{
    private const string CorePrefab = "BlackIce_Core";
    private const string OuterPrefab = "BlackIce_Core_outer";
    private static readonly int Core = CorePrefab.GetStableHashCode();
    private static readonly int Outer = OuterPrefab.GetStableHashCode();
    private readonly AlwaysProtectedObjects _zones = new(new[] { CorePrefab, OuterPrefab });

    // Native destruction completes the event and creates its reward. FreshWorld's raw ZDO deletion
    // does neither. Keep this final guard even if event metadata is missing or a spawned link crosses zones.
    internal static bool IsProtectedObject(ZDO zdo) => zdo.GetPrefab() == Core || zdo.GetPrefab() == Outer;

    // Reuse the exact-prefab policy: capture unloaded objectives after saving, see arrivals on
    // target retries, and retain observed zones until this run ends. No event/ice ownership guess.
    public int Capture() => _zones.Capture();
    public bool CanResetZone(Vector2s zone) => _zones.CanResetZone(zone);
}
