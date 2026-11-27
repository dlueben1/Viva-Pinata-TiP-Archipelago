namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosConversation : Patch
{
    public override uint Address => 0x82492058;
    public override void Execute(PatchContext c)
    {
        uint strategy = c.GetGuestAddress(3);
        if (!SeedosNative.IsSeedos(c, SeedosNative.Actor(c, strategy))) { c.CallOriginal(); return; }
        bool empty = c.ReadUInt32(PatchContext.Add(strategy, 0x149C)) == SeedosNative.Null;
        c.CallOriginal(); // Preserve chance, RNG and forced-gift bookkeeping even for Null.
        if (empty) c.SetGpr(3, 0);
    }
    public override void OnFailure(PatchContext c) => Environment.FailFast("Seedos conversation decision could not be safely completed.");
}
