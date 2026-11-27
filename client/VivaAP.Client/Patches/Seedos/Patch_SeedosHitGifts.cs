namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosHitGifts : Patch
{
    public override uint Address => 0x823FEA00;
    public override void Execute(PatchContext c)
    {
        if (SeedosScope.Current.Path != SeedosPath.Hit || c.GetGpr(3) != SeedosScope.Current.Scene)
        { c.CallOriginal(); return; }
        uint scene = c.GetGuestAddress(3);
        bool empty = !SeedosSelectionPolicy.HasCandidates(c, scene, false);
        c.CallOriginal(); // Hit/anger/model/debt effects are mandatory.
        if (empty) c.SetGpr(3, 0);
    }
    public override void OnFailure(PatchContext c) => Environment.FailFast("Seedos hit bookkeeping could not be safely completed.");
}
