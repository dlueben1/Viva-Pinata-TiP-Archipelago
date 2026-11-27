namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosWeedUpdate : Patch
{
    public override uint Address => 0x82493CB8;
    public override void Execute(PatchContext c)
    {
        uint strategy = c.GetGuestAddress(3), actor = SeedosNative.Actor(c, strategy);
        if (!SeedosNative.IsSeedos(c, actor)) { c.CallOriginal(); return; }
        uint scene = SeedosNative.Scene(c, actor);
        using var scope = new SeedosScope(SeedosPath.WeedDelivery, scene);
        uint selected = c.ReadUInt32(PatchContext.Add(strategy, 0x149C));
        if (selected == SeedosNative.Null || !SeedosSelectionPolicy.Allows(selected) ||
            !SeedosSelectionPolicy.HasCandidates(c, scene, true))
        {
            // Native 0x2B exit is checked before the asset-ready branch on the ensuing tick.
            // Init has initialized its load unit; normal Release retains resource ownership.
            // Neither the local attempt count nor either controller debt word is edited.
            SeedosNative.FinishWeeds(c, strategy);
        }
        c.CallOriginal();
    }
    public override void OnFailure(PatchContext c) => Environment.FailFast("Seedos weed cleanup failed; cannot safely enter the asset wait.");
}
