namespace VivaAP.Client.Patches;

internal sealed class Patch_CoinRewards : Patch
{
    internal static readonly GuestFunction Credit = new(0x8252B960, 2);
    private const uint LocalPlayer = 0x83DBED50;
    private const uint TutorialMode = 0x83DBED7C;
    private const uint JustForFunMode = 0x83DBED80;

    public override uint Address => 0x823D6390; // entitySceneControlPinataGarden::meInstTick

    public override void Execute(PatchContext context)
    {
        CoinRewards.TryGrant(context, (uint)context.GetGpr(3));
        context.CallOriginal();
    }

    internal static uint Grant(PatchContext context, uint controller, long outstanding)
    {
        if (context.ReadUInt32(TutorialMode) != 0 || context.ReadUInt32(JustForFunMode) != 0)
            return 0;

        // Wait for GardenBegin and require the player binding set by Scene Begin.
        // Scene End clears the binding and the in-garden flag.
        uint player = context.ReadUInt32(LocalPlayer);
        if (player == 0 || context.ReadUInt32(player + 0x1E6C) != 1 ||
            context.ReadUInt32(controller + 0xD0) != 1 || context.ReadUInt32(controller + 0xC8) != 0)
            return 0;

        uint profile = context.ReadUInt32(player + 0x1E70);
        uint gardenState = profile == 0 ? 0 : context.ReadUInt32(profile + 0x18);
        uint gardenBits = context.ReadUInt32(controller + 0xB8);
        if (gardenState == 0 || gardenBits == 0 || context.ReadUInt32(gardenState + 4) != gardenBits)
            return 0;

        uint balanceAddress = player + 4;
        uint before = context.ReadUInt32(balanceAddress);
        if (before >= int.MaxValue)
            return 0;

        uint amount = (uint)Math.Min(outstanding, int.MaxValue - (long)before);
        context.CallGuest(Credit, [player, amount]);
        uint after = context.ReadUInt32(balanceAddress);
        return after > before ? Math.Min(after - before, amount) : 0;
    }
}
