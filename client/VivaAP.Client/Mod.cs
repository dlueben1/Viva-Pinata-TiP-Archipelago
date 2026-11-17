using System.Runtime.InteropServices;
using VivaAP.Client.Hooks;
using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client;

public static class Mod
{
    public const int SuccessToken = 0x56495631;
    private static readonly object _lock = new();
    private static GameHooks? hooks;
    private static bool initialized;
    public static string GameName => "Viva Pinata: Trouble in Paradise";

    [UnmanagedCallersOnly]
    public static int VivaApManagedEntry()
    {
        lock (_lock)
        {
            if (initialized)
                return SuccessToken;
            try
            {
                // Start the Logger
                LogUtility.Initialize();
                LogUtility.Info("Managed client initialized");

                // Register the hooks from each feature's observer
                hooks = new GameHooks();
                try
                {
                    GardenerLevelObserver.Register(hooks);
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Gardener Level observer registration failed: {ex.Message}");
                }

                // Connect to Archipelago (this method will change later when not hardcoded) (TODO)

                // Mark the managed runtime as initialized so we don't try to start it again
                initialized = true;
                return SuccessToken;
            }
            catch (Exception ex)
            {
                hooks?.Dispose();
                hooks = null;
                LogUtility.Error($"Managed startup failed: {ex.Message}");
                return 0;
            }
        }
    }

    [UnmanagedCallersOnly]
    public static void VivaApManagedShutdown()
    {
        lock (_lock)
        {
            try
            {
                hooks?.Dispose();
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Managed shutdown failed: {ex.Message}");
            }

            hooks = null;
        }
    }
}
