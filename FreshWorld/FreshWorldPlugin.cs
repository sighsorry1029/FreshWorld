using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using FreshWorld.Backend;
using FreshWorld.Configuration;
using FreshWorld.Commands;
using FreshWorld.Core;
using FreshWorld.Engine;
using FreshWorld.Runtime;
using HarmonyLib;
using UnityEngine;

namespace FreshWorld
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public sealed class FreshWorldPlugin : BaseUnityPlugin
    {
        public const string Author = "sighsorry";
        public const string ModName = "FreshWorld";
        public const string ModVersion = "1.0.8";
        public const string ModGUID = Author + "." + ModName;
        public const string PluginGuid = ModGUID;
        public const string PluginName = ModName;
        public const string PluginVersion = ModVersion;
        private const float SchedulePollIntervalSeconds = 1f;
        internal static FreshWorldPlugin? Instance;

        private FreshWorldConfig configuration = null!;
        private ConfigSynchronization? configSync;
        private RuntimeSettings? settings;
        private Harmony? harmony;
        private FileSystemWatcher? watcher;
        private int reloadRequested;
        private int configValuesChanged;
        private bool supported;
        private bool sessionFaulted;
        private ZNet? host;
        private long? worldUid;
        private FreshWorldScheduler? scheduler;
        private GuardedCoroutine? runner;
        private IDisposable? backendLease;
        private ScheduleClock lastClock;
        private float attachedAt;
        private float nextPoll;
        private float nextCheckpoint;
        private float nextReleaseSweep;
        private ManualRequest? pendingManual;
        private string? activeRunId;
        private string progress = "Idle.";

        private sealed class ManualRequest
        {
            internal readonly ScheduledRun Run;
            internal readonly CommandRequestContext Context;
            internal readonly RunOptions Options;
            internal string? LastWait;
            internal ManualRequest(ScheduledRun run, CommandRequestContext context, RunOptions options)
            { Run = run; Context = context; Options = options; }
        }

        private void Awake()
        {
            Instance = this;
            try
            {
                configuration = new FreshWorldConfig(Config);
                harmony = new Harmony(PluginGuid);
                // The embedded ServerSync library installs its own patches. Do not patch it twice.
                // Unannotated helpers can have ordinary Prepare/Cleanup methods that Harmony treats as hooks.
                foreach (var type in typeof(FreshWorldPlugin).Assembly.GetTypes().Where(type =>
                    (type.Namespace == "FreshWorld" || type.Namespace?.StartsWith("FreshWorld.", StringComparison.Ordinal) == true) &&
                    type.IsDefined(typeof(HarmonyPatch), inherit: false)))
                    harmony.PatchAll(type);
                configSync = new ConfigSynchronization(Config, configuration, harmony,
                    () => Interlocked.Exchange(ref configValuesChanged, 1), message => Logger.LogWarning(message));
                ReloadSettings(false);
                FreshWorldCommands.Register(HandleCommand, message => Logger.LogWarning(message));
                watcher = new FileSystemWatcher(Path.GetDirectoryName(Config.ConfigFilePath)!, Path.GetFileName(Config.ConfigFilePath));
                watcher.Changed += OnConfigChanged;
                watcher.Created += OnConfigChanged;
                watcher.Renamed += OnConfigChanged;
                watcher.EnableRaisingEvents = true;
                supported = true;
                Logger.LogInfo("FreshWorld ready. Maintenance runs only on the world host; configure " + Config.ConfigFilePath);
            }
            catch (Exception error)
            {
                CleanupPlugin();
                Logger.LogError("FreshWorld initialization failed; maintenance is disabled. " + error);
            }
        }

        private void OnConfigChanged(object sender, FileSystemEventArgs args) => Interlocked.Exchange(ref reloadRequested, 1);

        private void ReloadSettings(bool reloadFile)
        {
            try
            {
                if (reloadFile)
                {
                    // BepInEx fires value changes during Reload. Do not save a partially read cfg
                    // over the remaining file entries; normal Configuration Manager saves stay enabled.
                    configSync!.ReloadFile();
                    Interlocked.Exchange(ref configValuesChanged, 0);
                }
                var next = configuration.Capture();
                // Scheduling changes must not discard an authorized manual request or its captured reset policy.
                try { scheduler?.Reconfigure(next.Schedule, ReadClock()); }
                catch (Exception error)
                {
                    // A failed durable schedule update is a session fault, not a recoverable cfg typo.
                    sessionFaulted = true;
                    CloseScheduler("The updated schedule could not be persisted.");
                    Logger.LogError("Could not update the recorded schedule; maintenance is disabled for this world session. " + error);
                    return;
                }
                settings = next;
                if (reloadFile) configSync?.Publish();
                Logger.LogInfo($"Configuration applied: automatic={next.AutomaticEnabled}, schedule={next.Schedule.Mode}, timezone={next.Schedule.TimeZone.Id}, safeZones={next.Options.ZoneSafeZones}/{next.Options.VegetationSafeZones}/{next.Options.LocationSafeZones} (zones/resources/locations).");
                nextPoll = 0;
            }
            catch (Exception error)
            {
                settings = null;
                CloseScheduler("The host configuration is invalid.");
                Logger.LogError("Invalid FreshWorld configuration; new maintenance is disabled until the cfg is corrected. " + error.Message);
            }
        }

        private void ApplyPendingSettings()
        {
            if (runner != null) return;
            var reload = Interlocked.Exchange(ref reloadRequested, 0) != 0;
            var changed = Interlocked.Exchange(ref configValuesChanged, 0) != 0;
            if (reload || changed) ReloadSettings(reload);
        }

        private static bool IsHost(ZNet? net) => net != null && net.IsServer() && !net.HaveStopped && ZNet.World != null;

        private static bool WorldReady() => ZoneSystem.instance != null && ZoneSystem.instance.LocationsGenerated &&
            ZDOMan.instance != null && ZNetScene.instance != null && WorldGenerator.instance != null &&
            DungeonDB.instance != null && Game.instance != null && EnvMan.instance != null &&
            !ZNet.m_loadError;

        private ScheduleClock ReadClock()
        {
            if (!IsHost(host) || EnvMan.instance == null) return new ScheduleClock(DateTimeOffset.UtcNow, lastClock.GameDay);
            var length = EnvMan.instance.m_dayLengthSec;
            if (length <= 0) throw new InvalidOperationException("The game day length must be positive.");
            return new ScheduleClock(DateTimeOffset.UtcNow, Math.Max(0, host!.GetTimeSeconds() / length));
        }

        private void Update()
        {
            if (!supported) return;
            try
            {
                var current = ZNet.instance;
                long? currentUid = IsHost(current) ? current.GetWorldUID() : null;
                if (worldUid != null && (!ReferenceEquals(host, current) || worldUid != currentUid || !IsHost(current)))
                    StopSession();

                // File watchers only flag changes. All Unity and configuration work stays on this thread.
                ApplyPendingSettings();

                if (!IsHost(current) || !WorldReady())
                {
                    // A disappearing subsystem must cancel the guard, not leave its lease held indefinitely.
                    runner?.Dispose();
                    runner = null;
                    CancelPendingManual("The hosting world is no longer ready.");
                    return;
                }
                var now = Time.realtimeSinceStartup;
                if (now >= nextReleaseSweep)
                {
                    var released = GameWorld.ProcessDeferredReleases();
                    if (released > 0) Logger.LogInfo($"Cleared {released} deferred maintenance zone release(s).");
                    nextReleaseSweep = now + 1f;
                }
                AttachHost(current);

                if (runner != null)
                {
                    var active = runner;
                    if (!active.MoveNext() && ReferenceEquals(runner, active)) runner = null;
                }
                // Apply edits deferred by the active run before admitting another automatic run.
                ApplyPendingSettings();
                if (settings == null || sessionFaulted) return;
                lastClock = ReadClock();
                EnsureScheduler();

                // A manually admitted request is checked each frame; schedule polling never adds delay.
                if (pendingManual != null)
                {
                    if (!IsCurrentRequest(pendingManual.Context))
                        CancelPendingManual("The requester disconnected, changed session, or lost administrator authority.");
                    else if (runner == null)
                        TryDispatch(pendingManual.Run);
                }

                if (now < nextPoll) return;
                nextPoll = now + SchedulePollIntervalSeconds;
                var due = scheduler!.GetDue(lastClock);
                if (now >= nextCheckpoint)
                {
                    scheduler.Checkpoint(lastClock);
                    nextCheckpoint = now + 60;
                }
                if (runner != null) return;
                if (due != null) TryDispatch(due);
            }
            catch (Exception error)
            {
                sessionFaulted = true;
                runner?.Dispose();
                runner = null;
                Logger.LogError("FreshWorld stopped maintenance for this world session after an error. " + error);
                CloseScheduler("The host stopped maintenance after an error.");
            }
        }

        private void AttachHost(ZNet current)
        {
            var uid = current.GetWorldUID();
            if (worldUid != null && (!ReferenceEquals(host, current) || worldUid != uid)) StopSession();
            if (host != null) return;
            host = current;
            worldUid = uid;
            attachedAt = Time.realtimeSinceStartup;
            nextCheckpoint = attachedAt + 60;
            sessionFaulted = false;
        }

        private void EnsureScheduler()
        {
            if (scheduler != null) return;
            var stateWorldId = worldUid!.Value.ToString(CultureInfo.InvariantCulture);
            scheduler = FreshWorldScheduler.Open(settings!.Schedule,
                new JsonWorldStateStore(Path.Combine(Paths.ConfigPath, "FreshWorld", "state")), stateWorldId, lastClock);
            // A persisted command has no authenticated requester after restart/reload. Unlike scheduled
            // jobs, it must be explicitly submitted again in the new session (also covers legacy RunNow).
            var orphan = scheduler.PendingRun;
            if (orphan?.IsManual == true)
            {
                scheduler.CancelPendingManualRun(orphan.Id, lastClock, "The manual request belonged to an earlier host session.");
                Logger.LogWarning("Cancelled an earlier session's unstarted manual request: " + orphan.Id);
            }
            Logger.LogInfo("Scheduler attached to world " + host!.GetWorldName() + " (" + stateWorldId + ").");
        }

        private bool IsCurrentRequest(CommandRequestContext context) => context.IsAuthorizedNow &&
            ReferenceEquals(context.Network, ZNet.instance) && IsHost(context.Network) &&
            context.Network.GetWorldUID() == context.WorldUid;

        internal void HandleCommand(FreshWorldCommandAction action, CommandRequestContext context)
        {
            try
            {
                if (!supported || !IsCurrentRequest(context))
                { context.Reply("Request rejected: this host session or your authority is no longer valid."); return; }
                if (!Enum.IsDefined(typeof(FreshWorldCommandAction), action))
                { context.Reply("Invalid command action."); return; }
                if (!WorldReady()) { context.Reply("Request rejected: the world is not ready."); return; }
                AttachHost(context.Network);
                if (action == FreshWorldCommandAction.Status)
                {
                    context.Reply(StatusMessage());
                    return;
                }
                if (runner != null || activeRunId != null)
                { context.Reply("Maintenance is already running: " + activeRunId + ". " + progress); return; }
                if (pendingManual != null)
                {
                    if (!IsCurrentRequest(pendingManual.Context)) CancelPendingManual("The earlier request lost its authority or session.");
                    else { context.Reply("A manual request is already pending: " + pendingManual.Run.Id); return; }
                }
                // Read only the host's file. A client supplies an action, never reset options or IDs.
                Interlocked.Exchange(ref reloadRequested, 0);
                ReloadSettings(true);
                if (settings == null) { context.Reply("Request rejected: the host cfg is invalid; see the host log."); return; }
                if (sessionFaulted) { context.Reply("Request rejected: maintenance is faulted for this world session; see the host log."); return; }
                if (!IsCurrentRequest(context)) { context.Reply("Request rejected: authority changed before admission."); return; }
                lastClock = ReadClock();
                EnsureScheduler();
                var due = scheduler!.GetDue(lastClock);
                if (due != null) { context.Reply("A scheduled maintenance is already pending: " + due.Id); return; }
                var run = scheduler.RequestManualRun(lastClock);
                pendingManual = new ManualRequest(run, context, settings.Options);
                context.Reply("Accepted " + run.Id + ". Host cfg: zones=" + settings.Options.ZonesEnabled +
                    ", vegetation=" + settings.Options.VegetationEnabled + ", locations=" + settings.Options.LocationsEnabled +
                    ", safeZones=" + settings.Options.ZoneSafeZones + "/" + settings.Options.VegetationSafeZones + "/" +
                    settings.Options.LocationSafeZones + " (zones/resources/locations).");
                Logger.LogInfo("Manual request accepted from " + context.Actor + ": " + run.Id);
                TryDispatch(run);
                nextPoll = 0;
            }
            catch (Exception error)
            {
                sessionFaulted = true;
                context.Reply("Request failed; see the host log. New maintenance is disabled for this session.");
                Logger.LogError("FreshWorld command admission failed: " + error);
                CloseScheduler("Command admission failed.");
            }
        }

        private string StatusMessage()
        {
            if (sessionFaulted) return "Maintenance is faulted for this world session. " + progress;
            if (runner != null) return "Running " + activeRunId + ": " + progress;
            if (pendingManual != null) return "Pending " + pendingManual.Run.Id + ": " + (pendingManual.LastWait ?? "waiting to start");
            if (settings == null) return "The host cfg is invalid; new maintenance is disabled.";
            var pending = scheduler?.PendingRun;
            if (pending != null) return "Scheduled maintenance pending: " + pending.Id;
            var last = scheduler?.Attempts.LastOrDefault();
            var policy = "Automatic=" + settings.AutomaticEnabled + "; manual commands available; Mode=" + settings.Schedule.Mode + "; zones=" + settings.Options.ZonesEnabled +
                ", resources=" + settings.Options.VegetationEnabled + ", locations=" + settings.Options.LocationsEnabled +
                "; safeZones=" + settings.Options.ZoneSafeZones + "/" + settings.Options.VegetationSafeZones + "/" +
                settings.Options.LocationSafeZones + " (zones/resources/locations). ";
            return policy + (last == null ? "Idle. No maintenance has run in this world's recorded history." :
                "Idle. Last run: " + last.Status + " (" + last.Run.Id + ").");
        }

        private void TryDispatch(ScheduledRun run)
        {
            if (runner != null || settings == null || sessionFaulted) return;
            if (!run.IsManual && !settings.AutomaticEnabled) return;
            var options = settings.Options;
            var request = pendingManual;
            if (run.IsManual)
            {
                if (request == null || request.Run.Id != run.Id || !IsCurrentRequest(request.Context))
                {
                    if (request != null && request.Run.Id == run.Id) CancelPendingManual("The requester is no longer authorized in this session.");
                    else scheduler!.CancelPendingManualRun(run.Id, lastClock, "No authenticated request exists in this session.");
                    return;
                }
                options = request.Options;
            }
            string? wait = null;
            if (!WorldReady() || !IsHost(host)) wait = "world readiness";
            else if (Time.realtimeSinceStartup - attachedAt < settings.StartupDelaySeconds) wait = "startup delay";
            else if (Time.timeScale <= 0) wait = "the host to unpause";
            else if (host!.IsSaving()) wait = "the current world save";
            else if (!MaintenanceGate.IsAvailable) wait = "another maintenance operation";
            if (wait != null)
            {
                if (request != null && request.LastWait != wait)
                { request.LastWait = wait; request.Context.Reply("Waiting for " + wait + "."); }
                return;
            }
            StartRun(run, options);
        }

        private void StartRun(ScheduledRun run, RunOptions options)
        {
            var activeScheduler = scheduler!;
            var activeHost = host;
            var activeWorldUid = worldUid;
            var requester = pendingManual?.Run.Id == run.Id ? pendingManual.Context : null;
            backendLease = MaintenanceGate.Acquire();
            try
            {
                activeScheduler.BeginRun(run, lastClock);
                pendingManual = null;
                activeRunId = run.Id;
                progress = "Starting.";
                Exception? failure = null;
                var pipeline = new MaintenancePipeline(options, run.IncludeVegetation,
                    message => { progress = message; Logger.LogInfo(message); requester?.Reply(message); },
                    message => { Logger.LogWarning(message); requester?.Reply(message); });
                bool CanContinue()
                {
                    if (requester != null && !IsCurrentRequest(requester))
                    {
                        failure ??= new InvalidOperationException("The requester's connection, session, or authority is no longer valid.");
                        return false;
                    }
                    return ReferenceEquals(ZNet.instance, activeHost) && IsHost(activeHost) &&
                        activeHost!.GetWorldUID() == activeWorldUid && WorldReady();
                }
                runner = new GuardedCoroutine(pipeline.Run(),
                    CanContinue,
                    error => { failure = failure ?? error; Logger.LogError("Maintenance error: " + error); },
                    success =>
                    {
                        try
                        {
                            if (success)
                            {
                                activeScheduler.CompleteRun(run.Id, ReadClock());
                                Logger.LogInfo("Maintenance completed: " + run.Id);
                                progress = "Completed.";
                                requester?.Reply("Completed " + run.Id + ". World changes use the game's normal saves; no extra save was requested.");
                            }
                            else
                            {
                                activeScheduler.FailRun(run.Id, ReadClock(), failure?.Message ?? "World session ended or maintenance was cancelled.");
                                Logger.LogWarning("Maintenance stopped; completed world changes remain. This run will not retry automatically: " + run.Id);
                                progress = "Stopped: " + (failure?.Message ?? "world session ended");
                                requester?.Reply("Failed or interrupted " + run.Id + ": " + (failure?.Message ?? "world session ended") +
                                    ". Applied changes remain; this run will not retry automatically.");
                            }
                        }
                        catch (Exception error)
                        {
                            sessionFaulted = true;
                            Logger.LogError("Could not persist the maintenance result; further runs disabled for this session. " + error);
                            requester?.Reply("Maintenance ended, but its result could not be recorded. Further runs are disabled; see the host log.");
                        }
                        finally
                        {
                            backendLease?.Dispose();
                            backendLease = null;
                            activeRunId = null;
                        }
                    });
                Logger.LogInfo("Maintenance starting: " + run.Id + "; vegetation=" + run.IncludeVegetation);
                requester?.Reply("Starting " + run.Id + ".");
            }
            catch
            {
                backendLease?.Dispose();
                backendLease = null;
                activeRunId = null;
                requester?.Reply("Maintenance could not start; see the host log.");
                throw;
            }
        }

        private void CancelPendingManual(string reason)
        {
            var request = pendingManual;
            if (request == null) return;
            pendingManual = null;
            try { scheduler?.CancelPendingManualRun(request.Run.Id, ReadClock(), reason); }
            catch (Exception error)
            {
                sessionFaulted = true;
                Logger.LogError("Could not persist cancellation of the manual request: " + error);
            }
            request.Context.Reply("Cancelled " + request.Run.Id + " before execution: " + reason);
            Logger.LogInfo("Manual request cancelled before execution: " + request.Run.Id + "; " + reason);
        }

        private void CloseScheduler(string reason = "The scheduler was closed.")
        {
            CancelPendingManual(reason);
            var closing = scheduler;
            scheduler = null;
            if (closing == null) return;
            try { closing.Dispose(); }
            catch (Exception error)
            {
                sessionFaulted = true;
                Logger.LogError("Failed to close world state. " + error);
            }
        }

        internal void StopSession()
        {
            runner?.Dispose();
            runner = null;
            backendLease?.Dispose();
            backendLease = null;
            CloseScheduler("The hosting world session ended.");
            host = null;
            worldUid = null;
            nextPoll = 0;
            nextReleaseSweep = 0;
            sessionFaulted = false;
            activeRunId = null;
            progress = "Idle.";
        }

        private void OnDestroy()
        {
            CleanupPlugin();
        }

        private void CleanupPlugin()
        {
            supported = false;
            try
            {
                TryCleanup(DisposeWatcher, "Could not stop configuration watching.");
                TryCleanup(() => { configSync?.Dispose(); configSync = null; }, "Could not stop configuration synchronization.");
                TryCleanup(StopSession, "Could not stop the active world session.");
                TryCleanup(FreshWorldCommands.Unregister, "Could not unregister the console command.");
                TryCleanup(() => harmony?.UnpatchSelf(), "Could not remove Harmony patches.");
            }
            finally { Instance = null; }
        }

        private void DisposeWatcher()
        {
            var closing = watcher;
            watcher = null;
            if (closing == null) return;
            closing.Changed -= OnConfigChanged;
            closing.Created -= OnConfigChanged;
            closing.Renamed -= OnConfigChanged;
            closing.Dispose();
        }

        private void TryCleanup(Action cleanup, string message)
        {
            try { cleanup(); }
            catch (Exception error) { Logger.LogError(message + " " + error); }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown), new[] { typeof(bool) })]
    internal static class ShutdownPatch
    {
        [HarmonyPrefix] private static void Prefix() => FreshWorldPlugin.Instance?.StopSession();
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.ShutdownWithoutSave), new[] { typeof(bool) })]
    internal static class ShutdownWithoutSavePatch
    {
        [HarmonyPrefix] private static void Prefix() => FreshWorldPlugin.Instance?.StopSession();
    }
}
