using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Hooks;

internal sealed class GardenerLevelObserver : MemoryObserver
{
    protected override string Name => "Gardener Level";
    protected override uint Address => 0x8252E798;

    protected override int RegisterHook(GameHooks hooks) =>
        hooks.Register(Address, 3, 0x14, Observe);

    /// <summary>
    /// When the player levels up, attempt to send the appropriate check
    /// </summary>
    protected override void Observe(HookEvent item)
    {
        // Only fire if the level has increased
        if ((item.Flags & 1) != 0 && item.After > item.Before)
        {
            // Log it
            LogUtility.Info($"Gardener Level increased: {item.Before} -> {item.After}");

            // Find the location for this level
            var location =
                ArchipelagoClient.Session?.Locations.GetLocationIdFromName(
                    Mod.GameName,
                    $"Reach Gardener Level {item.After}"
                ) ?? -1;

            // If the check doesn't exist, log this
            if (location == -1)
            {
                LogUtility.Warn($"Location for Gardener Level {item.After} not found.");
            }
            // Otherwise, send the check!
            else
            {
                // Send the check
                ArchipelagoClient.Session?.Locations.CompleteLocationChecks(location);
            }
        }
    }
}
