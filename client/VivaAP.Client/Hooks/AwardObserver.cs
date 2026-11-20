using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Hooks;

/// <summary>
/// Observer for the earned-award journal insert (research name `meRememberAward`, guest 0x8252B498).
/// Every award commit — the message grant route, the deferred flush, and save imports — calls it with
/// the player in r3 and a 0x78-byte award record in r4. The routine has no return contract: it dedups on
/// the u64 key at record + 0x70 and only appends misses to the earned list at player + 0x1E48/+0x1E4C,
/// so watching that list end tells a fresh award from a duplicate.
/// See .research/awards/qwen-native-awards.md (statically verified; runtime untested).
/// </summary>
internal sealed class AwardObserver : MemoryObserver
{
    private const uint RememberAward = 0x8252B498;

    // u32 watch: end of the player's earned-award list (begin is +0x1E48). It only moves when the
    // record was new; the dedup walk leaves the list untouched.
    private const uint EarnedListEnd = 0x1E4C;

    // Record captures: u64 dedup/identity key at record + 0x70 and the award identifier text at
    // record + 0x2C, e.g. "animal_breed_gold2_ant" — the suffix after "aid_reqspinata_pinata_awards_"
    // on native_event_award_id rows in .research/xp/xp_sources.csv (exact runtime contents unverified).
    private const uint AwardKeyOffset = 0x70;
    private const uint AwardTextOffset = 0x2C;

    protected override string Name => "Award";
    protected override uint Address => RememberAward;

    private const long _archipelagoLocationIdOffset = 200;

    /// <summary>
    /// Gets the Location ID of a given award's check
    /// </summary>
    private long? GetAwardLocationId(string name)
    {
        var award = Array.FindIndex(GameUtility.Awards, a => a.EventId.Contains(name));
        if (award == -1)
        {
            LogUtility.Error($"Award with ID '{name}' not found in Awards collection.");
            return null;
        }
        return award + _archipelagoLocationIdOffset;
    }

    public AwardObserver() { }

    protected override int RegisterHook(GameHooks hooks)
    {
        // On the message grant route the record can live inside a transient game message (msg + 0x48),
        // so the key and text must be captured by the native hook at entry, not dereferenced later.
        return hooks.Register(
            RememberAward,
            1,
            3,
            EarnedListEnd,
            1,
            4,
            AwardKeyOffset,
            1,
            4,
            AwardTextOffset,
            Observe
        );
    }

    /// <summary>
    /// When an award is earned, log it
    /// </summary>
    protected override void Observe(HookEvent item)
    {
        // A usable observation for new-award detection needs both the earned-list watch and a valid
        // entry-time key. The key is taken at hook entry only: on the message grant route the record
        // lives inside a transient game message and may be freed once the call returns.
        bool fieldOk = (item.Flags & HookEventFlags.Field) != 0;
        bool keyOk =
            (item.Flags & HookEventFlags.Key) != 0
            && item.KeyBefore != 0
            && item.KeyBefore != 0xFFFFFFFFFFFFFFFF;

        string name =
            (item.Flags & HookEventFlags.Text) != 0 && !string.IsNullOrEmpty(item.Text)
                ? item.Text
                : "<no text>";

        if (!keyOk)
        {
            // Cannot identify or dedup without a key; never report this as earned.
            LogUtility.Warn(
                $"Award commit with unusable key capture (flags 0x{item.Flags:X}, "
                    + $"key 0x{item.KeyBefore:X16}): {name}."
            );
            return;
        }

        // Production must instead verify an absent-to-present transition of the award key in the
        // earned ledger (player + 0x1E48..+0x1E4C, stride 0x78, key at + 0x70); not built yet.
        bool fresh = fieldOk && item.After != item.Before;

        if (fresh)
            LogUtility.Info($"Award earned: {name} [0x{item.KeyBefore:X16}].");
        else
            LogUtility.Debug(
                $"Award re-committed (already earned{(!fieldOk ? ", list watch unavailable" : "")}): "
                    + $"{name} [0x{item.KeyBefore:X16}]."
            );

        // Get the Check/Location ID for this award
        var locationId = GetAwardLocationId(name);
        if (locationId == null)
        {
            LogUtility.Error($"Archipelago Location ID for award '{name}' not found.");
            return;
        }

        // Send the check (the server should handle deduplication if for some reason we're re-sending this check)
        if (
            ArchipelagoClient.Session != null
            && ArchipelagoClient.Session!.Locations.AllMissingLocations.Contains(locationId.Value)
        )
        {
            ArchipelagoClient.Session!.Locations.CompleteLocationChecks(locationId.Value);
        }
        // Log a warning if this didn't work - it may be for a valid reason, like the check not being in logic for this slot.
        else
        {
            LogUtility.Warn(
                $"Archipelago Location ID {locationId.Value} for award '{name}' already completed or not present in current slot."
            );
        }
    }
}
