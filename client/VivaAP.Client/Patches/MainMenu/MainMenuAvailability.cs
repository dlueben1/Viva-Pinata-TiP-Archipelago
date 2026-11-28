namespace VivaAP.Client.Patches.MainMenu;

// Installed only by the AP-configured managed startup, including connection/reconnect time.
internal static class MainMenuAvailability
{
    internal const uint XboxLive = 0x87D, SystemLink = 0x87E, JustForFun = 0x880, Credits = 0x881;
    internal const uint SignPointers = 0x610, SignCount = 8, ActorTag = 0x84, PopupHandle = 0x43C;
    internal static readonly GuestFunction SetAbility = new(0x82588BF8, 3);
    internal static readonly GuestFunction SetPresentation = new(0x823D0F60, 2);
    internal static readonly GuestFunction ShowLockedPopup = new(0x824CC290, 6);
    internal static readonly GuestFunction[] Dependencies = [SetAbility, SetPresentation, ShowLockedPopup];

    // Every unlisted tag preserves vanilla availability, including Tutorial and Exit.
    internal static bool IsRestricted(uint tag) => tag is XboxLive or SystemLink or JustForFun or Credits;

    internal static bool OwnsSign(PatchContext context, uint controller, uint actor)
    {
        for (uint i = 0; i < SignCount; ++i)
            if (context.ReadUInt32(PatchContext.Add(controller, SignPointers + 4 * i)) == actor)
                return true;
        return false;
    }

    internal static void RestrictSign(PatchContext context, uint actor)
    {
        // Keep the sign selectable so native rejection can explain why it is locked.
        context.CallGuest(SetAbility, [actor, 0x50, 1]);
        context.CallGuest(SetAbility, [actor, 0x53, 0]);
        context.CallGuest(SetPresentation, [actor, 0]);
    }
}
