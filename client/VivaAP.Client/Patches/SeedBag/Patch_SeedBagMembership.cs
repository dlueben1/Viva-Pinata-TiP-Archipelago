namespace VivaAP.Client.Patches.SeedBag;

internal sealed class Patch_SeedBagMembership : Patch
{
    public override uint Address => 0x825B2508;

    public override void Execute(PatchContext context)
    {
        if (Patch_SeedBagAdmission.TryConsumeMembership(context.GetGpr(4), out bool received))
        {
            context.SetGpr(3, received ? 1u : 0u);
            context.SkipOriginal();
            return;
        }

        context.CallOriginal();
    }
}
