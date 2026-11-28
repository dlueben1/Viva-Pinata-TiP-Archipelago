namespace VivaAP.Client.Patches.MainMenu;

internal sealed class Patch_MainMenuSelection : Patch
{
    public override uint Address => 0x823D2F48;

    public override void Execute(PatchContext context)
    {
        uint controller = context.GetGuestAddress(3);
        uint actor = context.GetGuestAddress(4);
        uint tag = context.ReadUInt32(PatchContext.Add(actor, MainMenuAvailability.ActorTag));
        if (!MainMenuAvailability.IsRestricted(tag) || !MainMenuAvailability.OwnsSign(context, controller, actor))
        {
            context.CallOriginal();
            return;
        }

        // Connectivity polling may have changed ability53 since setup. Reapply permission
        // immediately before native selection, without touching progression/profile stats.
        MainMenuAvailability.RestrictSign(context, actor);
        if (tag != MainMenuAvailability.JustForFun)
        {
            context.CallOriginal(); // Native locked Credits / LIVE / System Link branches.
            return;
        }

        // JFF has no native locked arm. Reuse the Credits rejection's popup, duplicate
        // guard and callback (823D4B28 clears controller+43C on dismissal).
        uint popup = PatchContext.Add(controller, MainMenuAvailability.PopupHandle);
        context.SkipOriginal(); // Void entry; its caller still consumes the action.
        if (context.ReadUInt32(popup) == uint.MaxValue)
            context.CallGuest(MainMenuAvailability.ShowLockedPopup,
                [0, popup, controller, 0xEF, 0xFFFFFFFF823D4B28UL, controller]);
    }

    public override void OnFailure(PatchContext context) =>
        Environment.FailFast("Main menu selection restriction failed; refusing unrestricted selection.");
}
