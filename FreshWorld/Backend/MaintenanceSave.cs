using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace FreshWorld.Backend;

/// <summary>Shared pre-operation save barrier. A completed worker is required before world changes.</summary>
internal static class MaintenanceSave
{
    public static IEnumerator Run(ZNet network, ZoneSystem zones, Action requireWorld, Action<string> log, string reason, float timeoutSeconds)
    {
        requireWorld();
        var waiting = Stopwatch.StartNew();
        yield return WaitUntilUnpaused(requireWorld, waiting);
        while (network.IsSaving())
        {
            yield return WaitUntilUnpaused(requireWorld, waiting);
            requireWorld();
            CheckSaveTimeout(waiting, timeoutSeconds);
            yield return null;
        }
        if (ZNet.m_loadError || zones.SkipSaving() || DungeonDB.instance == null || DungeonDB.instance.SkipSaving())
            throw new InvalidOperationException("World saving is disabled or the game reported a load error; maintenance cannot continue.");

        string? saveError = null;
        var observedStart = false;
        Action started = () => observedStart = true;
        Application.LogCallback logged = (message, trace, type) =>
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
                message.IndexOf("sav", StringComparison.OrdinalIgnoreCase) >= 0)
                Interlocked.CompareExchange(ref saveError, message, null);
        };
        ZNet.WorldSaveStarted += started;
        Application.logMessageReceivedThreaded += logged;
        try
        {
            log($"Requesting world save {reason}.");
            var previousDoneTime = network.SaveDoneTime;
            network.Save(false);
            if (!observedStart && !network.IsSaving() && network.SaveDoneTime == previousDoneTime)
                throw new InvalidOperationException("The world save request did not start.");
            waiting.Restart();
            do
            {
                yield return WaitUntilUnpaused(requireWorld, waiting);
                requireWorld();
                var error = Volatile.Read(ref saveError);
                if (error != null) throw new InvalidOperationException("World save reported an error: " + error);
                CheckSaveTimeout(waiting, timeoutSeconds);
                yield return null;
            } while (network.IsSaving());
            var finalError = Volatile.Read(ref saveError);
            if (finalError != null) throw new InvalidOperationException("World save reported an error: " + finalError);
            log($"World save worker finished {reason} ({waiting.Elapsed.TotalSeconds:F1}s).");
        }
        finally
        {
            Application.logMessageReceivedThreaded -= logged;
            ZNet.WorldSaveStarted -= started;
        }
    }


    private static void CheckSaveTimeout(Stopwatch waiting, float timeoutSeconds)
    {
        if (waiting.Elapsed.TotalSeconds > timeoutSeconds)
            throw new TimeoutException($"World save did not finish within {timeoutSeconds}s.");
    }

    private static IEnumerator WaitUntilUnpaused(Action requireWorld, Stopwatch waiting)
    {
        while (Time.timeScale <= 0f)
        {
            requireWorld(); waiting.Stop(); yield return null;
        }
        waiting.Start();
    }
}
