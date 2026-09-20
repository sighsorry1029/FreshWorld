using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using FreshWorld;
using FreshWorld.Backend;
using FreshWorld.Commands;
using FreshWorld.Core;
using UnityEngine;

internal static class Program
{
    private static int passed;

    private static int Main()
    {
        try
        {
            Test("startup submits only declared plugin patches before a world loads", StartupPatchSelection);
            Test("plugin metadata supplies the standard config filename and shared live settings", MetadataConfigIdentity);
            Test("manual dispatch bypasses scheduler polling interval", ImmediateDispatch);
            Test("automatic work polls every second without duplicate execution", FixedSchedulePolling);
            Test("hot reload applies before the next automatic poll", ReloadBeforeScheduledPoll);
            Test("synced in-memory edits apply without rereading the cfg", SyncedMemoryEdit);
            Test("synced edits wait for the active reset to finish", SyncedEditDuringRun);
            Test("disabled automatic scheduling suppresses automatic work while retaining manual commands", AutomaticDisabledCommands);
            Test("switching to disabled automatic scheduling drops pending automatic work without later catch-up", DisableCancelsAutomaticPending);
            Test("inactive daily times reload preserves the game-day scheduler and anchor", InactiveDailyTimesPreserveAnchor);
            Test("inactive schedule values preserve an authorized pending manual command", InactiveSchedulePreservesManualPending);
            Test("disabled automatic maintenance status still advertises manual commands", DisabledStatus);
            Test("invalid current host configuration rejects a manual request", InvalidConfigurationRejects);
            Test("unready world rejects a manual request", UnreadyRejects);
            Test("invalid request authority or session rejects before reservation", InvalidContextRejects);
            Test("startup delay queues manual work and rejects duplicates", StartupWaits);
            Test("pause and save gates queue manual work without mutations", PauseAndSaveWait);
            Test("backend lease gates queued manual work", BackendLeaseWaits);
            Test("server configuration snapshot controls all manual stages", CapturedServerOptions);
            Test("pending scheduled run cannot be replaced by manual command", ScheduledPendingWins);
            Test("running manual request rejects a second command", RunningRejectsDuplicate);
            Test("queued authority revocation cancels durable request before mutation", QueuedRevocation);
            Test("authority revoked after dispatch cancels before the backend first advances", DispatchRevocation);
            Test("active authority revocation cancels before the next mutation", ActiveRevocation);
            Test("authority revoked after partial progress preserves prior changes and stops further work", PartialRevocation);
            Test("queued old-world request is canceled during world switch", QueuedWorldSwitch);
            Test("same-world network replacement cancels queued and active requests before fresh-session work", SameWorldNetworkReplacement);
            Test("disabling automatic maintenance preserves an accepted manual request and its options", DisablePreservesManualPending);
            Test("reenabling automatic maintenance preserves an accepted manual request", EnablePreservesManualPending);
            Test("automatic disable deferred during a run applies before the next scheduled admission", DisableDuringActiveRun);
            Test("schedule persistence failure without a manual request remains a session fault after storage recovers", ReconfigureFailureRemainsFaulted);
            Test("unstarted manual request from previous process cannot resume without authority", PreviousProcessRequest);
            Test("completion is persisted and reported to requester", CompletionNotification);
            Test("backend failure is persisted and reported without retry", FailureNotification);
            Test("status reports idle policy and the last result without reserving or dispatching maintenance", ReadOnlyStatus);
            Test("plugin shutdown unregisters command and releases backend lease", ShutdownCleansUp);
            Test("plugin shutdown continues after configuration watcher disposal fails", ShutdownSurvivesWatcherFailure);
            System.Console.WriteLine($"Plugin/controller tests passed: {passed}");
            return 0;
        }
        catch (Exception error)
        {
            System.Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Test(string name, Action action)
    {
        action(); ++passed;
        System.Console.WriteLine("PASS " + name);
    }

    private static void StartupPatchSelection()
    {
        using var f = new Fixture(beforeAwake: (_, _, _) =>
        {
            ZNet.instance = null!;
            ZNet.World = null;
        });
        var submitted = f.GetField<HarmonyLib.Harmony>("harmony")!.PatchedTypes;
        // These are the two production patch containers linked into this controller harness.
        // Ordinary helpers (including cleanup methods) and the embedded-library fixture must be absent.
        True(submitted.Count == 2 && submitted.Contains(typeof(ShutdownPatch)) &&
            submitted.Contains(typeof(ShutdownWithoutSavePatch)),
            "unexpected startup patch submissions: " + string.Join(", ", submitted.Select(type => type.FullName)));
        True(FreshWorldCommands.Handler != null, "startup did not reach command registration");
        True(f.Scheduler == null && MaintenancePipeline.Created.Count == 0,
            "pre-world initialization must not start maintenance");
    }

    private static void SyncedMemoryEdit()
    {
        using var f = new Fixture();
        var reloads = f.Plugin.Config.ReloadCount;
        f.Plugin.Config.Set("Reset", "Zones", "false");
        f.SetField("configValuesChanged", 1); // Transport has already applied the complete edit.
        f.Tick();
        Equal(reloads, f.Plugin.Config.ReloadCount, "memory edit must not reread an older cfg");
        True(!f.GetField<FreshWorld.Configuration.RuntimeSettings>("settings")!.Options.ZonesEnabled, "synced policy was not captured");
    }

    private static void SyncedEditDuringRun()
    {
        using var f = new Fixture();
        MaintenancePipeline.HoldFrames = 3;
        f.Run(); f.Tick();
        f.Plugin.Config.Set("Reset", "Zones", "false");
        f.SetField("configValuesChanged", 1);
        f.Tick();
        True(f.GetField<FreshWorld.Configuration.RuntimeSettings>("settings")!.Options.ZonesEnabled, "policy changed in the middle of a reset");
        True(MaintenancePipeline.Created.Single().Options.ZonesEnabled, "backend snapshot changed");
        for (var i = 0; i < 4; i++) f.Tick();
        True(!f.GetField<FreshWorld.Configuration.RuntimeSettings>("settings")!.Options.ZonesEnabled, "deferred policy was not applied after completion");
    }

    private static void MetadataConfigIdentity()
    {
        using var f = new Fixture();
        var metadata = typeof(FreshWorldPlugin).GetCustomAttribute<BepInPlugin>()!;
        Equal("sighsorry.FreshWorld", metadata.Guid, "author-qualified plugin GUID");
        Equal("FreshWorld", metadata.Name, "mod name remains unchanged");
        Equal("FreshWorld", typeof(FreshWorldPlugin).Namespace, "namespace follows the mod name");
        Equal(FreshWorldPlugin.ModVersion, metadata.Version, "plugin metadata uses the shared version constant");
        BaseUnityPlugin pluginView = f.Plugin;
        var config = pluginView.Config;
        True(ReferenceEquals(config, f.Plugin.Config), "Configuration Manager and the controller must share the standard base Config");
        True(typeof(FreshWorldPlugin).GetProperty("Config", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) == null,
            "a shadow Config property would hide settings from Configuration Manager");
        var expectedPath = Path.Combine(Paths.ConfigPath, "sighsorry.FreshWorld.cfg");
        Equal(expectedPath, config.ConfigFilePath, "standard cfg filename");
        True(config.IsBound("General", "Enabled") && config.IsBound("Reset", "TerrainResourceIds") &&
            config.IsBound("Protection", "PieceBlacklist"), "settings are exposed on the base Config instance");
        var watcher = (FileSystemWatcher)typeof(FreshWorldPlugin).GetField("watcher", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Plugin)!;
        Equal("sighsorry.FreshWorld.cfg", watcher.Filter, "hot reload watches the metadata-derived filename");
        Equal(Path.GetFullPath(Paths.ConfigPath), Path.GetFullPath(watcher.Path), "hot reload watches the config directory");
        True(f.Plugin.Logger.Messages.Any(message => message.Contains(expectedPath)), "startup identifies the active cfg path");

        var reloads = config.ReloadCount;
        config.Set("Reset", "ResourceTerrainRadius", "7.5");
        f.SetField("reloadRequested", 1);
        f.Tick();
        Equal(reloads + 1, config.ReloadCount, "live reload uses the standard Config instance");
        f.Run();
        Equal(7.5f, MaintenancePipeline.Created.Single().Options.VegetationTerrainRadius,
            "manual dispatch sees changes made through the Configuration Manager-facing Config");
    }

    private static void ImmediateDispatch()
    {
        using var f = new Fixture();
        f.SetField("nextPoll", 1000f);
        f.Run();
        Equal(1, MaintenancePipeline.Created.Count, "manual command must dispatch before next poll");
        Equal(0f, Time.realtimeSinceStartup, "test must not advance real time");
        f.Tick();
        Equal(1, MaintenancePipeline.Mutations, "next controller update advances accepted work");
    }

    private static void FixedSchedulePolling()
    {
        using var f = new Fixture();
        f.Net.Seconds = 24 * EnvMan.instance!.m_dayLengthSec;
        Time.realtimeSinceStartup = 0.5f;
        f.Tick();
        Time.realtimeSinceStartup = 0.999f;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "automatic work waits for the one-second poll boundary");
        Time.realtimeSinceStartup = 1;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "due work dispatches at one second");
        f.Tick();
        Equal(RunStatus.Completed, f.Scheduler!.Attempts.Single().Status, "automatic work completes once");
        Time.realtimeSinceStartup = 2;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "subsequent polls cannot replay the completed deadline");
        Equal(1, MaintenancePipeline.Mutations, "one backend execution for one deadline");
    }

    private static void ReloadBeforeScheduledPoll()
    {
        using var f = new Fixture();
        f.Net.Seconds = 24 * EnvMan.instance!.m_dayLengthSec;
        Time.realtimeSinceStartup = 0.25f;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "fixture remains before its next automatic poll");
        int previousReloads = f.Plugin.Config.ReloadCount;
        f.Plugin.Config.Set("Reset", "Zones", "false");
        f.Plugin.Config.Set("Reset", "Locations", "true");
        // Model the file watcher's event flag; parsing and capture execute through the real controller.
        f.SetField("reloadRequested", 1);
        f.Tick();
        Equal(previousReloads + 1, f.Plugin.Config.ReloadCount, "file reload processed on the next update before the poll deadline");
        Equal(0.25f, Time.realtimeSinceStartup, "hot reload adds no polling wait");
        var dispatched = MaintenancePipeline.Created.Single();
        True(!dispatched.Options.ZonesEnabled && dispatched.Options.VegetationEnabled && dispatched.Options.LocationsEnabled,
            "due automatic work receives the newly applied stage policy");
        Equal(0, f.Plugin.Config.SaveCount, "hot reload does not rewrite the cfg");
    }

    private static void AutomaticDisabledCommands()
    {
        using var f = new Fixture(c => c.Set("General", "Enabled", "false"));
        f.Net.Seconds = 300 * EnvMan.instance!.m_dayLengthSec;
        Time.realtimeSinceStartup = 1;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "elapsed game days cannot trigger disabled automatic scheduling maintenance");
        True(f.Scheduler!.PendingRun == null, "disabled automatic scheduling does not reserve automatic work");
        f.Run();
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "disabled automatic scheduling accepts an authorized manual command");
        Equal("Manual", f.Scheduler.Attempts.Single().Run.Slot, "manual work keeps its command origin");
        Equal(RunStatus.Completed, f.Scheduler.Attempts.Single().Status, "disabled automatic scheduling run persists completion");
        f.Net.Seconds = 600 * EnvMan.instance.m_dayLengthSec;
        Time.realtimeSinceStartup = 2;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "manual completion cannot enable automatic work");
    }

    private static void DisableCancelsAutomaticPending()
    {
        using var f = new Fixture(skipStartupDelay: false);
        f.Net.Seconds = 24 * EnvMan.instance!.m_dayLengthSec;
        Time.realtimeSinceStartup = 1;
        f.Tick();
        string abandoned = f.Scheduler!.PendingRun!.Id;
        Equal(0, MaintenancePipeline.Created.Count, "startup gate leaves automatic work pending");
        f.Plugin.Config.Set("General", "Enabled", "false");
        f.SetField("reloadRequested", 1);
        f.Tick();
        True(f.Scheduler!.PendingRun == null, "switching to disabled automatic scheduling removes pending automatic work");
        f.Net.Seconds = 300 * EnvMan.instance.m_dayLengthSec;
        Time.realtimeSinceStartup = 60;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "disabled automatic scheduling cannot resume the abandoned automatic run");
        f.Plugin.Config.Set("General", "Enabled", "true");
        f.SetField("reloadRequested", 1);
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "returning to GameDays anchors at the current game day without catch-up");
        f.Net.Seconds = 323 * EnvMan.instance.m_dayLengthSec;
        Time.realtimeSinceStartup = 61;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "new automatic interval must fully elapse");
        f.Net.Seconds = 324 * EnvMan.instance.m_dayLengthSec;
        Time.realtimeSinceStartup = 62;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "future automatic deadline still works after leaving disabled automatic scheduling");
        True(f.Scheduler!.ActiveRun!.Run.Id != abandoned, "abandoned deadline identity cannot be replayed");
    }

    private static void InactiveDailyTimesPreserveAnchor()
    {
        using var f = new Fixture();
        var original = f.Scheduler!;
        f.Net.Seconds = 10 * EnvMan.instance!.m_dayLengthSec;
        Time.realtimeSinceStartup = 1;
        f.Tick();
        f.Plugin.Config.Set("General", "DailyTimes", "unused and malformed");
        f.SetField("reloadRequested", 1);
        f.Tick();
        True(ReferenceEquals(original, f.Scheduler), "inactive DailyTimes cannot replace the GameDays scheduler");
        f.Net.Seconds = 24 * EnvMan.instance.m_dayLengthSec;
        Time.realtimeSinceStartup = 2;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "original day-zero anchor is preserved across inactive config reload");
    }

    private static void InactiveSchedulePreservesManualPending()
    {
        using var f = new Fixture(c => c.Set("General", "Enabled", "false"), skipStartupDelay: false);
        f.Run();
        var original = f.Scheduler!;
        string requested = original.PendingRun!.Id;
        f.Plugin.Config.Set("General", "GameDayInterval", "unused and malformed");
        f.Plugin.Config.Set("General", "DailyTimes", "also unused");
        f.SetField("reloadRequested", 1);
        f.Tick();
        True(ReferenceEquals(original, f.Scheduler), "inactive disabled automatic scheduling settings do not close its scheduler");
        Equal(requested, f.Scheduler!.PendingRun!.Id, "pending authorized command survives inactive config reload");
        Time.realtimeSinceStartup = 30;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "the retained command starts when its original startup gate expires");
        Equal(requested, f.Scheduler.ActiveRun!.Run.Id, "retained request identity is dispatched exactly once");
    }

    private static void DisabledStatus()
    {
        using var f = new Fixture(c => c.Set("General", "Enabled", "false"));
        f.Plugin.HandleCommand(FreshWorldCommandAction.Status, f.Request);
        HasReply(f.Request, "Automatic=False");
        HasReply(f.Request, "manual commands available");
        Equal(0, MaintenancePipeline.Created.Count, "status never dispatches a reset");
    }

    private static void InvalidConfigurationRejects()
    {
        using var f = new Fixture();
        f.Plugin.Config.Set("Protection", "LocationSafeZones", "invalid");
        f.Run();
        Equal(0, MaintenancePipeline.Created.Count, "fresh invalid cfg must not fall back to last known valid options");
        True(f.Plugin.Config.ReloadCount > 0, "manual command must read current host cfg");
        HasReply(f.Request, "cfg");
    }

    private static void UnreadyRejects()
    {
        using var f = new Fixture();
        ZoneSystem.instance!.LocationsGenerated = false;
        f.Run();
        Equal(0, MaintenancePipeline.Created.Count, "unready world dispatch");
        True(f.Scheduler?.PendingRun == null, "unready request must not become durable work");
        True(f.Request.Replies.Count != 0, "unready request response");
    }

    private static void InvalidContextRejects()
    {
        using var f = new Fixture();
        f.Request.Authorized = false;
        f.Run();
        var otherWorld = new CommandRequestContext(f.Net, f.Net.Uid + 1);
        f.Run(otherWorld);
        var otherNetwork = new CommandRequestContext(new ZNet { Uid = f.Net.Uid }, f.Net.Uid);
        f.Run(otherNetwork);
        Equal(0, MaintenancePipeline.Created.Count, "invalid contexts dispatch");
        True(f.Scheduler?.PendingRun == null, "invalid contexts durable reservation");
    }

    private static void StartupWaits()
    {
        using var f = new Fixture(skipStartupDelay: false);
        f.Run();
        string pending = f.Scheduler!.PendingRun!.Id;
        Equal(0, MaintenancePipeline.Created.Count, "startup delay must defer dispatch");
        var second = new CommandRequestContext(f.Net, f.Net.Uid);
        f.Run(second);
        Equal(pending, f.Scheduler.PendingRun!.Id, "duplicate must preserve pending request identity");
        HasReply(second, "pending");
        Time.realtimeSinceStartup = 29;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "startup delay lower bound");
        Time.realtimeSinceStartup = 30;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "dispatch at ready time without polling delay");
    }

    private static void PauseAndSaveWait()
    {
        using var f = new Fixture();
        Time.timeScale = 0;
        f.Run();
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "paused dispatch");
        Time.timeScale = 1;
        f.Net.Saving = true;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "saving dispatch");
        f.Net.Saving = false;
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "unpause and save completion release pending command");
    }

    private static void BackendLeaseWaits()
    {
        using var f = new Fixture();
        using (MaintenanceGate.Acquire())
        {
            f.Run();
            f.Tick();
            Equal(0, MaintenancePipeline.Created.Count, "other backend lease prevents dispatch");
        }
        f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "released backend lease permits pending command");
    }

    private static void CapturedServerOptions()
    {
        using var f = new Fixture(c =>
        {
            c.Set("Reset", "Zones", "false");
            c.Set("Reset", "Resources", "true");
            c.Set("Reset", "ResourceIds", "Beech1");
            c.Set("Reset", "TerrainResourceIds", "silvervein");
            c.Set("Reset", "ResourceTerrainRadius", "20");
            c.Set("Reset", "Locations", "true");
            c.Set("Protection", "LocationSafeZones", "0");
            c.Set("Protection", "PieceBlacklist", "piece_workbench,custom_marker");
            c.Set("Reset", "LocationIds", "Hildir_cave");
        }, skipStartupDelay: false);
        f.Run();
        f.Plugin.Config.Set("Reset", "Zones", "true");
        f.Plugin.Config.Set("Reset", "Resources", "false");
        f.Plugin.Config.Set("Reset", "ResourceIds", "Birch1");
        f.Plugin.Config.Set("Reset", "TerrainResourceIds", "rock4_copper");
        f.Plugin.Config.Set("Reset", "ResourceTerrainRadius", "0");
        f.Plugin.Config.Set("Reset", "Locations", "false");
        f.Plugin.Config.Set("Protection", "LocationSafeZones", "1");
        f.Plugin.Config.Set("Protection", "PieceBlacklist", "");
        Time.realtimeSinceStartup = 30;
        f.Tick();
        var dispatched = MaintenancePipeline.Created.Single();
        True(dispatched.IncludeVegetation, "manual work includes the enabled resource stage");
        True(!dispatched.Options.ZonesEnabled && dispatched.Options.VegetationEnabled && dispatched.Options.LocationsEnabled,
            "accepted host stage snapshot must survive cfg value mutation before dispatch");
        Equal(0, dispatched.Options.LocationSafeZones, "accepted host location protection policy");
        Equal("Beech1", dispatched.Options.VegetationIds.Single(), "accepted vegetation-only list");
        Equal("silvervein", dispatched.Options.TerrainVegetationIds.Single(), "accepted terrain resource list");
        Equal(20f, dispatched.Options.VegetationTerrainRadius, "accepted terrain resource radius");
        True(dispatched.Options.PieceBlacklist.SequenceEqual(new[] { "piece_workbench", "custom_marker" }),
            "accepted blacklist survives cfg edits before dispatch");
        Equal("Hildir_cave", dispatched.Options.LocationIds.Single(), "accepted exact location list");
    }

    private static void ScheduledPendingWins()
    {
        using var f = new Fixture(skipStartupDelay: false);
        f.Net.Seconds = 24 * EnvMan.instance!.m_dayLengthSec;
        f.SetField("nextPoll", 0f);
        f.Tick();
        var scheduled = f.Scheduler!.PendingRun!;
        True(scheduled.Slot != "Manual", "fixture must observe scheduled work first");
        f.Run();
        Equal(scheduled.Id, f.Scheduler.PendingRun!.Id, "manual request must not replace scheduled work");
        Equal(0, MaintenancePipeline.Created.Count, "startup gate remains enforced");
        HasReply(f.Request, "pending");
    }

    private static void RunningRejectsDuplicate()
    {
        using var f = new Fixture();
        MaintenancePipeline.HoldFrames = 5;
        f.Run();
        f.Tick();
        string runId = f.Scheduler!.ActiveRun!.Run.Id;
        var second = new CommandRequestContext(f.Net, f.Net.Uid);
        f.Run(second);
        Equal(1, MaintenancePipeline.Created.Count, "duplicate active dispatch");
        Equal(runId, f.Scheduler.ActiveRun!.Run.Id, "active run identity");
        HasReply(second, "running");
    }

    private static void QueuedRevocation()
    {
        using var f = new Fixture(skipStartupDelay: false);
        f.Run();
        string requested = f.Scheduler!.PendingRun!.Id;
        f.Request.Authorized = false;
        Time.realtimeSinceStartup = 30;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "revoked queued request must not dispatch");
        True(f.Scheduler.PendingRun == null, "revoked request removed from pending state");
        var record = f.Scheduler.Attempts.Single(a => a.Run.Id == requested);
        Equal(RunStatus.Interrupted, record.Status, "revoked queued request cancellation is durable");
    }

    private static void QueuedWorldSwitch()
    {
        using var f = new Fixture(skipStartupDelay: false);
        f.Run();
        var oldScheduler = f.Scheduler!;
        string requested = oldScheduler.PendingRun!.Id;
        ZNet.instance = new ZNet { Uid = f.Net.Uid + 1 };
        Time.realtimeSinceStartup = 30;
        f.Tick();
        Equal(0, MaintenancePipeline.Created.Count, "old-world pending request must not enter new world");
        Equal(RunStatus.Interrupted, oldScheduler.Attempts.Single(a => a.Run.Id == requested).Status,
            "old-world request canceled durably");
    }

    private static void SameWorldNetworkReplacement()
    {
        foreach (var active in new[] { false, true })
        {
            using var f = new Fixture(skipStartupDelay: active);
            MaintenancePipeline.MutateBeforeYield = active;
            MaintenancePipeline.HoldFrames = 1;
            f.Run();
            var oldScheduler = f.Scheduler!;
            var oldRun = active ? oldScheduler.ActiveRun!.Run : oldScheduler.PendingRun!;
            if (active) f.Tick();
            var initialMutations = active ? 1 : 0;
            Equal(initialMutations, MaintenancePipeline.Mutations, "fixture progress before session replacement");

            // Reopening the same world must not preserve the old network instance's request authority.
            var replacement = new ZNet { Uid = f.Net.Uid };
            ZNet.instance = replacement;
            Time.realtimeSinceStartup = 30;
            f.Tick();

            var expectedStatus = active ? RunStatus.Failed : RunStatus.Interrupted;
            Equal(expectedStatus, oldScheduler.Attempts.Single(a => a.Run.Id == oldRun.Id).Status,
                "old request is stopped in its original scheduler");
            True(!ReferenceEquals(oldScheduler, f.Scheduler), "same UID still attaches a new host session");
            Equal(expectedStatus, f.Scheduler!.Attempts.Single(a => a.Run.Id == oldRun.Id).Status,
                "new session reopens the same durable world history");
            Equal(initialMutations, MaintenancePipeline.Mutations, "replacement must not advance the old operation");
            True(MaintenanceGate.IsAvailable, "replacement releases the old operation's lease");

            f.Run();
            HasReply(f.Request, "authority is no longer valid");
            f.Run(new CommandRequestContext(replacement, replacement.Uid));
            True(f.Scheduler.PendingRun != null && f.Scheduler.PendingRun.Id != oldRun.Id,
                "the new session can admit its own authenticated request");
            Equal(initialMutations, MaintenancePipeline.Created.Count,
                "new session observes its own startup grace before dispatch");
        }
    }

    private static void ActiveRevocation()
    {
        using var f = new Fixture();
        MaintenancePipeline.HoldFrames = 1;
        f.Run();
        f.Tick();
        Equal(0, MaintenancePipeline.Mutations, "fixture waits before its destructive boundary");
        f.Request.Authorized = false;
        f.Tick();
        Equal(0, MaintenancePipeline.Mutations, "revocation must cancel the active coroutine before it continues");
        Equal(RunStatus.Failed, f.Scheduler!.Attempts.Single().Status, "active revoked run persisted as failure");
        True(MaintenanceGate.IsAvailable, "revocation releases active backend lease");
    }

    private static void DispatchRevocation()
    {
        using var f = new Fixture();
        f.Run();
        Equal(1, MaintenancePipeline.Created.Count, "fixture command admitted and pipeline constructed");
        f.Request.Authorized = false;
        f.Tick();
        Equal(0, MaintenancePipeline.Mutations, "revoke between dispatch and first iterator advance prevents all mutations");
        Equal(RunStatus.Failed, f.Scheduler!.Attempts.Single().Status, "admitted but revoked run is recorded");
        True(MaintenanceGate.IsAvailable, "pre-advance revocation releases lease");
    }

    private static void PartialRevocation()
    {
        using var f = new Fixture();
        MaintenancePipeline.MutateBeforeYield = true;
        MaintenancePipeline.HoldFrames = 1;
        f.Run(); f.Tick();
        Equal(1, MaintenancePipeline.Mutations, "fixture performed initial backend mutation");
        f.Request.Authorized = false;
        f.Tick();
        Equal(1, MaintenancePipeline.Mutations, "previous changes remain but later mutation is canceled");
        Equal(1, MaintenancePipeline.Disposals, "partially advanced backend is cleaned up once");
        Equal(RunStatus.Failed, f.Scheduler!.Attempts.Single().Status, "partial interruption is failed, not completed");
    }

    private static void DisablePreservesManualPending()
    {
        using var f = new Fixture(c => c.Set("Reset", "ResourceIds", "Beech1"), skipStartupDelay: false);
        f.Run();
        var prior = f.Scheduler!;
        string requested = prior.PendingRun!.Id;
        f.Plugin.Config.Set("General", "Enabled", "false");
        f.Plugin.Config.Set("Reset", "ResourceIds", "Birch1");
        f.SetField("reloadRequested", 1);
        f.Tick();
        True(ReferenceEquals(prior, f.Scheduler), "automatic toggle must not close the manual request's scheduler");
        Equal(requested, f.Scheduler!.PendingRun!.Id, "the authorized request survives automatic disable");
        Equal(0, f.Scheduler.Attempts.Count, "the retained request is not recorded as cancelled");
        Time.realtimeSinceStartup = 30;
        f.Tick();
        var dispatched = MaintenancePipeline.Created.Single();
        Equal("Beech1", dispatched.Options.VegetationIds.Single(), "accepted manual resource snapshot survives reload");
        Equal(requested, f.Scheduler.ActiveRun!.Run.Id, "the same request starts while automatic work remains disabled");
        f.Tick();
        Equal(RunStatus.Completed, f.Scheduler.Attempts.Single().Status, "manual completion is persisted while disabled");
    }

    private static void EnablePreservesManualPending()
    {
        using var f = new Fixture(c => c.Set("General", "Enabled", "false"), skipStartupDelay: false);
        f.Run();
        string requested = f.Scheduler!.PendingRun!.Id;
        f.Net.Seconds = 300 * EnvMan.instance!.m_dayLengthSec;
        f.Plugin.Config.Set("General", "Enabled", "true");
        f.SetField("reloadRequested", 1);
        f.Tick();
        Equal(requested, f.Scheduler.PendingRun!.Id, "enabling auto does not replace an accepted manual request");
        Time.realtimeSinceStartup = 30;
        f.Tick();
        Equal(requested, f.Scheduler.ActiveRun!.Run.Id, "enabling does not synthesize a competing past deadline");
        Equal(1, MaintenancePipeline.Created.Count, "retained manual request starts exactly once");
    }

    private static void DisableDuringActiveRun()
    {
        using var f = new Fixture();
        MaintenancePipeline.HoldFrames = 1;
        f.Net.Seconds = 24 * EnvMan.instance!.m_dayLengthSec;
        Time.realtimeSinceStartup = 1;
        f.Tick(); // Admit automatic work.
        f.Tick(); // Backend is active and waiting.
        Equal(1, MaintenancePipeline.Created.Count, "fixture has one active automatic run");
        int reloads = f.Plugin.Config.ReloadCount;
        f.Plugin.Config.Set("General", "Enabled", "false");
        f.SetField("reloadRequested", 1);
        f.Net.Seconds = 48 * EnvMan.instance.m_dayLengthSec;
        Time.realtimeSinceStartup = 2;
        f.Tick();
        Equal(RunStatus.Completed, f.Scheduler!.Attempts.Single().Status, "the active run finishes its existing policy");
        Equal(reloads + 1, f.Plugin.Config.ReloadCount, "deferred toggle applied in the completion update");
        Equal(1, MaintenancePipeline.Created.Count, "the next due automatic interval is never admitted with stale cfg");
        True(f.Scheduler.PendingRun == null, "disabled automatic interval is not left pending");
    }

    private static void ReconfigureFailureRemainsFaulted()
    {
        using var f = new Fixture();
        var store = new JsonWorldStateStore(Path.Combine(Paths.ConfigPath, "FreshWorld", "state"));
        string statePath = store.GetStatePath(f.Net.Uid.ToString());
        True(Path.GetFullPath(statePath).StartsWith(Path.GetFullPath(Paths.ConfigPath) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "state failure fixture must remain in its isolated test directory");
        byte[] original = File.ReadAllBytes(statePath);
        File.Delete(statePath);
        Directory.CreateDirectory(statePath); // Block the scheduler's atomic replacement, without a pending manual request.
        try
        {
            f.Plugin.Config.Set("General", "Enabled", "false");
            f.SetField("reloadRequested", 1);
            f.Tick();
            True(f.Scheduler == null, "failed durable reconfiguration closes its scheduler");
        }
        finally
        {
            Directory.Delete(statePath); // Only the empty directory created above.
            File.WriteAllBytes(statePath, original);
        }
        f.Plugin.Config.Set("General", "Enabled", "true");
        f.SetField("reloadRequested", 1);
        f.Tick();
        f.Run();
        Equal(0, MaintenancePipeline.Created.Count, "IO recovery or a valid cfg reload cannot clear a session persistence fault");
        HasReply(f.Request, "faulted");
        True(f.Scheduler == null, "the faulted host cannot reopen its scheduler");
    }

    private static void PreviousProcessRequest()
    {
        string requested = "";
        using var f = new Fixture(beforeAwake: (directory, net, config) =>
        {
            var settings = new FreshWorld.Configuration.FreshWorldConfig(config).Capture();
            var now = new ScheduleClock(DateTimeOffset.UtcNow, 0);
            using var previous = FreshWorldScheduler.Open(settings.Schedule,
                new JsonWorldStateStore(Path.Combine(directory, "FreshWorld", "state")), net.Uid.ToString(), now);
            requested = previous.RequestManualRun(now).Id;
        });
        Equal(0, MaintenancePipeline.Created.Count, "persisted manual request lacks authoritative live request context");
        True(f.Scheduler!.PendingRun == null, "orphan manual request removed on attach");
        Equal(RunStatus.Interrupted, f.Scheduler.Attempts.Single(a => a.Run.Id == requested).Status,
            "orphan manual cancellation persisted");
    }

    private static void CompletionNotification()
    {
        using var f = new Fixture();
        f.Run();
        f.Tick();
        Equal(RunStatus.Completed, f.Scheduler!.Attempts.Single().Status, "successful run persisted");
        Equal(1, MaintenancePipeline.Mutations, "single backend execution");
        HasReply(f.Request, "completed");
        True(MaintenanceGate.IsAvailable, "completion releases backend lease");
    }

    private static void FailureNotification()
    {
        using var f = new Fixture();
        MaintenancePipeline.Failure = new InvalidOperationException("fixture reset failure");
        f.Run();
        f.Tick();
        Equal(RunStatus.Failed, f.Scheduler!.Attempts.Single().Status, "failed run persisted");
        HasReply(f.Request, "fail");
        for (int i = 0; i < 3; ++i) f.Tick();
        Equal(1, MaintenancePipeline.Created.Count, "failed manual work must not retry");
        True(MaintenanceGate.IsAvailable, "failure releases backend lease");
    }

    private static void ReadOnlyStatus()
    {
        using var f = new Fixture();
        var observer = new CommandRequestContext(f.Net, f.Net.Uid, "another remote admin");
        f.Plugin.HandleCommand(FreshWorldCommandAction.Status, observer);
        Equal(0, MaintenancePipeline.Created.Count, "idle status does not dispatch");
        True(f.Scheduler!.PendingRun == null && f.Scheduler.Attempts.Count == 0, "idle status does not reserve work");
        HasReply(observer, "Automatic=True");
        HasReply(observer, "No maintenance has run");

        f.Run();
        f.Tick();
        observer.Replies.Clear();
        f.Plugin.HandleCommand(FreshWorldCommandAction.Status, observer);
        HasReply(observer, "Last run: Completed");
        HasReply(observer, f.Scheduler.Attempts.Single().Run.Id);
        Equal(1, MaintenancePipeline.Created.Count, "status does not start a second run after completion");
        Equal(1, MaintenancePipeline.Mutations, "status does not mutate the world");
        True(f.Scheduler.PendingRun == null && f.Scheduler.Attempts.Count == 1, "status preserves execution history");
    }

    private static void ShutdownCleansUp()
    {
        var f = new Fixture();
        MaintenancePipeline.HoldFrames = 5;
        f.Run(); f.Tick();
        var scheduler = f.Scheduler!;
        True(FreshWorldCommands.Handler != null, "command registered during plugin initialization");
        f.Dispose();
        True(FreshWorldCommands.Handler == null, "command unregistered at plugin destruction");
        True(MaintenanceGate.IsAvailable, "shutdown releases active lease");
        Equal(1, MaintenancePipeline.Disposals, "active backend coroutine cleaned up once");
        Equal(RunStatus.Failed, scheduler.Attempts.Single().Status, "interrupted active run persisted as failure");
    }

    private static void ShutdownSurvivesWatcherFailure()
    {
        var f = new Fixture();
        MaintenancePipeline.HoldFrames = 5;
        f.Run(); f.Tick();
        var watcherField = typeof(FreshWorldPlugin).GetField("watcher", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((FileSystemWatcher)watcherField.GetValue(f.Plugin)!).Dispose();
        watcherField.SetValue(f.Plugin, new ThrowingWatcher());
        f.Dispose();
        True(FreshWorldCommands.Handler == null, "watcher failure left the command registered");
        True(MaintenanceGate.IsAvailable, "watcher failure left the backend lease held");
        Equal(1, MaintenancePipeline.Disposals, "watcher failure skipped active coroutine cleanup");
        True(FreshWorldPlugin.Instance == null, "watcher failure retained the destroyed plugin instance");
        True(f.Plugin.Logger.Messages.Any(message => message.Contains("Could not stop configuration watching.")),
            "watcher disposal failure was not logged");
    }

    private static void HasReply(CommandRequestContext request, string text) =>
        True(request.Replies.Any(message => message.Contains(text, StringComparison.OrdinalIgnoreCase)),
            "missing reply containing '" + text + "': " + string.Join(" | ", request.Replies));
    private static void True(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }
    private static void Equal<T>(T expected, T actual, string reason)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}"); }

    private sealed class ThrowingWatcher : FileSystemWatcher
    {
        protected override void Dispose(bool disposing) => throw new InvalidOperationException("injected watcher disposal failure");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory;
        private bool disposed;
        public FreshWorldPlugin Plugin { get; }
        public ZNet Net { get; }
        public CommandRequestContext Request { get; }
        public FreshWorldScheduler? Scheduler => GetField<FreshWorldScheduler>("scheduler");
        public Fixture(Action<ConfigFile>? configure = null, Action<string, ZNet, ConfigFile>? beforeAwake = null,
            bool skipStartupDelay = true)
        {
            directory = Path.Combine(Path.GetTempPath(), "FreshWorld.Plugin.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Paths.ConfigPath = directory;
            Time.realtimeSinceStartup = 0; Time.timeScale = 1;
            Net = new ZNet(); ZNet.instance = Net; ZNet.World = new object(); ZNet.m_loadError = false;
            ZoneSystem.instance = new ZoneSystem(); ZDOMan.instance = new ZDOMan();
            ZNetScene.instance = new ZNetScene(); WorldGenerator.instance = new WorldGenerator();
            DungeonDB.instance = new DungeonDB(); Game.instance = new Game(); EnvMan.instance = new EnvMan();
            MaintenancePipeline.Reset();
            Plugin = new FreshWorldPlugin();
            configure?.Invoke(Plugin.Config);
            beforeAwake?.Invoke(directory, Net, Plugin.Config);
            Invoke("Awake");
            True(GetField<bool>("supported"), "fixture plugin initialization: " + string.Join(" | ", Plugin.Logger.Messages));
            Tick();
            // Most cases begin after the internal startup grace period; startup-specific tests keep it intact.
            if (skipStartupDelay) SetField("attachedAt", -30f);
            Request = new CommandRequestContext(Net, Net.Uid);
        }
        public void Tick() => Invoke("Update");
        public void Run(CommandRequestContext? context = null) => Plugin.HandleCommand(FreshWorldCommandAction.Run, context ?? Request);
        public void SetField(string name, object value) => typeof(FreshWorldPlugin).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Plugin, value);
        internal T? GetField<T>(string name) => (T?)typeof(FreshWorldPlugin).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Plugin);
        private void Invoke(string name)
        {
            try { typeof(FreshWorldPlugin).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Plugin, null); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Invoke("OnDestroy");
            // The only recursive deletion is our resolved GUID-specific temporary fixture directory.
            string permittedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FreshWorld.Plugin.Tests")) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(directory);
            if (!target.StartsWith(permittedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
            Directory.Delete(target, true);
        }
    }
}
