namespace VivaAP.Client.Patches.Seedos;

// Both request admission and the actual synchronous strategy transition are guarded. The latter
// closes the selection-to-initialization gap without editing debt or substituting an Init body.
internal sealed class Patch_SeedosWeedStrategy(bool transition) : Patch
{
    public override uint Address => transition ? 0x821BEEC0u : 0x824C0288u;
    public override void Execute(PatchContext c)
    {
        uint requested = unchecked((uint)c.GetGpr(4));
        if (requested is 0x4B6 or 0x4B7)
        {
            uint actor = SeedosNative.Actor(c, c.GetGuestAddress(3));
            if (SeedosNative.IsSeedos(c, actor) &&
                !SeedosSelectionPolicy.HasCandidates(c, SeedosNative.Scene(c, actor), true))
                c.SetGpr(4, 0x4B8);
        }
        c.CallOriginal();
    }
    public override void OnFailure(PatchContext c) => Environment.FailFast("Seedos strategy transition could not be safely completed.");
}
