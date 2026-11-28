using VivaAP.Client.Utils;

namespace VivaAP.Client.Patches.SeedBag;

internal sealed class Patch_SeedBagAdmission : Patch
{
    [ThreadStatic]
    private static (uint Tag, bool Received)? pendingMembership;

    public override uint Address => 0x823056C8;

    public override void Execute(PatchContext context)
    {
        var previous = pendingMembership;
        // Every admission owns its scope, including unrelated nested admissions.
        pendingMembership = null;
        try
        {
            uint bag = context.GetGuestAddress(3);
            ulong tag = context.GetGpr(4);
            if (context.ReadUInt32(PatchContext.Add(bag, 0xEC4)) == 0x27 &&
                context.ReadUInt32(PatchContext.Add(bag, 0xEC8)) == 0x1C)
            {
                var seed = GameUtility.ShopSeedLookupTable.Values.FirstOrDefault(item => item.NativeTag == tag);
                if (seed != null)
                {
                    bool received = ArchipelagoClient.ReceivedItems.Any(item =>
                        item.ItemId >= (long)APItemType.Seed &&
                        item.ItemId < (long)APItemType.Accessory &&
                        item.ItemDisplayName == seed.DisplayName);
                    pendingMembership = (seed.NativeTag, received);
                }
            }

            context.CallOriginal();
        }
        finally
        {
            pendingMembership = previous;
        }
    }

    public override void OnFailure(PatchContext context)
    {
        // The framework's original fallback must also be isolated from an outer admission.
        var previous = pendingMembership;
        pendingMembership = null;
        try { base.OnFailure(context); }
        finally { pendingMembership = previous; }
    }

    internal static bool TryConsumeMembership(ulong tag, out bool received)
    {
        if (pendingMembership is { } pending && pending.Tag == tag)
        {
            pendingMembership = null;
            received = pending.Received;
            return true;
        }

        received = false;
        return false;
    }
}
