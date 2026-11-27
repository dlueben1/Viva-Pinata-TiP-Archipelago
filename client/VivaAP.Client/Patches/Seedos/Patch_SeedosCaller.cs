namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosCaller(uint address, SeedosPath path) : Patch
{
    public override uint Address => address;
    public override void Execute(PatchContext c)
    {
        uint argument = c.GetGuestAddress(3);
        uint actor = path == SeedosPath.Hit ? argument : SeedosNative.Actor(c, argument);
        if (!SeedosNative.IsSeedos(c, actor))
        {
            using var barrier = new SeedosScope(SeedosPath.None);
            c.CallOriginal(); return;
        }
        uint scene = path == SeedosPath.TalkInitialization ? SeedosNative.TalkScene(c, actor) : SeedosNative.Scene(c, actor);
        using var scope = new SeedosScope(path, scene);
        c.CallOriginal();
        if (path == SeedosPath.WeedInitialization &&
            (c.ReadUInt32(PatchContext.Add(argument, 0x149C)) == SeedosNative.Null ||
             !SeedosSelectionPolicy.Allows(c.ReadUInt32(PatchContext.Add(argument, 0x149C))) ||
             !SeedosSelectionPolicy.HasCandidates(c, scene, true)))
            SeedosNative.FinishWeeds(c, argument);
    }
    public override void OnFailure(PatchContext c) => Environment.FailFast("Seedos caller scope or cleanup failed; native state is uncertain.");
}

// The shared radial scatter is the only researched non-Seedos selector caller. It can reenter
// through a native callback; a nested barrier prevents inheriting an outer Seedos policy scope.
internal sealed class Patch_SeedosEnvironmentalBarrier : Patch
{
    public override uint Address => 0x825708F8;
    public override void Execute(PatchContext c)
    {
        using var barrier = new SeedosScope(SeedosPath.Environment);
        c.CallOriginal();
    }
}
