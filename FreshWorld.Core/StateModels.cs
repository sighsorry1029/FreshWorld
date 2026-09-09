using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace FreshWorld.Core
{
    [DataContract]
    public sealed class ScheduledRun
    {
        [DataMember(Order = 1)] public string Id { get; private set; } = "";
        [DataMember(Order = 2)] private long dueUtcTicks;
        [DataMember(Order = 3)] public double GameDay { get; private set; }
        [DataMember(Order = 4)] public string Slot { get; private set; } = "";
        [DataMember(Order = 5)] public bool IncludeVegetation { get; private set; }
        public DateTimeOffset DueUtc => new DateTimeOffset(dueUtcTicks, TimeSpan.Zero);
        internal ScheduledRun(string id, DateTimeOffset dueUtc, double gameDay, string slot, bool vegetation)
        { Id = id; dueUtcTicks = dueUtc.UtcDateTime.Ticks; GameDay = gameDay; Slot = slot; IncludeVegetation = vegetation; }
        internal bool Valid => !string.IsNullOrWhiteSpace(Id) && dueUtcTicks > 0 && dueUtcTicks <= DateTime.MaxValue.Ticks
            && GameDay >= 0 && !double.IsInfinity(GameDay) && !double.IsNaN(GameDay) && Slot != null;
    }

    public enum RunStatus { Running, Completed, Failed, Interrupted }

    [DataContract]
    public sealed class RunRecord
    {
        [DataMember(Order = 1)] public ScheduledRun Run { get; private set; } = null!;
        [DataMember(Order = 2)] public RunStatus Status { get; internal set; }
        [DataMember(Order = 3)] private long startedUtcTicks;
        [DataMember(Order = 4)] private long finishedUtcTicks;
        [DataMember(Order = 5)] public string Message { get; internal set; } = "";
        public DateTimeOffset StartedUtc => new DateTimeOffset(startedUtcTicks, TimeSpan.Zero);
        public DateTimeOffset? FinishedUtc => finishedUtcTicks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(finishedUtcTicks, TimeSpan.Zero);
        internal RunRecord(ScheduledRun run, DateTimeOffset start) { Run = run; startedUtcTicks = start.UtcDateTime.Ticks; Status = RunStatus.Running; }
        internal void Finish(RunStatus status, DateTimeOffset time, string message)
        { Status = status; finishedUtcTicks = time.UtcDateTime.Ticks; Message = message ?? ""; }
        internal RunRecord Copy() => (RunRecord)MemberwiseClone();
        internal bool Valid => Run != null && Run.Valid && Enum.IsDefined(typeof(RunStatus), Status)
            && startedUtcTicks > 0 && startedUtcTicks <= DateTime.MaxValue.Ticks
            && finishedUtcTicks >= 0 && finishedUtcTicks <= DateTime.MaxValue.Ticks
            && (Status == RunStatus.Running ? finishedUtcTicks == 0 : finishedUtcTicks > 0) && Message != null;
    }

    [DataContract]
    internal sealed class WorldState
    {
        [DataMember(Order = 1)] public int SchemaVersion = 1;
        [DataMember(Order = 2)] public string WorldId = "";
        [DataMember(Order = 3)] public string Fingerprint = "";
        [DataMember(Order = 4)] public long CursorUtcTicks;
        [DataMember(Order = 5)] public double CursorGameDay;
        [DataMember(Order = 6)] public double GameDayAnchor;
        [DataMember(Order = 7)] public ScheduledRun? Pending;
        [DataMember(Order = 8)] public List<RunRecord> Attempts = new List<RunRecord>();
        public WorldState Copy()
        {
            var copy = (WorldState)MemberwiseClone();
            copy.Attempts = Attempts.Select(a => a.Copy()).ToList();
            return copy;
        }
        public void Validate(string expectedWorldId)
        {
            if (SchemaVersion != 1 || WorldId != expectedWorldId || string.IsNullOrWhiteSpace(Fingerprint)
                || CursorUtcTicks <= 0 || CursorUtcTicks > DateTime.MaxValue.Ticks || !FiniteNonnegative(CursorGameDay)
                || !FiniteNonnegative(GameDayAnchor) || GameDayAnchor > CursorGameDay || Attempts == null
                || Attempts.Any(a => a == null || !a.Valid) || Attempts.Select(a => a.Run.Id).Distinct().Count() != Attempts.Count
                || Attempts.Count(a => a.Status == RunStatus.Running) > 1 || (Pending != null && (!Pending.Valid || Attempts.Any(a => a.Run.Id == Pending.Id))))
                throw new StateCorruptionException("FreshWorld state is inconsistent. Automatic reset is disabled until the state file is recovered.");
        }
        private static bool FiniteNonnegative(double x) => x >= 0 && !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
