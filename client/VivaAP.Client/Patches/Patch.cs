namespace VivaAP.Client.Patches;

internal abstract class Patch
{
    public abstract uint Address { get; }
    public virtual string Name => GetType().Name;
    public abstract void Execute(PatchContext context);

    // There is no universal rollback for guest side effects. Individual patches may supply
    // a VERIFIED denial/cleanup contract. Never retry an original that has already started.
    public virtual void OnFailure(PatchContext context)
    {
        if (context.OriginalStarted) return;
        if (context.HasSideEffects || context.OriginalSkipped)
            Environment.FailFast($"{Name} failed after side effects without a recovery contract.");
        context.CallOriginal();
    }
}

// Only qualified integer-register contracts are exposed to gameplay code. No float arguments,
// stack arguments, arbitrary continuation PCs, or native host pointers.
internal readonly record struct GuestFunction(uint Address, int ArgumentCount);
