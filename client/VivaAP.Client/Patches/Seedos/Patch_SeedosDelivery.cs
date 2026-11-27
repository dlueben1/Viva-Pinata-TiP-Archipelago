using VivaAP.Client.Utils;

namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosDelivery(bool gardenThrow) : Patch
{
    private long lastWarning;
    public override uint Address => gardenThrow ? 0x82567F50u : 0x82567D88u;
    public override void Execute(PatchContext c)
    {
        ulong actorValue = c.GetGpr(4);
        if (actorValue > uint.MaxValue) { c.CallOriginal(); return; }
        uint actor = (uint)actorValue;
        if (!SeedosNative.IsSeedos(c, actor)) { c.CallOriginal(); return; }
        uint seed = unchecked((uint)c.GetGpr(3));
        // Temporary alpha exception for forced tutorial Daisy gifts on the Seedos helper path.
        // Ordinary Daisy selection still requires AP ownership.
        if ((!gardenThrow && seed == 441) || SeedosSelectionPolicy.Allows(seed))
        {
            c.CallOriginal();
            return;
        }

        long now = Environment.TickCount64, previous = Volatile.Read(ref lastWarning);
        if (previous == 0 || now - previous >= 5000)
            if (Interlocked.CompareExchange(ref lastWarning, now, previous) == previous)
                try { LogUtility.Warn($"Rejected unauthorized Seedos seed {seed} at {(gardenThrow ? "garden throw" : "helper delivery")}, path={SeedosScope.Current.Path}."); }
                catch { /* Logging cannot undo authorization denial. */ }
        if (!gardenThrow) c.SetGpr(3, 0); // Verified native admission-failure result.
        c.SkipOriginal();
    }
    public override void OnFailure(PatchContext c) => Environment.FailFast("Seedos physical delivery authorization could not be established.");
}
