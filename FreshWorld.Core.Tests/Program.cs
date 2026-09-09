using System;
using System.IO;
using System.Linq;
using FreshWorld.Core;

internal static class Program
{
    private static int count;
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "freshworld-core-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Day = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
    private static ScheduleClock At(double hours, double day = 10) => new ScheduleClock(Day.AddHours(hours), day);
    private static ScheduleSettings Daily(bool catchUp = false, string zone = "UTC", string times = "05:35,17:35", string vegetation = "05:35")
        => new ScheduleSettings(ScheduleMode.DailyTimes, times, zone, 30, vegetation, catchUp);
    private static ScheduleSettings GameDays(bool catchUp = false) => new ScheduleSettings(ScheduleMode.GameDays, "05:35", "UTC", 3, "*", catchUp);
    private static ScheduleSettings Disabled(ScheduleMode mode = ScheduleMode.GameDays)
        => new ScheduleSettings(mode, "invalid time", "invalid zone", double.NaN, "invalid vegetation", true, false);
    private static JsonWorldStateStore Store() => new JsonWorldStateStore(Path.Combine(TestRoot, Guid.NewGuid().ToString("N")));
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static ScheduledRun Due(FreshWorldScheduler scheduler, ScheduleClock clock)
        => scheduler.GetDue(clock) ?? throw new Exception("Expected due run");
    private static void Execute(FreshWorldScheduler scheduler, ScheduledRun run, ScheduleClock clock)
    { scheduler.BeginRun(run, clock); scheduler.CompleteRun(run.Id, clock); }
    private static void Test(string name, Action action)
    { action(); count++; Console.WriteLine("PASS " + name); }

    private static int Main()
    {
        Directory.CreateDirectory(TestRoot);
        try
        {
            Test("daily deadline, morning vegetation, evening exclusion, no duplicate", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "1", At(4));
                Assert(s.GetDue(At(5.5)) == null, "before deadline");
                var morning = Due(s, new ScheduleClock(Day.AddHours(5).AddMinutes(35), 10));
                Assert(morning.Slot == "05:35" && morning.IncludeVegetation, "morning selection");
                Assert(morning.DueUtc == Day.AddHours(5).AddMinutes(35), "exact due timestamp");
                Execute(s, morning, At(6));
                Assert(s.GetDue(At(6)) == null, "no repeat after completion");
                var evening = Due(s, At(18));
                Assert(evening.Slot == "17:35" && !evening.IncludeVegetation, "evening selection");
            });
            Test("missed online deadlines coalesce to latest slot", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "2", At(0));
                var run = Due(s, At(72 + 18));
                Assert(run.DueUtc == Day.AddDays(3).AddHours(17).AddMinutes(35), "latest only");
                Execute(s, run, At(90));
                Assert(s.GetDue(At(90)) == null, "older deadlines not replayed");
            });
            Test("default restart skips downtime", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(), store, "3", At(0))) s.Checkpoint(At(4));
                using var next = FreshWorldScheduler.Open(Daily(), store, "3", At(18));
                Assert(next.GetDue(At(18)) == null, "downtime should be skipped");
                Assert(Due(next, At(30)).Slot == "05:35", "next day deadline");
            });
            Test("opt-in restart catches up once", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(true), store, "4", At(0))) s.Checkpoint(At(4));
                using var next = FreshWorldScheduler.Open(Daily(true), store, "4", At(90));
                var run = Due(next, At(90));
                Assert(run.DueUtc == Day.AddDays(3).AddHours(17).AddMinutes(35), "latest after downtime");
                Execute(next, run, At(90));
                Assert(next.GetDue(At(90)) == null, "catchup only once");
            });
            Test("pending survives backend wait and restart with catchup disabled", () =>
            {
                var store = Store();
                string id;
                using (var s = FreshWorldScheduler.Open(Daily(), store, "5", At(0)))
                {
                    id = Due(s, At(6)).Id;
                    Assert(Due(s, At(7)).Id == id, "unavailable backend must not discard pending");
                }
                using var next = FreshWorldScheduler.Open(Daily(), store, "5", At(18));
                Assert(Due(next, At(18)).Id == id, "pending is an observed run, not downtime catchup");
            });
            Test("pending coalesces while backend remains unavailable", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "6", At(0));
                var a = Due(s, At(6));
                var b = Due(s, At(18));
                Assert(a.Id != b.Id && b.Slot == "17:35", "replace stale pending by latest deadline");
                Throws<InvalidOperationException>(() => s.BeginRun(a, At(18)));
                Execute(s, b, At(18));
            });
            Test("game days wait first interval, pause and coalesce", () =>
            {
                using var s = FreshWorldScheduler.Open(GameDays(), Store(), "7", At(0, 100));
                Assert(s.GetDue(At(24, 100)) == null, "paused game clock");
                Assert(s.GetDue(At(25, 102.99)) == null, "first interval not reached");
                var first = Due(s, At(26, 103));
                Assert(first.GameDay == 103 && first.IncludeVegetation, "game deadline");
                Execute(s, first, At(26, 103));
                Assert(Due(s, At(28, 112)).GameDay == 112, "coalesce game days");
            });
            Test("game day restart keeps anchor and skips downtime by default", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(GameDays(), store, "8", At(0, 10))) s.Checkpoint(At(1, 11));
                using var next = FreshWorldScheduler.Open(GameDays(), store, "8", At(4, 17));
                Assert(next.GetDue(At(4, 17)) == null, "skip offline intervals");
                Assert(Due(next, At(5, 19)).GameDay == 19, "original anchor retained");
            });
            Test("game day restart catchup uses persisted cursor", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(GameDays(true), store, "9", At(0, 10))) s.Checkpoint(At(1, 11));
                using var next = FreshWorldScheduler.Open(GameDays(true), store, "9", At(4, 17));
                Assert(Due(next, At(4, 17)).GameDay == 16, "latest elapsed game deadline");
            });
            Test("disabled automatic scheduling ignores inactive fields while manual work remains available", () =>
            {
                Assert((int)ScheduleMode.DailyTimes == 0 && (int)ScheduleMode.GameDays == 1 && Enum.GetValues(typeof(ScheduleMode)).Length == 2,
                    "existing schedule enum values changed");
                var settings = Disabled();
                Throws<ArgumentOutOfRangeException>(() => Disabled((ScheduleMode)2));
                using var s = FreshWorldScheduler.Open(settings, Store(), "automatic-disabled", At(0));
                Assert(!settings.AutomaticEnabled && s.GetDue(At(10000, 10000)) == null && s.PendingRun == null, "disabled schedule created automatic work");
                var manual = s.RequestManualRun(At(10000, 10000));
                Assert(Due(s, At(10001, 10001)).Id == manual.Id, "manual request unavailable with automatic scheduling disabled");
                Execute(s, manual, At(10001, 10001));
                Assert(s.GetDue(At(20000, 20000)) == null && s.Attempts.Single().Status == RunStatus.Completed,
                    "manual completion created automatic work or lost history");
            });
            Test("disabled automatic scheduling preserves manual reservations across restart and inactive edits", () =>
            {
                var store = Store(); string id;
                using (var s = FreshWorldScheduler.Open(Disabled(), store, "manual-restart", At(0)))
                    id = s.RequestManualRun(At(1)).Id;
                var edited = Disabled(ScheduleMode.DailyTimes);
                using var restored = FreshWorldScheduler.Open(edited, store, "manual-restart", At(100, 100));
                var pending = Due(restored, At(100, 100));
                Assert(pending.Id == id && pending.Slot == "Manual", "inactive changes discarded manual reservation");
                Execute(restored, pending, At(100, 100));
                Assert(restored.GetDue(At(200, 200)) == null && restored.Attempts.Single().Run.Id == id,
                    "disabled schedule lost history or scheduled automatic work after restart");
            });
            Test("manual interruption is recorded without retry while automatic scheduling is disabled", () =>
            {
                var store = Store(); string id;
                var settings = Disabled();
                using (var s = FreshWorldScheduler.Open(settings, store, "manual-interrupted", At(0)))
                {
                    var manual = s.RequestManualRun(At(1)); id = manual.Id;
                    s.BeginRun(manual, At(1));
                }
                using var restored = FreshWorldScheduler.Open(settings, store, "manual-interrupted", At(100, 100));
                Assert(restored.GetDue(At(1000, 1000)) == null && restored.PendingRun == null, "interrupted manual run replayed");
                Assert(restored.Attempts.Single().Run.Id == id && restored.Attempts.Single().Status == RunStatus.Interrupted,
                    "interrupted manual attempt history missing");
            });
            Test("inactive daily fields do not move the game-day anchor or discard pending work", () =>
            {
                var store = Store(); string id;
                using (var s = FreshWorldScheduler.Open(GameDays(), store, "days-inactive", At(0, 10))) s.Checkpoint(At(1, 11));
                var edited = new ScheduleSettings(ScheduleMode.GameDays, "invalid", "not-a-zone", 3, "invalid");
                using (var restored = FreshWorldScheduler.Open(edited, store, "days-inactive", At(2, 12)))
                {
                    Assert(restored.GetDue(At(2, 12)) == null, "inactive change created a retroactive run");
                    var due = Due(restored, At(3, 13)); id = due.Id;
                    Assert(due.GameDay == 13, "inactive change reset original day-10 anchor");
                }
                using var pending = FreshWorldScheduler.Open(GameDays(), store, "days-inactive", At(4, 14));
                Assert(Due(pending, At(4, 14)).Id == id, "inactive change discarded or renamed pending work");
            });
            Test("inactive game interval does not alter daily validation or pending work", () =>
            {
                var store = Store(); string id;
                using (var s = FreshWorldScheduler.Open(Daily(), store, "daily-inactive", At(0))) id = Due(s, At(6)).Id;
                var edited = new ScheduleSettings(ScheduleMode.DailyTimes, "05:35,17:35", "UTC", double.NaN, "05:35");
                using var restored = FreshWorldScheduler.Open(edited, store, "daily-inactive", At(7));
                var pending = Due(restored, At(7));
                Assert(pending.Id == id, "inactive interval discarded or renamed pending daily run");
                Execute(restored, pending, At(7));
                Assert(restored.GetDue(At(7)) == null && Due(restored, At(18)).Slot == "17:35", "daily clock changed");
            });
            Test("active game interval change clears pending work and establishes a new anchor", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(GameDays(), store, "days-active", At(0, 10))) Due(s, At(1, 13));
                var changed = new ScheduleSettings(ScheduleMode.GameDays, "", "", 4, "", true);
                using var restored = FreshWorldScheduler.Open(changed, store, "days-active", At(2, 14));
                Assert(restored.PendingRun == null && restored.GetDue(At(2, 14)) == null, "old pending run survived active interval change");
                Assert(restored.GetDue(At(3, 17.99)) == null && Due(restored, At(4, 18)).GameDay == 18, "new interval did not start at edit baseline");
            });
            Test("disabling automatic scheduling clears scheduled pending while preserving attempt history", () =>
            {
                var store = Store(); string completedId;
                using (var s = FreshWorldScheduler.Open(Daily(true), store, "mode-change", At(0)))
                {
                    var first = Due(s, At(6)); completedId = first.Id; Execute(s, first, At(6));
                    Due(s, At(18));
                }
                using (var manual = FreshWorldScheduler.Open(Disabled(), store, "mode-change", At(19)))
                {
                    Assert(manual.PendingRun == null && manual.GetDue(At(90, 90)) == null, "disabled schedule exposed previous automatic run");
                    Assert(manual.Attempts.Single().Run.Id == completedId, "mode change erased attempt history");
                    var run = manual.RequestManualRun(At(90, 90)); Execute(manual, run, At(90, 90));
                }
                using var daily = FreshWorldScheduler.Open(Daily(true), store, "mode-change", At(91, 91));
                Assert(daily.GetDue(At(91, 91)) == null && daily.Attempts.Count == 2, "automatic re-enable replayed downtime or lost manual history");
                Assert(Due(daily, At(102, 92)).Slot == "05:35", "automatic schedule failed to resume at next future deadline");
            });
            Test("game-day re-enable starts a fresh interval without colliding with earlier run history", () =>
            {
                var store = Store(); string previousId;
                using (var s = FreshWorldScheduler.Open(GameDays(), store, "days-reenable", At(0, 10)))
                {
                    var first = Due(s, At(1, 13)); previousId = first.Id; Execute(s, first, At(1, 13));
                }
                using (var manual = FreshWorldScheduler.Open(Disabled(), store, "days-reenable", At(2, 14)))
                    Assert(manual.GetDue(At(3, 20)) == null, "disabled automatic schedule ran");
                using var resumed = FreshWorldScheduler.Open(GameDays(), store, "days-reenable", At(4, 20));
                Assert(resumed.GetDue(At(5, 22.99)) == null, "re-enable did not wait a full new interval");
                var next = Due(resumed, At(6, 23));
                Assert(next.GameDay == 23 && next.Id != previousId, "first new interval collided with earlier history");
                Execute(resumed, next, At(6, 23));
                Assert(resumed.Attempts.Count == 2 && resumed.GetDue(At(6, 23)) == null, "new interval was duplicated or history erased");
            });
            Test("live schedule and automatic-toggle edits retain the accepted manual request and policy", () =>
            {
                using var s = FreshWorldScheduler.Open(GameDays(), Store(), "manual-live-toggle", At(0, 10));
                var manual = s.RequestManualRun(At(1, 11), false);
                s.Reconfigure(Daily(), At(6, 12));
                Assert(s.PendingRun?.Id == manual.Id, "schedule mode edit discarded accepted manual request");
                s.Reconfigure(Disabled(ScheduleMode.DailyTimes), At(7, 13));
                Assert(Due(s, At(7, 13)).Id == manual.Id && !s.PendingRun!.IncludeVegetation, "automatic disable replaced manual policy");
                s.Reconfigure(new ScheduleSettings(ScheduleMode.GameDays, "", "", 24), At(8, 14));
                Assert(Due(s, At(8, 14)).Id == manual.Id && !s.PendingRun!.IncludeVegetation, "automatic re-enable replaced manual policy");
                Execute(s, manual, At(8, 14));
                Assert(s.Attempts.Single().Run.Id == manual.Id && s.Attempts.Single().Status == RunStatus.Completed, "manual request history changed");
                Assert(s.GetDue(At(9, 37.99)) == null && Due(s, At(10, 38)).GameDay == 38,
                    "automatic re-enable failed to wait a fresh 24-day interval");
            });
            Test("live automatic disable cancels only scheduled pending and remains disabled across restart", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(GameDays(true), store, "scheduled-live-toggle", At(0, 10)))
                {
                    var scheduled = Due(s, At(1, 13));
                    s.Reconfigure(Disabled(), At(2, 14));
                    Assert(s.PendingRun == null, "automatic disable retained a scheduled reservation");
                    Throws<InvalidOperationException>(() => s.BeginRun(scheduled, At(2, 14)));
                    Assert(s.GetDue(At(3, 100)) == null, "disabled schedule continued producing work");
                    s.Checkpoint(At(3, 100));
                }
                using var restored = FreshWorldScheduler.Open(Disabled(ScheduleMode.DailyTimes), store, "scheduled-live-toggle", At(100, 500));
                Assert(restored.GetDue(At(100, 500)) == null && restored.PendingRun == null, "restart replayed disabled deadlines");
                restored.Reconfigure(new ScheduleSettings(ScheduleMode.GameDays, "", "", 24, "*", true), At(101, 500));
                Assert(restored.GetDue(At(102, 523.99)) == null && Due(restored, At(103, 524)).GameDay == 524,
                    "re-enable caught up downtime or reused an old anchor");
            });
            Test("daily re-enable waits for a future slot without catching up disabled deadlines", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(true), Store(), "daily-live-toggle", At(0));
                var scheduled = Due(s, At(6));
                s.Reconfigure(Disabled(ScheduleMode.DailyTimes), At(7));
                Assert(s.PendingRun == null && s.GetDue(At(90)) == null, "disabled daily schedule retained or created work");
                Throws<InvalidOperationException>(() => s.BeginRun(scheduled, At(90)));
                s.Reconfigure(Daily(true), At(91));
                Assert(s.GetDue(At(91)) == null, "daily re-enable caught up a disabled deadline");
                Assert(Due(s, At(102)).Slot == "05:35", "daily re-enable missed the next future slot");
            });
            Test("unchanged active scheduling preserves pending work and the original game-day anchor", () =>
            {
                using var s = FreshWorldScheduler.Open(GameDays(), Store(), "same-live-policy", At(0, 10));
                var pending = Due(s, At(1, 13));
                s.Reconfigure(new ScheduleSettings(ScheduleMode.GameDays, "invalid", "invalid", 3, "invalid"), At(2, 14));
                Assert(s.PendingRun?.Id == pending.Id, "inactive-field edit replaced a scheduled reservation");
                Execute(s, pending, At(2, 14));
                Assert(Due(s, At(3, 16)).GameDay == 16, "inactive-field edit reset the original anchor");

                using var daily = FreshWorldScheduler.Open(Daily(), Store(), "same-live-daily-policy", At(0));
                daily.Reconfigure(new ScheduleSettings(ScheduleMode.DailyTimes, "05:35,17:35", "UTC", double.NaN, "05:35"), At(6));
                Assert(Due(daily, At(6)).Slot == "05:35", "inactive interval edit swallowed a just-due daily slot");
            });
            Test("disabled-mode edits do not rewrite the baseline or accepted manual reservation", () =>
            {
                var store = Store();
                using var s = FreshWorldScheduler.Open(Disabled(), store, "same-disabled-policy", At(0, 10));
                var manual = s.RequestManualRun(At(1, 11));
                var before = File.ReadAllText(store.GetStatePath("same-disabled-policy"));
                s.Reconfigure(new ScheduleSettings(ScheduleMode.DailyTimes, "", "", -1, "", true, false), At(100, 100));
                Assert(s.PendingRun?.Id == manual.Id, "unused disabled-mode edits discarded the manual reservation");
                Assert(File.ReadAllText(store.GetStatePath("same-disabled-policy")) == before, "unused disabled-mode edits rewrote schedule state");
            });
            Test("live reconfiguration preserves an active manual attempt and records completion", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "active-manual-toggle", At(0));
                var manual = s.RequestManualRun(At(1)); s.BeginRun(manual, At(1));
                s.Reconfigure(Disabled(), At(2));
                Assert(s.ActiveRun?.Run.Id == manual.Id && s.GetDue(At(100, 100)) == null, "automatic disable altered active manual work");
                s.CompleteRun(manual.Id, At(100, 100));
                Assert(s.Attempts.Single().Status == RunStatus.Completed && s.GetDue(At(101, 101)) == null,
                    "active manual completion was lost or followed by automatic work");
            });
            Test("opening with a changed automatic policy retains manual state for caller session validation", () =>
            {
                var store = Store(); string id;
                using (var s = FreshWorldScheduler.Open(Daily(), store, "manual-policy-restart", At(0))) id = s.RequestManualRun(At(1)).Id;
                using var restored = FreshWorldScheduler.Open(Disabled(), store, "manual-policy-restart", At(100, 100));
                Assert(restored.PendingRun?.Id == id, "automatic policy change deleted manual state before caller validation");
                Assert(restored.CancelPendingManualRun(id, At(100, 100), "previous requester session ended"), "caller could not reject previous-session manual request");
                Assert(restored.PendingRun == null && restored.Attempts.Single().Status == RunStatus.Interrupted,
                    "previous-session manual cancellation was not recorded");
            });
            Test("UTC and game clock reversal cannot repeat", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "10", At(0));
                var run = Due(s, At(6)); Execute(s, run, At(6));
                Assert(s.GetDue(At(4)) == null && s.GetDue(At(6)) == null, "UTC reversal");
                using var days = FreshWorldScheduler.Open(GameDays(), Store(), "10", At(0, 10));
                var d = Due(days, At(1, 13)); Execute(days, d, At(1, 13));
                Assert(days.GetDue(At(2, 11)) == null && days.GetDue(At(3, 13)) == null, "game reversal");
                Assert(Due(days, At(4, 16)).GameDay == 16, "resume at new deadline");
            });
            Test("separate world UIDs do not share attempts", () =>
            {
                var store = Store();
                using var a = FreshWorldScheduler.Open(Daily(), store, "uid-A", At(0));
                using var b = FreshWorldScheduler.Open(Daily(), store, "uid-B", At(0));
                var run = Due(a, At(6)); Execute(a, run, At(6));
                Assert(Due(b, At(6)).Id == run.Id && b.Attempts.Count == 0, "world isolation");
                Assert(store.GetStatePath("uid-A") != store.GetStatePath("uid-B"), "separate files");
            });
            Test("reservation is durable and interrupted runs are not retried", () =>
            {
                var store = Store(); string id;
                using (var s = FreshWorldScheduler.Open(Daily(true), store, "12", At(0)))
                {
                    var run = Due(s, At(6)); id = run.Id; s.BeginRun(run, At(6));
                    Assert(File.ReadAllText(store.GetStatePath("12")).Contains(id), "reservation on disk before mutations");
                }
                using var next = FreshWorldScheduler.Open(Daily(true), store, "12", At(7));
                Assert(next.Attempts.Single().Status == RunStatus.Interrupted, "interruption recorded");
                Assert(next.GetDue(At(7)) == null && next.ActiveRun == null, "interrupted run not repeated");
            });
            Test("failed run is recorded and never automatically retried", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(true), store, "13", At(0)))
                {
                    var run = Due(s, At(6)); s.BeginRun(run, At(6)); s.FailRun(run.Id, At(6), "backend rejected");
                    Throws<InvalidOperationException>(() => s.BeginRun(run, At(6)));
                }
                using var next = FreshWorldScheduler.Open(Daily(true), store, "13", At(7));
                Assert(next.GetDue(At(7)) == null, "failed run not repeated");
                Assert(next.Attempts.Single().Status == RunStatus.Failed && next.Attempts.Single().Message == "backend rejected", "failure reason retained");
            });
            Test("deadline crossed during active run remains queued", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "14", At(0));
                var first = Due(s, At(6)); s.BeginRun(first, At(6));
                Assert(s.GetDue(At(18)) == null, "no concurrent run");
                s.CompleteRun(first.Id, At(18));
                Assert(Due(s, At(18)).Slot == "17:35", "later deadline is pending");
            });
            Test("completion without polling does not swallow intervening deadline", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "15", At(0));
                var first = Due(s, At(6)); s.BeginRun(first, At(6)); s.CompleteRun(first.Id, At(18));
                Assert(Due(s, At(18)).Slot == "17:35", "completion must not advance schedule cursor");
            });
            Test("DST fallback executes a repeated local slot once", () =>
            {
                var config = Daily(false, "Eastern Standard Time", "01:30", "*");
                var midnight = new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero);
                using var s = FreshWorldScheduler.Open(config, Store(), "16", new ScheduleClock(midnight, 10));
                var first = Due(s, new ScheduleClock(midnight.AddMinutes(90), 10));
                Assert(first.DueUtc == midnight.AddMinutes(90), "first occurrence chosen");
                Execute(s, first, new ScheduleClock(midnight.AddMinutes(90), 10));
                Assert(s.GetDue(new ScheduleClock(midnight.AddMinutes(150), 10)) == null, "second occurrence skipped");
            });
            Test("DST spring gap skips nonexistent local slot", () =>
            {
                var config = Daily(false, "Eastern Standard Time", "02:30", "*");
                var midnight = new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero);
                using var s = FreshWorldScheduler.Open(config, Store(), "17", new ScheduleClock(midnight, 10));
                Assert(s.GetDue(new ScheduleClock(midnight.AddHours(3), 10)) == null, "nonexistent slot skipped");
                Assert(Due(s, new ScheduleClock(midnight.AddHours(26), 10)).Slot == "02:30", "next day resumes");
            });
            Test("timezone conversion uses host-selected civil time", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(false, "Korea Standard Time"), Store(), "18", At(0));
                var run = Due(s, At(9));
                Assert(run.Slot == "17:35" && run.DueUtc == Day.AddHours(8).AddMinutes(35), "KST slot maps to UTC");
            });
            Test("invalid config is rejected before any run", () =>
            {
                Throws<ArgumentException>(() => Daily(times: "25:00", vegetation: "*"));
                Throws<ArgumentException>(() => Daily(times: "5:35", vegetation: "*"));
                Throws<ArgumentException>(() => Daily(times: "05:35,05:35", vegetation: "*"));
                Throws<ArgumentException>(() => Daily(vegetation: "05:36"));
                Throws<TimeZoneNotFoundException>(() => Daily(zone: "not-a-zone"));
                Throws<ArgumentOutOfRangeException>(() => new ScheduleSettings(ScheduleMode.GameDays, "05:35", "UTC", -1));
                Throws<ArgumentOutOfRangeException>(() => new ScheduleSettings(ScheduleMode.GameDays, "05:35", "UTC", double.NaN));
                Throws<ArgumentOutOfRangeException>(() => new ScheduleClock(Day, -1));
            });
            Test("corrupt state fails closed without overwriting evidence", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(), store, "20", At(0))) { }
                var path = store.GetStatePath("20"); File.WriteAllText(path, "{broken");
                Throws<StateCorruptionException>(() => FreshWorldScheduler.Open(Daily(), store, "20", At(6)));
                Assert(File.ReadAllText(path) == "{broken", "corrupt file preserved");
            });
            Test("wrong world state fails closed", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(), store, "21-A", At(0))) { }
                File.Copy(store.GetStatePath("21-A"), store.GetStatePath("21-B"));
                Throws<StateCorruptionException>(() => FreshWorldScheduler.Open(Daily(), store, "21-B", At(6)));
            });
            Test("exclusive world lease prevents two reset controllers", () =>
            {
                var store = Store();
                using var s = FreshWorldScheduler.Open(Daily(), store, "22", At(0));
                Throws<IOException>(() => FreshWorldScheduler.Open(Daily(), store, "22", At(0)));
            });
            Test("manual request persists and cannot replace scheduled pending", () =>
            {
                var store = Store();
                using var s = FreshWorldScheduler.Open(Daily(), store, "23", At(0));
                var manual = s.RequestManualRun(At(1), false);
                Assert(manual.Slot == "Manual" && !manual.IncludeVegetation, "manual selection");
                Assert(File.ReadAllText(store.GetStatePath("23")).Contains(manual.Id), "manual pending durable");
                Throws<InvalidOperationException>(() => s.RequestManualRun(At(1)));
                s.BeginRun(manual, At(1));
                Throws<InvalidOperationException>(() => s.RequestManualRun(At(1)));
                s.CompleteRun(manual.Id, At(1));
                Due(s, At(6));
                Throws<InvalidOperationException>(() => s.RequestManualRun(At(6)));
            });
            Test("pending manual run keeps vegetation policy across automatic deadlines and restart", () =>
            {
                var store = Store(); string id;
                using (var s = FreshWorldScheduler.Open(Daily(true), store, "manual-wait", At(16)))
                {
                    var manual = s.RequestManualRun(At(17), true); id = manual.Id;
                    var pending = Due(s, At(18));
                    Assert(pending.Id == id && pending.Slot == "Manual" && pending.IncludeVegetation,
                        "evening automatic slot replaced the explicit manual policy");
                }
                using var next = FreshWorldScheduler.Open(Daily(true), store, "manual-wait", At(19));
                var restored = Due(next, At(19));
                Assert(restored.Id == id && restored.Slot == "Manual" && restored.IncludeVegetation, "manual pending did not survive restart");
                Execute(next, restored, At(19));
                Assert(next.GetDue(At(19)) == null, "automatic deadline covered by manual run should not replay");
                Assert(Due(next, At(30)).Slot == "05:35", "next automatic deadline still runs");
            });
            Test("manual cancellation is durable and cannot execute or replay the cancelled request", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(), store, "cancel-manual", At(0)))
                {
                    var run = s.RequestManualRun(At(1));
                    Assert(s.PendingRun?.Id == run.Id, "pending snapshot missing");
                    Assert(!s.CancelPendingManualRun("wrong-id", At(1), "wrong"), "wrong request cancelled");
                    Assert(s.CancelPendingManualRun(run.Id, At(1), "authority revoked"), "cancellation failed");
                    Assert(s.PendingRun == null && s.ActiveRun == null, "cancelled request remains executable");
                    Assert(s.Attempts.Single().Status == RunStatus.Interrupted, "cancellation not recorded");
                    Throws<InvalidOperationException>(() => s.BeginRun(run, At(1)));
                    Assert(!s.CancelPendingManualRun(run.Id, At(1), "repeat"), "cancellation repeated");
                }
                using var restored = FreshWorldScheduler.Open(Daily(), store, "cancel-manual", At(2));
                Assert(restored.PendingRun == null && restored.Attempts.Single().Message == "authority revoked", "cancellation was not durable");
                Assert(Due(restored, At(6)).Slot == "05:35", "future scheduled work was removed");
            });
            Test("manual cancellation cannot remove a scheduled or already active run", () =>
            {
                using var s = FreshWorldScheduler.Open(Daily(), Store(), "cancel-scheduled", At(0));
                var scheduled = Due(s, At(6));
                Assert(!s.CancelPendingManualRun(scheduled.Id, At(6), "not manual"), "scheduled request cancelled");
                Execute(s, scheduled, At(6));
                var manual = s.RequestManualRun(At(7));
                s.BeginRun(manual, At(7));
                Assert(!s.CancelPendingManualRun(manual.Id, At(7), "already active"), "active request cancelled through pending API");
                Assert(s.ActiveRun?.Run.Id == manual.Id, "active request changed");
                s.CompleteRun(manual.Id, At(7));
            });
            Test("schedule edit establishes a new baseline instead of surprise reset", () =>
            {
                var store = Store();
                using (var s = FreshWorldScheduler.Open(Daily(true), store, "24", At(0))) Due(s, At(6));
                var changed = Daily(true, times: "06:00", vegetation: "*");
                using var next = FreshWorldScheduler.Open(changed, store, "24", At(8));
                Assert(next.PendingRun == null && next.GetDue(At(8)) == null, "old pending survived or edited schedule ran retroactively");
                Assert(Due(next, At(30)).Slot == "06:00", "new schedule applies going forward");
            });
            Test("state write failure faults session before dispatch", () =>
            {
                var store = Store();
                using var s = FreshWorldScheduler.Open(Daily(), store, "25", At(0));
                var run = Due(s, At(6));
                var path = store.GetStatePath("25");
                File.Delete(path); Directory.CreateDirectory(path);
                Throws<IOException>(() => s.BeginRun(run, At(6)));
                Throws<InvalidOperationException>(() => s.GetDue(At(7)));
                Assert(s.ActiveRun == null, "unpersisted reservation not exposed as active");
            });
            Test("reconfiguration persistence failure faults the scheduler before publishing a new policy", () =>
            {
                var store = Store();
                using var s = FreshWorldScheduler.Open(GameDays(), store, "reconfigure-write-failure", At(0, 10));
                var manual = s.RequestManualRun(At(1, 11));
                var path = store.GetStatePath("reconfigure-write-failure");
                File.Delete(path); Directory.CreateDirectory(path);
                Throws<IOException>(() => s.Reconfigure(Disabled(), At(2, 12)));
                Throws<InvalidOperationException>(() => s.GetDue(At(3, 13)));
                Throws<InvalidOperationException>(() => s.BeginRun(manual, At(3, 13)));
                Throws<InvalidOperationException>(() => s.Reconfigure(GameDays(), At(3, 13)));
                Assert(s.Attempts.Count == 0 && s.ActiveRun == null, "failed reconfiguration published or executed uncommitted work");
            });
            count += ConfigRegressions.Run();
            count += ConfigPresentationRegressions.Run();
            Console.WriteLine($"All {count} core/config regression checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            var full = Path.GetFullPath(TestRoot);
            if (Path.GetDirectoryName(full) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                && Path.GetFileName(full).StartsWith("freshworld-core-tests-", StringComparison.Ordinal))
                Directory.Delete(full, true);
        }
    }
}
