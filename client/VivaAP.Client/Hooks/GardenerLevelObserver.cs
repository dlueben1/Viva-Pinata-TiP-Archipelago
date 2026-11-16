using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Hooks;

internal static class GardenerLevelObserver
{
    internal static void Register(GameHooks hooks)
    {
        const uint setter = 0x8252E798;
        int status = hooks.Register(setter, 3, 0x14, Observe);
        if (status == 0)
            LogUtility.Info($"Gardener Level observer registered at 0x{setter:X8}.");
        else
            LogUtility.Warn(
                $"Gardener Level observer registration failed at 0x{setter:X8}: {Describe(status)} (status {status})."
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

    private static void Observe(HookEvent item)
    {
        if ((item.Flags & 1) != 0 && item.After > item.Before)
            LogUtility.Info($"Gardener Level increased: {item.Before} -> {item.After}");
    }
}
