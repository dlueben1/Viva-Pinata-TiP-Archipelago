using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Hooks;

internal abstract class MemoryObserver
{
    protected abstract string Name { get; }
    protected abstract uint Address { get; }

    internal void Register(GameHooks hooks)
    {
        int status = RegisterHook(hooks);
        if (status == 0)
            LogUtility.Info($"{Name} observer registered at 0x{Address:X8}.");
        else
            LogUtility.Warn(
                $"{Name} observer registration failed at 0x{Address:X8}: {Describe(status)} (status {status})."
            );
    }

    protected abstract int RegisterHook(GameHooks hooks);
    protected abstract void Observe(HookEvent item);

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
}
