namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosWeedEligibility : Patch
{
    public override uint Address => 0x82493718;
    public override void Execute(PatchContext c)
    {
        uint actor = SeedosNative.Actor(c, c.GetGuestAddress(3));
        if (!SeedosNative.IsSeedos(c, actor)) { c.CallOriginal(); return; }
        bool empty = !SeedosSelectionPolicy.HasCandidates(c, SeedosNative.Scene(c, actor), true);
        c.CallOriginal();
        if (empty) c.SetFpr(1, 0);
    }
    public override void OnFailure(PatchContext c)
    {
        if (!c.OriginalStarted && !c.OriginalSkipped) c.SkipOriginal();
        c.SetFpr(1, 0); // Verified floating-point CanDo failure contract.
    }
}
