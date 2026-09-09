using System;

namespace FreshWorld.Backend;

/// <summary>Main-thread lease shared by scheduled and manual FreshWorld maintenance.</summary>
internal static class MaintenanceGate
{
    private static Lease? active;
    public static bool IsActive => active != null;
    public static bool IsAvailable => !IsActive;
    public static IDisposable Acquire()
    {
        if (!IsAvailable) throw new InvalidOperationException("FreshWorld maintenance is already running.");
        return active = new Lease();
    }
    private sealed class Lease : IDisposable
    {
        public void Dispose() { if (ReferenceEquals(active, this)) active = null; }
    }
}
