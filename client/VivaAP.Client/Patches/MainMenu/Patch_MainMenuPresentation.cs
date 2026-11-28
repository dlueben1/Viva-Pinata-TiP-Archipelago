namespace VivaAP.Client.Patches.MainMenu;

// Required beyond setup/selection: mode6 polls connectivity and calls this helper to
// restore the online signs' base textures at 823D1200/124C. Keep their native cross.
internal sealed class Patch_MainMenuPresentation : Patch
{
    public override uint Address => 0x823D0F60;

    public override void Execute(PatchContext context)
    {
        uint actor = context.GetGuestAddress(3);
        uint tag = context.ReadUInt32(PatchContext.Add(actor, MainMenuAvailability.ActorTag));
        if (MainMenuAvailability.IsRestricted(tag))
            context.SetGpr(4, 0);
        context.CallOriginal();
    }

    public override void OnFailure(PatchContext context) =>
        Environment.FailFast("Main menu disabled presentation failed; availability may be inconsistent.");
}
