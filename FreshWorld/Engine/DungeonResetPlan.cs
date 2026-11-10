using System;
using System.Collections.Generic;

namespace FreshWorld.Engine;

/// <summary>One synchronous reset's extra interior deletions and explicitly retained foreign interiors.</summary>
internal sealed class DungeonResetPlan
{
    public readonly Func<ZDO, bool> RetainObject;
    public readonly IReadOnlyCollection<ZDO> AdditionalObjects;
    public readonly Action Validate;
    public readonly Action Complete;

    public DungeonResetPlan(Func<ZDO, bool> retainObject, IReadOnlyCollection<ZDO> additionalObjects, Action validate, Action? complete = null)
    { RetainObject = retainObject; AdditionalObjects = additionalObjects; Validate = validate; Complete = complete ?? (() => { }); }
}

/// <summary>Raised only during preflight, before any deletion or terrain mutation.</summary>
internal sealed class DungeonResetBlockedException : Exception
{
    public DungeonResetBlockedException(string reason) : base(reason) { }
}
