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
internal static class AwardObserver
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

    internal static void Register(GameHooks hooks)
    {
        // On the message grant route the record can live inside a transient game message (msg + 0x48),
        // so the key and text must be captured by the native hook at entry, not dereferenced later.
        int status = hooks.Register(
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
        if (status == 0)
            LogUtility.Info($"Award observer registered at 0x{RememberAward:X8}.");
        else
            LogUtility.Warn(
                $"Award observer registration failed at 0x{RememberAward:X8}: {Describe(status)} (status {status})."
            );
    }

    private static string Describe(int status) =>
        status switch
        {
            1 => "invalid capture configuration",
            2 => "hook API unavailable or shutting down",
            3 => "address is not hookable",
            4 => "hook capacity exhausted",
            5 => "RexFabric rejected the hook",
            _ => "unknown error",
        };

    /// <summary>
    /// When an award is committed, identify it: name text plus 64-bit key, and whether the journal
    /// actually gained it. No specific awards are acted on yet; the stream matches the rows in
    /// .research/xp/xp_sources.csv for future location checks.
    /// </summary>
    private static void Observe(HookEvent item)
    {
        string name = (item.Flags & HookEventFlags.Text) != 0 && !string.IsNullOrEmpty(item.Text)
            ? item.Text
            : "<no text>";
        string key = (item.Flags & HookEventFlags.Key) != 0 ? $"0x{item.KeyBefore:X16}" : "<no key>";

        // The list end only moves when the inserter appended the record (stride 0x78); a duplicate
        // commit (import replay or second delivery) leaves both watches equal.
        bool fresh = (item.Flags & HookEventFlags.Field) != 0 && item.After != item.Before;

        if (fresh)
            LogUtility.Info($"Award earned: {name} [{key}].");
        else
            LogUtility.Debug($"Award re-committed (already earned): {name} [{key}].");

        // Future work: match the observed award id against .research/xp/xp_sources.csv
        // (native_event_award_id) and complete the matching checks via ArchipelagoClient.Session.
    }
}
