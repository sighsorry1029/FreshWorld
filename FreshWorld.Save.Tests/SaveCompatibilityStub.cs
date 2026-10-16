namespace FreshWorld.Engine;

// This .NET 8 harness exercises standalone chunk selection against original game DLLs.
// Actual Harmony patch inspection/admission is tested by the opt-in Mono integration harness.
internal static class EmptyChunkSaveCompatibility
{
    internal enum Strategy { FreshWorld }
    internal static Strategy Check() => Strategy.FreshWorld;
}
