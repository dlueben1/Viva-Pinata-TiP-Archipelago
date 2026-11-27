using System.Buffers.Binary;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Patches.Seedos;

internal static class SeedosSelectionPolicy
{
    private const int RowCount = 66, RowSize = 0x14;
    private const uint FirstTag = 431;
    private static readonly Dictionary<uint, string> controlledSeeds = GameUtility.ShopSeedLookupTable.Values
        .ToDictionary(seed => seed.NativeTag, seed => seed.DisplayName);
    private static readonly HashSet<uint> previouslySelected = new();

    internal static bool Allows(uint tag) =>
        !controlledSeeds.TryGetValue(tag, out var name) ||
        ArchipelagoClient.ReceivedItems.Any(item =>
            IsSeedReceipt(item.ItemId) && item.ItemDisplayName == name);

    private static bool IsSeedReceipt(long itemId) =>
        itemId >= (long)APItemType.Seed && itemId < (long)APItemType.Accessory;

    private static int Candidates(PatchContext c, uint scene, int xp, uint weed,
        Span<uint> candidates)
    {
        uint controller = c.ReadUInt32(PatchContext.Add(scene, 0x4B18));
        if (controller == 0)
        {
            uint root = c.ReadPointer(scene);
            uint list = PatchContext.Add(root, 0xEC); // Inline controller list, not a pointer slot.
            controller = checked((uint)c.CallGuest(SeedosNative.FindController, [list, 0x8A]));
        }
        if (controller == 0) return 0;
        uint table = c.ReadUInt32(PatchContext.Add(controller, 0x1434));
        if (table == 0) return 0;
        Span<byte> rows = stackalloc byte[RowCount * RowSize];
        c.ReadBytes(table, rows); // One bounded bulk transfer; bytes are guest endian.
        // Use the same item names and seed classification as native receipt synchronization.
        Span<bool> received = stackalloc bool[RowCount];
        received.Clear();
        foreach (var item in ArchipelagoClient.ReceivedItems)
        {
            if (IsSeedReceipt(item.ItemId) &&
                GameUtility.ShopSeedLookupTable.TryGetValue(item.ItemDisplayName, out var seed))
                received[(int)(seed.NativeTag - FirstTag)] = true;
        }

        int count = 0;
        for (int i = 0; i < RowCount; ++i)
        {
            ReadOnlySpan<byte> row = rows.Slice(i * RowSize, RowSize);
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(row);
            uint usable = BinaryPrimitives.ReadUInt32BigEndian(row[4..]);
            int threshold = BinaryPrimitives.ReadInt32BigEndian(row[8..]);
            uint classification = BinaryPrimitives.ReadUInt32BigEndian(row[12..]);
            if (tag != FirstTag + i || usable > 1 || classification > 1)
                throw new InvalidOperationException("Native Seedos candidate table does not match the qualified retail layout.");
            if (usable == 0 || classification != weed) continue;
            if (controlledSeeds.ContainsKey(tag) ? !received[i] : threshold > xp) continue;
            candidates[count++] = tag;
        }
        return count;
    }

    internal static bool HasCandidates(PatchContext c, uint scene, bool weeds)
    {
        Span<uint> tags = stackalloc uint[RowCount];
        return Candidates(c, scene, SeedosNative.Xp(c), weeds ? 1u : 0u, tags) != 0;
    }

    internal static uint Select(PatchContext c, uint scene, int xp, uint weed)
    {
        Span<uint> tags = stackalloc uint[RowCount];
        int count = Candidates(c, scene, xp, weed, tags);
        if (count == 0) return SeedosNative.Null;

        lock (previouslySelected)
        {
            // Partition unseen candidates into the front of the existing buffer.
            int unseen = 0;
            for (int i = 0; i < count; ++i)
            {
                if (previouslySelected.Contains(tags[i])) continue;
                (tags[unseen], tags[i]) = (tags[i], tags[unseen]);
                ++unseen;
            }
            uint selected = tags[Random.Shared.Next(unseen == 0 ? count : unseen)];
            previouslySelected.Add(selected);
            return selected;
        }
    }
}
