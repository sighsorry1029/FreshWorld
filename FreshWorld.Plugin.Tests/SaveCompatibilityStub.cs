namespace FreshWorld.Engine;

internal static class EmptyChunkSaveCompatibility
{
    internal enum Strategy { FreshWorld, Unsupported }
    internal static bool Reject;
    internal static string? DisabledReason { get; set; }
    internal static void Reset() => DisabledReason = null;
    internal static Strategy Check()
    {
        if (Reject) DisabledReason = "injected incompatible save contract";
        return DisabledReason == null ? Strategy.FreshWorld : Strategy.Unsupported;
    }
}
