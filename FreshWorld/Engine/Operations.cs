using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FreshWorld.Engine;

internal sealed class OperationParameters
{
    public int SafeZones { get; set; }
    public float TerrainReset { get; set; }
    public bool ProtectEpicLoot { get; set; } = true;
}

/// <summary>Typed host-side operations; exceptions propagate to the owning guarded runner.</summary>
internal abstract class ExecutedOperation
{
    protected readonly Action<string> Log;
    protected ExecutedOperation(Action<string> log) => Log = log;
    public void Init()
    {
        Log(OnInit());
    }
    public IEnumerator Execute()
    {
        OnStart();
        yield return OnExecute();
        OnEnd();
    }
    protected abstract string OnInit();
    protected abstract IEnumerator OnExecute();
    protected virtual void OnStart() { }
    protected virtual void OnEnd() { }
}

internal abstract class ZoneOperation : ExecutedOperation
{
    protected readonly OperationParameters Args;
    protected Vector2s[] ZonesToUpgrade;
    protected ZoneOperation(Action<string> log, OperationParameters args, HashSet<Vector2s>? candidates = null) : base(log)
    {
        Args = args;
        ZonesToUpgrade = GameWorld.GeneratedSnapshot(candidates);
    }
    protected override string OnInit()
    {
        var protectedZones = BaseProtection.GetExcluded(Args.SafeZones);
        ZonesToUpgrade = ZonesToUpgrade.Where(zone => !protectedZones.Contains(zone)).ToArray();
        return GetType().Name + ": " + ZonesToUpgrade.Length + " zones selected.";
    }
    protected abstract bool ExecuteZone(Vector2s zone);
    protected override IEnumerator OnExecute()
    {
        // Production subclasses are dispatched through OperationTracker's bounded retry loop.
        throw new InvalidOperationException("A zone operation must be executed through its tracking wrapper.");
    }
}
