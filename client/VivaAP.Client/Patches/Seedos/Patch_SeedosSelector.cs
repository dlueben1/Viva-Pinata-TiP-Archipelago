namespace VivaAP.Client.Patches.Seedos;

internal sealed class Patch_SeedosSelector : Patch
{
    public override uint Address => 0x824E9E90;
    public override void Execute(PatchContext c)
    {
        var scope = SeedosScope.Current;
        uint weed = unchecked((uint)c.GetGpr(5));
        bool ordinary = scope.Path is SeedosPath.TalkInitialization or SeedosPath.Hit;
        bool weeds = scope.Path is SeedosPath.GardenThrow or SeedosPath.WeedInitialization;
        if ((!ordinary && !weeds) || c.GetGpr(3) != scope.Scene || weed != (weeds ? 1u : 0u))
        { c.CallOriginal(); return; }
        uint tag = SeedosSelectionPolicy.Select(c, scope.Scene, unchecked((int)c.GetGpr(4)), weed);
        c.SetGpr(3, tag);
        c.SkipOriginal();
    }
    public override void OnFailure(PatchContext c)
    {
        if (c.OriginalStarted) return;
        c.SetGpr(3, SeedosNative.Null);
        if (!c.OriginalSkipped) c.SkipOriginal();
    }
}
