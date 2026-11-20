using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Interop;

internal sealed class PretztailRestriction : IDisposable
{
    private const string OptIn = "VIVAAP_PRETZTAIL_EXPERIMENT";
    private const string ArmFile = "pretztail-sold-out.arm";
    private const string GameHash = "7F9BA911BA83B592F7AAAC6CA173C595543162B9DC97C6686374E94472E5741D";
    private const string RuntimeHash = "BB60CC6B9BF8D74EE8FCACA2317C9FBA6173CE60DA9DF5CE06BB64DD58FEED0A";
    private const uint ReelDisabled = 1, PlacementDenied = 2, ReelRejected = 4, PlacementRejected = 8;

    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int VivaApPretztailGetUserDataRoot([Out] byte[] buffer, int capacity);

    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int VivaApPretztailPrepare();

    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern int VivaApPretztailSetRestricted(int restricted);

    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint VivaApPretztailTakeDiagnostics();

    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;

    private PretztailRestriction(string root)
    {
        worker = Task.Run(() => WatchAsync(root, cancellation.Token));
    }

    internal static PretztailRestriction? TryStart()
    {
        if (Environment.GetEnvironmentVariable(OptIn) != "1")
            return null;

        try
        {
            var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VivaAP-Pretztail-Test"))
            );
            var buffer = new byte[4096];
            var length = VivaApPretztailGetUserDataRoot(buffer, buffer.Length);
            var nativeRoot = length > 0 && length < buffer.Length && buffer[length] == 0
                ? new UTF8Encoding(false, true).GetString(buffer, 0, length)
                : "";
            if (!Path.IsPathFullyQualified(nativeRoot)
                || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeRoot))
                    .Equals(root, StringComparison.OrdinalIgnoreCase)
                || !IsTestDirectory(root))
            {
                LogUtility.Warn($"Pretztail experiment disabled. ReXFabric must use the dedicated test root {root}.");
                return null;
            }

            using var process = Process.GetCurrentProcess();
            var runtime = process.Modules.Cast<ProcessModule>().FirstOrDefault(module =>
                module.ModuleName.Equals("rexruntimerd.dll", StringComparison.OrdinalIgnoreCase)
            );
            if (!MatchesHash(Environment.ProcessPath, GameHash) || !MatchesHash(runtime?.FileName, RuntimeHash))
            {
                LogUtility.Warn("Pretztail experiment disabled. The loaded game or ReXFabric build is unsupported.");
                return null;
            }

            var result = VivaApPretztailPrepare();
            if (result != 0)
            {
                var reason = result switch
                {
                    1 => "required ReXFabric API functions are missing",
                    2 => "a required guest entry is unavailable",
                    3 => "the native reel hook failed to register",
                    4 => "the native placement hook failed to register",
                    _ => $"native setup returned {result}",
                };
                LogUtility.Warn($"Pretztail experiment disabled because {reason}.");
                return null;
            }

            LogUtility.Info("Pretztail experiment ready. Both native hooks are registered, with restriction off until armed.");
            return new PretztailRestriction(root);
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Pretztail experiment setup failed and restriction is off. {ex.Message}");
            return null;
        }
    }

    private static bool IsTestDirectory(string root) => Directory.Exists(root)
        && (File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0;

    private static bool MatchesHash(string? path, string expected)
    {
        if (path is null)
            return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)) == expected;
    }

    private static async Task WatchAsync(string root, CancellationToken token)
    {
        var armPath = Path.Combine(root, ArmFile);
        var restricted = false;
        uint reported = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsTestDirectory(root))
                    throw new InvalidOperationException("The dedicated test directory is unavailable.");

                var requested = File.Exists(armPath);
                if (requested != restricted)
                {
                    VivaApPretztailTakeDiagnostics();
                    if (VivaApPretztailSetRestricted(requested ? 1 : 0) != 1)
                        throw new InvalidOperationException("The native bridge rejected the restriction state.");

                    restricted = requested;
                    reported = 0;
                    LogUtility.Info(restricted
                        ? "Pretztail restriction armed. Awaiting a qualified local Willy reel or placement."
                        : "Pretztail restriction off. Reopen Willy or rebuild the house reel for native availability."
                    );
                }

                var diagnostics = VivaApPretztailTakeDiagnostics() & ~reported;
                if ((diagnostics & ReelDisabled) != 0)
                    LogUtility.Info("Pretztail reel verified. Copied house row set to native Sold Out.");
                if ((diagnostics & PlacementDenied) != 0)
                    LogUtility.Info("Pretztail placement verified and denied before purchase.");
                if ((diagnostics & ReelRejected) != 0)
                    LogUtility.Warn("Pretztail reel qualification failed. The native reel was left alone for that call.");
                if ((diagnostics & PlacementRejected) != 0)
                    LogUtility.Warn("Pretztail placement qualification failed. The game handled placement for that call.");
                reported |= diagnostics;

                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogUtility.Warn($"Pretztail experiment stopped. {ex.Message}");
        }
        finally
        {
            VivaApPretztailSetRestricted(0);
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try
        {
            worker.GetAwaiter().GetResult();
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
