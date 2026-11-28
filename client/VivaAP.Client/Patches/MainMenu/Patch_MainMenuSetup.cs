namespace VivaAP.Client.Patches.MainMenu;

internal sealed class Patch_MainMenuSetup : Patch
{
    public override uint Address => 0x823D2250;

    public override void Execute(PatchContext context)
    {
        uint controller = context.GetGuestAddress(3);
        context.CallOriginal(); // Native initialization and permitted options stay intact.

        for (uint i = 0; i < MainMenuAvailability.SignCount; ++i)
        {
            uint actor = context.ReadUInt32(PatchContext.Add(controller, MainMenuAvailability.SignPointers + 4 * i));
            if (actor == 0) continue;
            uint tag = context.ReadUInt32(PatchContext.Add(actor, MainMenuAvailability.ActorTag));
            if (MainMenuAvailability.IsRestricted(tag))
                MainMenuAvailability.RestrictSign(context, actor);
        }
    }

    public override void OnFailure(PatchContext context) =>
        Environment.FailFast("Main menu restriction setup failed; availability may be inconsistent.");
}
