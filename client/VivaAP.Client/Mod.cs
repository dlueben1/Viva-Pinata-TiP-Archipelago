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
    private static MemoryObserver[] observers;
    private static bool initialized;
    public static string GameName => "Viva Pinata: Trouble in Paradise";

    /// <summary>
    /// Runs the Archipelago Connection task async, so we don't block the incredibly important managed entrypoint
    /// </summary>
    private static Task? apConnectTask;

    #region Managed Entry/Exit Points

    [UnmanagedCallersOnly]
    public static int VivaApManagedEntry()
    {
        lock (_lock)
        {
            if (initialized)
                return SuccessToken;

            // Initialize Observers
            observers = new MemoryObserver[] { new GardenerLevelObserver(), new AwardObserver() };

            try
            {
                // Start the Logger
                LogUtility.Initialize();
                LogUtility.Info("Managed client initialized");

                // Register the hooks from each feature's observer
                hooks = new GameHooks();
                try
                {
                    foreach (var observer in observers)
                    {
                        observer.Register(hooks);
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Observer registration failed: {ex.Message}");
                }

                // Launch the test connection without blocking native startup
                apConnectTask = Task.Run(() =>
                    ArchipelagoClient.ConnectAsync("127.0.0.1", 38281, "Pinata")
                );

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

    #endregion
}
