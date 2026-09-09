using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FreshWorld.Core
{
    /// <summary>Single-world scheduler. Call BeginRun successfully before making any world changes.</summary>
    public sealed class FreshWorldScheduler : IDisposable
    {
        private readonly object gate = new object();
        private ScheduleSettings settings;
        private readonly JsonWorldStateStore.Lease lease;
        private WorldState state;
        private bool disposed;
        private bool faulted;
        public string WorldId => state.WorldId;
        public IReadOnlyList<RunRecord> Attempts { get { lock (gate) return state.Attempts.Select(a => a.Copy()).ToArray(); } }
        public RunRecord? ActiveRun { get { lock (gate) return state.Attempts.FirstOrDefault(a => a.Status == RunStatus.Running)?.Copy(); } }
        public ScheduledRun? PendingRun { get { lock (gate) { EnsureUsable(); return state.Pending; } } }

        private FreshWorldScheduler(ScheduleSettings settings, JsonWorldStateStore.Lease lease, WorldState state)
        { this.settings = settings; this.lease = lease; this.state = state; }

        public static FreshWorldScheduler Open(ScheduleSettings settings, JsonWorldStateStore store, string worldId, ScheduleClock now)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (store == null) throw new ArgumentNullException(nameof(store));
            var lease = store.Acquire(worldId);
            try
            {
                var state = lease.Read(worldId);
                if (state == null)
                    state = NewState(settings, worldId, now);
                else
                {
                    foreach (var running in state.Attempts.Where(a => a.Status == RunStatus.Running))
                        running.Finish(RunStatus.Interrupted, now.UtcNow, "The previous host session ended before completion. This run will not be retried automatically.");
                    if (state.Fingerprint != settings.Fingerprint)
                        ResetAutomaticSchedule(state, settings, now);
                    else if (!settings.RunMissedOnWorldStart)
                        Advance(state, now); // A pending run observed during an earlier session remains pending.
                }
                if (!settings.AutomaticEnabled && state.Pending?.Slot != "Manual") state.Pending = null;
                lease.Write(state);
                return new FreshWorldScheduler(settings, lease, state);
            }
            catch { lease.Dispose(); throw; }
        }

        /// <summary>Change automatic scheduling without replacing an accepted manual request or its history.</summary>
        public void Reconfigure(ScheduleSettings nextSettings, ScheduleClock now)
        {
            if (nextSettings == null) throw new ArgumentNullException(nameof(nextSettings));
            lock (gate)
            {
                EnsureUsable();
                var next = state.Copy();
                if (next.Fingerprint != nextSettings.Fingerprint) ResetAutomaticSchedule(next, nextSettings, now);
                if (!nextSettings.AutomaticEnabled && next.Pending?.Slot != "Manual") next.Pending = null;
                if (next.Fingerprint != state.Fingerprint || next.Pending?.Id != state.Pending?.Id)
                    Persist(next);
                // Publishing settings after persistence keeps a failed update from exposing an uncommitted schedule.
                settings = nextSettings;
            }
        }

        public ScheduledRun? GetDue(ScheduleClock now)
        {
            lock (gate)
            {
                EnsureUsable();
                var next = state.Copy();
                var candidate = !settings.AutomaticEnabled ? null : settings.Mode == ScheduleMode.DailyTimes ? LatestDaily(next, now)
                    : LatestGameDay(next, now);
                if (!settings.AutomaticEnabled && next.Pending?.Slot != "Manual") next.Pending = null;
                Advance(next, now);
                // A user's manual request retains its policy while awaiting backend readiness.
                // Automatic deadlines observed meanwhile are covered by that pending maintenance.
                if (candidate != null && next.Pending?.Slot != "Manual" && !next.Attempts.Any(a => a.Run.Id == candidate.Id))
                    next.Pending = candidate;
                if (next.Pending?.Id != state.Pending?.Id) Persist(next);
                else state = next;
                return state.Attempts.Any(a => a.Status == RunStatus.Running) ? null : state.Pending;
            }
        }

        public void BeginRun(ScheduledRun run, ScheduleClock now)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            lock (gate)
            {
                EnsureUsable();
                if (state.Attempts.Any(a => a.Status == RunStatus.Running)) throw new InvalidOperationException("A FreshWorld run is already active.");
                if (state.Pending == null || state.Pending.Id != run.Id || state.Attempts.Any(a => a.Run.Id == run.Id))
                    throw new InvalidOperationException("The run is no longer pending or has already been attempted.");
                var next = state.Copy();
                next.Attempts.Add(new RunRecord(next.Pending!, now.UtcNow));
                next.Pending = null;
                while (next.Attempts.Count > 128) next.Attempts.RemoveAt(0);
                Persist(next); // Reservation must reach disk before the caller may dispatch any mutation.
            }
        }

        public ScheduledRun RequestManualRun(ScheduleClock now, bool includeVegetation = true)
        {
            lock (gate)
            {
                EnsureUsable();
                if (state.Attempts.Any(a => a.Status == RunStatus.Running))
                    throw new InvalidOperationException("A FreshWorld run is already active.");
                if (state.Pending != null)
                    throw new InvalidOperationException("A FreshWorld run is already pending; a manual request cannot replace it.");
                var next = state.Copy();
                var run = new ScheduledRun("manual:" + Guid.NewGuid().ToString("N"), now.UtcNow, now.GameDay, "Manual", includeVegetation);
                next.Pending = run;
                Persist(next);
                return run;
            }
        }

        public void CompleteRun(string runId, ScheduleClock now) => FinishRun(runId, now, RunStatus.Completed, "");
        public void FailRun(string runId, ScheduleClock now, string reason) => FinishRun(runId, now, RunStatus.Failed, reason);

        /// <summary>Cancel only the matching unstarted manual request; scheduled work cannot be removed here.</summary>
        public bool CancelPendingManualRun(string runId, ScheduleClock now, string reason)
        {
            lock (gate)
            {
                EnsureUsable();
                if (state.Pending == null || state.Pending.Id != runId || state.Pending.Slot != "Manual") return false;
                var next = state.Copy();
                var cancelled = new RunRecord(next.Pending!, now.UtcNow);
                cancelled.Finish(RunStatus.Interrupted, now.UtcNow, reason);
                next.Attempts.Add(cancelled);
                next.Pending = null;
                while (next.Attempts.Count > 128) next.Attempts.RemoveAt(0);
                Persist(next);
                return true;
            }
        }

        public void Checkpoint(ScheduleClock now)
        {
            lock (gate)
            {
                EnsureUsable();
                GetDue(now);
                Persist(state.Copy());
            }
        }

        private void FinishRun(string runId, ScheduleClock now, RunStatus status, string reason)
        {
            lock (gate)
            {
                EnsureUsable();
                var next = state.Copy();
                var record = next.Attempts.SingleOrDefault(a => a.Run.Id == runId && a.Status == RunStatus.Running);
                if (record == null) throw new InvalidOperationException("No matching active FreshWorld run exists.");
                record.Finish(status, now.UtcNow, reason);
                Persist(next);
            }
        }

        private ScheduledRun? LatestDaily(WorldState current, ScheduleClock now)
        {
            if (now.UtcNow.UtcDateTime.Ticks <= current.CursorUtcTicks) return null;
            var today = TimeZoneInfo.ConvertTime(now.UtcNow, settings.TimeZone).Date;
            // The latest daily slot is today or yesterday, except a skipped DST date; three days covers that as well.
            for (var ago = 0; ago < 3; ago++)
            {
                var date = today.AddDays(-ago);
                for (var i = settings.DailyTimes.Count - 1; i >= 0; i--)
                {
                    var slot = settings.DailyTimes[i];
                    var local = DateTime.SpecifyKind(date.Add(slot), DateTimeKind.Unspecified);
                    if (settings.TimeZone.IsInvalidTime(local)) continue;
                    var offset = settings.TimeZone.IsAmbiguousTime(local)
                        ? settings.TimeZone.GetAmbiguousTimeOffsets(local).Max() : settings.TimeZone.GetUtcOffset(local);
                    var due = new DateTimeOffset(local, offset).ToUniversalTime();
                    if (due > now.UtcNow) continue;
                    if (due.UtcDateTime.Ticks <= current.CursorUtcTicks) return null;
                    var slotText = ScheduleSettings.FormatTime(slot);
                    var id = current.Fingerprint + ":daily:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + slotText;
                    return new ScheduledRun(id, due, now.GameDay, slotText, settings.IncludeVegetation(slot));
                }
            }
            return null;
        }

        private ScheduledRun? LatestGameDay(WorldState current, ScheduleClock now)
        {
            if (now.GameDay <= current.CursorGameDay) return null;
            var index = Math.Floor((now.GameDay - current.GameDayAnchor) / settings.GameDayInterval);
            var previous = Math.Floor((current.CursorGameDay - current.GameDayAnchor) / settings.GameDayInterval);
            if (index < 1 || index <= previous) return null;
            var target = current.GameDayAnchor + index * settings.GameDayInterval;
            // A mode/interval change starts a new anchor; its first interval must not collide with older history.
            return new ScheduledRun(current.Fingerprint + ":days:" + current.GameDayAnchor.ToString("R", CultureInfo.InvariantCulture)
                + ":" + index.ToString("0", CultureInfo.InvariantCulture),
                now.UtcNow, target, "GameDays", true);
        }

        private void Persist(WorldState next)
        {
            try { lease.Write(next); state = next; }
            catch { faulted = true; throw; }
        }
        private static void Advance(WorldState target, ScheduleClock clock)
        {
            target.CursorUtcTicks = Math.Max(target.CursorUtcTicks, clock.UtcNow.UtcDateTime.Ticks);
            target.CursorGameDay = Math.Max(target.CursorGameDay, clock.GameDay);
        }
        private static void ResetAutomaticSchedule(WorldState target, ScheduleSettings settings, ScheduleClock now)
        {
            target.Fingerprint = settings.Fingerprint;
            if (target.Pending?.Slot != "Manual") target.Pending = null;
            target.GameDayAnchor = now.GameDay;
            target.CursorGameDay = now.GameDay;
            target.CursorUtcTicks = now.UtcNow.UtcDateTime.Ticks;
        }
        private static WorldState NewState(ScheduleSettings settings, string worldId, ScheduleClock now) => new WorldState
        {
            WorldId = worldId, Fingerprint = settings.Fingerprint, CursorUtcTicks = now.UtcNow.UtcDateTime.Ticks,
            CursorGameDay = now.GameDay, GameDayAnchor = now.GameDay
        };
        private void EnsureUsable()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FreshWorldScheduler));
            if (faulted) throw new InvalidOperationException("State persistence failed; this scheduler is disabled for the session.");
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                try { if (!faulted) lease.Write(state); }
                finally { disposed = true; lease.Dispose(); }
            }
        }
    }
}
