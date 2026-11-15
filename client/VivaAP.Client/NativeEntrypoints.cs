using System.Runtime.InteropServices;

namespace VivaAP.Client;

public static class NativeEntrypoints
{
    public const int SuccessToken = 0x56495631;
    private static readonly object Gate = new();
    private static GameHooks? hooks;
    private static bool initialized;

    [UnmanagedCallersOnly]
    public static int VivaApManagedEntry()
    {
        lock (Gate)
        {
            if (initialized) return SuccessToken;
            try
            {
                Logger.Initialize();
                Logger.Info("Managed client initialized");
                hooks = new GameHooks();
                try { GardenerLevelObserver.Register(hooks); }
                catch (Exception ex) { Logger.Warn($"Gardener Level observer registration failed: {ex.Message}"); }
                initialized = true;
                return SuccessToken;
            }
            catch (Exception ex)
            {
                try { hooks?.Dispose(); } catch { }
                hooks = null;
                try { Logger.Error($"Managed startup failed: {ex.Message}"); } catch { }
                return 0;
            }
        }
    }

    [UnmanagedCallersOnly]
    public static void VivaApManagedShutdown()
    {
        lock (Gate)
        {
            try { hooks?.Dispose(); }
            catch (Exception ex) { try { Logger.Error($"Managed shutdown failed: {ex.Message}"); } catch { } }
            hooks = null;
        }
    }
}
