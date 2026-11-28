using VivaAP.Client.Utils;

namespace VivaAP.Client.Patches.SeedBag;

internal sealed class Patch_SeedBagEligibility : Patch
{
    private const uint FirstSeed = 431, EndSeed = 497;
    private static readonly HashSet<uint> controlledTags = GameUtility.ShopSeedLookupTable.Values
        .Select(seed => seed.NativeTag).ToHashSet();
    private static readonly GuestFunction GetHost = new(0x8252ECD0, 0);
    private static readonly GuestFunction IsUsable = new(0x825A2168, 1);
    private static readonly GuestFunction HasMembership = new(0x825B2508, 2);
    internal static readonly GuestFunction[] Dependencies = [GetHost, IsUsable, HasMembership];

    public override uint Address => 0x82533880;

    public override void Execute(PatchContext context)
    {
        if (context.GetGpr(4) != 0x27)
        {
            context.CallOriginal();
            return;
        }

        try
        {
            context.CallOriginal(); // Retain entitlement, Lottie access and mode prerequisites.
            if (context.GetGpr(3) != 0 && !HasEligibleSeed(context))
                context.SetGpr(3, 0);
        }
        catch
        {
            // An unreadable/incomplete pool must never authorize an empty Bag.
            context.SetGpr(3, 0);
            if (!context.HasOutcome) context.SkipOriginal();
        }
    }

    private static bool HasEligibleSeed(PatchContext context)
    {
        // Admission uses the native host's source, which need not be the input player.
        uint host = checked((uint)context.CallGuest(GetHost, []));
        if (host == 0) return false;
        uint state = context.ReadUInt32(PatchContext.Add(host, 0x1E70));
        if (state == 0) return false;
        uint source = context.ReadUInt32(PatchContext.Add(state, 0x2D4));
        if (source == 0) return false;
        bool justForFun = context.ReadUInt32(0x83DBED80) != 0;

        // Same receipt classification and metadata as the existing admission patch.
        // Test owned candidates first, with no native usability calls for unowned seeds.
        foreach (var item in ArchipelagoClient.ReceivedItems)
            if (item.ItemId >= (long)APItemType.Seed && item.ItemId < (long)APItemType.Accessory &&
                GameUtility.ShopSeedLookupTable.TryGetValue(item.ItemDisplayName, out var seed) &&
                seed.NativeTag >= FirstSeed && seed.NativeTag < EndSeed &&
                PassesNativeAdmission(context, seed.NativeTag, justForFun))
                return true;

        // Uncontrolled seeds retain canonical membership, exactly as Bag admission does.
        for (uint tag = FirstSeed; tag < EndSeed; ++tag)
            if (!controlledTags.Contains(tag) && context.CallGuest(HasMembership, [source, tag]) != 0 &&
                PassesNativeAdmission(context, tag, justForFun))
                return true;
        return false;
    }

    private static bool PassesNativeAdmission(PatchContext context, uint tag, bool justForFun)
    {
        if (context.CallGuest(IsUsable, [tag]) == 0) return false;
        if (!justForFun) return true;

        // Usability loaded and released this cached descriptor. Read its weed field
        // without acquiring a stats reference or constructing any native Bag state.
        uint cache = context.ReadPointer(0x83D1B978);
        uint unit = context.ReadUInt32(PatchContext.Add(cache, tag * 4));
        if (unit == 0) return false;
        uint stats = context.ReadUInt32(unit);
        return stats != 0 && context.ReadUInt32(PatchContext.Add(stats, 0x380)) == 0;
    }
}
