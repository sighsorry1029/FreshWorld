using System;
using System.Collections.Generic;
using System.Threading;
using FreshWorld.Backend;

namespace FreshWorld.API;

/// <summary>Optional, main-thread exclusion for cooperating host maintenance. Does not save or certify a prior run.</summary>
public static class MaintenanceCoordination
{
    public static int ContractVersion => 2;
    public static string[] Capabilities => new[] { "atomic-lease-v1", "main-thread-v1", "lease-validation-v1" };
    private static int mainThread;
    private static string world = "", session = "";
    private static Func<bool>? ready;
    private static Action? beforeMaintenance;
    private static Func<bool>? cleanupPending;
    private static IDisposable? externalLease;
    private static string owner = "", token = "";
    internal static bool HasExternalLease => externalLease != null;

    internal static void Initialize(Func<bool> isReady, Action prepare, Func<bool> hasPendingCleanup)
    {
        mainThread = Thread.CurrentThread.ManagedThreadId;
        CloseSession();
        ready = isReady;
        beforeMaintenance = prepare;
        cleanupPending = hasPendingCleanup;
    }

    internal static void Attach(string worldId)
    {
        CloseSession();
        world = worldId;
        session = Guid.NewGuid().ToString("N");
    }

    internal static void CloseSession()
    {
        externalLease?.Dispose();
        externalLease = null;
        owner = token = world = session = "";
    }

    internal static void Shutdown()
    {
        CloseSession();
        ready = null;
        beforeMaintenance = null;
        cleanupPending = null;
        mainThread = 0;
    }

    private static bool OnMainThread => mainThread != 0 && Thread.CurrentThread.ManagedThreadId == mainThread;
    private static bool Available
    {
        get
        {
            if (!OnMainThread || world.Length == 0 || session.Length == 0) return false;
            try { return ready?.Invoke() == true; }
            catch { return false; }
        }
    }

    public static Dictionary<string, string> TryAcquire(string worldId, string leaseOwner)
    {
        if (!OnMainThread) return Failure("Rejected", "WrongThread");
        if (!Available) return Failure("Rejected", "ProviderNotReady");
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 256)
            return Failure("Rejected", "InvalidRequest");
        if (!string.Equals(worldId, world, StringComparison.Ordinal)) return Failure("Rejected", "WrongWorld");
        if (!MaintenanceGate.IsAvailable) return Failure("Busy", "Busy");
        try { if (cleanupPending?.Invoke() == true) return Failure("Busy", "CleanupPending"); }
        catch { return Failure("Rejected", "ProviderError"); }
        IDisposable? lease = null;
        string capturedSession = session;
        try
        {
            // Check and claim on the same game-thread turn. Native/mod hooks may run
            // during preparation, so recheck the session before publishing ownership.
            lease = MaintenanceGate.Acquire();
            beforeMaintenance!();
            if (!Available || session != capturedSession)
                throw new InvalidOperationException("World session changed during lease preparation.");
            token = Guid.NewGuid().ToString("N");
            owner = leaseOwner;
            externalLease = lease;
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = "Acquired", ["worldId"] = world,
                ["sessionId"] = session, ["leaseToken"] = token
            };
        }
        catch
        {
            lease?.Dispose();
            return Failure("Rejected", "PreparationFailed");
        }
    }

    public static bool ValidateLease(string worldId, string sessionId, string leaseOwner, string leaseToken) =>
        Available && externalLease != null && MaintenanceGate.IsActive && Matches(worldId, sessionId, leaseOwner, leaseToken);

    public static bool ReleaseLease(string worldId, string sessionId, string leaseOwner, string leaseToken)
    {
        // Readiness may fail during external work. Its owner must still be able to
        // release after actual cleanup, but no timeout or another session can unlock it.
        if (!OnMainThread || externalLease == null || !Matches(worldId, sessionId, leaseOwner, leaseToken)) return false;
        externalLease.Dispose();
        externalLease = null;
        owner = token = "";
        return true;
    }

    private static bool Matches(string worldId, string sessionId, string leaseOwner, string leaseToken) =>
        worldId == world && sessionId == session && leaseOwner == owner && leaseToken == token;
    private static Dictionary<string, string> Failure(string status, string error) =>
        new(StringComparer.Ordinal) { ["status"] = status, ["errorCode"] = error };
}
