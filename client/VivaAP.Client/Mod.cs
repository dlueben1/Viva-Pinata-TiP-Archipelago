using System.Runtime.InteropServices;
using Archipelago.MultiClient.Net;
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

                // Quick Validation test
                if (
                    string.IsNullOrWhiteSpace(ArchipelagoClient.Host)
                    || string.IsNullOrWhiteSpace(ArchipelagoClient.Slot)
                    || string.IsNullOrWhiteSpace(ArchipelagoClient.Seed)
                    || !ArchipelagoClient.Port.HasValue
                    || ArchipelagoClient.Port.Value is < 1 or > 65535
                )
                {
                    LogUtility.Error("Invalid Archipelago launch configuration.");
                    return 0;
                }

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

                LogUtility.Info(
                    $"Launcher detected: {Environment.GetEnvironmentVariable("AP_LAUNCHED")}"
                );
                LogUtility.Info(
                    $"Archipelago server: {Environment.GetEnvironmentVariable("AP_SERVER")}"
                );
                LogUtility.Info(
                    $"Archipelago slot: {Environment.GetEnvironmentVariable("AP_SLOT")}"
                );
                LogUtility.Info(
                    $"Save root: {Environment.GetEnvironmentVariable("REX_USER_DATA_ROOT")}"
                );

                // Launch the Archipelago Connection as a background task, so we don't block the managed entrypoint
                apConnectTask = Task.Run(async () =>
                {
                    var result = await ArchipelagoClient.ConnectAsync(
                        ArchipelagoClient.Host,
                        ArchipelagoClient.Port.Value,
                        ArchipelagoClient.Slot,
                        string.IsNullOrWhiteSpace(ArchipelagoClient.Password)
                            ? null
                            : ArchipelagoClient.Password
                    );
                });

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
                NativeStateSynchronizer.Stop();
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
