namespace VivaAP.Client.Patches.Seedos;

// Retail guest contracts. No native row/descriptor pointer is retained across guest callbacks.
internal static class SeedosNative
{
    internal const uint Null = 1, ActorTag = 0x183;
    internal static readonly GuestFunction FindController = new(0x8225E990, 2);
    internal static readonly GuestFunction WeedSubstate = new(0x824941B8, 2);
    internal static uint Actor(PatchContext c, uint strategy) => c.ReadPointer(PatchContext.Add(strategy, 0x68));
    internal static bool IsSeedos(PatchContext c, uint actor) => actor >= 0x10000 && actor <= uint.MaxValue - 0x84 &&
        c.TryReadUInt32(actor + 0x84, out uint tag) && tag == ActorTag;
    internal static uint Scene(PatchContext c, uint actor) => c.ReadPointer(PatchContext.Add(actor, 0xAA8));
    internal static uint TalkScene(PatchContext c, uint actor)
    {
        uint root = c.ReadPointer(PatchContext.Add(actor, 0xF8));
        uint control = c.ReadPointer(PatchContext.Add(root, 0x4A00));
        return c.ReadPointer(PatchContext.Add(control, 0x8E4));
    }
    private static uint Player(PatchContext c)
    {
        uint player = c.ReadUInt32(0x83DBED50);
        if (c.ReadUInt32(0x83DBED7C) != 0 || c.ReadUInt32(0x83DBED80) != 0)
        {
            uint tutorial = c.ReadUInt32(0x83DBED64);
            if (tutorial != 0) player = tutorial;
        }
        return PatchContext.GuestAddress(player);
    }
    internal static int Xp(PatchContext c)
    {
        uint player = Player(c);
        return c.ReadUInt32(PatchContext.Add(player, 8)) == 0 ? 0 : unchecked((int)c.ReadUInt32(PatchContext.Add(player, 0x10)));
    }
    internal static void FinishWeeds(PatchContext c, uint strategy) => c.CallGuest(WeedSubstate, [strategy, 0x2B]);
}
