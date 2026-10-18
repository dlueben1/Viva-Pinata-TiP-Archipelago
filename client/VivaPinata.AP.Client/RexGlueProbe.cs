using System.Runtime.InteropServices;
using Reloaded.Mod.Interfaces;

namespace VivaPinata.AP.Client;

internal sealed class RexGlueProbe : IDisposable
{
    private const string RuntimeDll = "rexruntimerd.dll";

    private const string ResolverExport =
        "?ResolveIndirectFunction@runtime@rex@@YAP6AXAEAUPPCContext@@PEAE@ZI@Z";

    // Original Xbox 360 addresses
    private const uint PlayerCreditsFunction = 0x8252EDA0;
    private const uint CoinAdditionFunction = 0x8252B960;

    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100;
    private const uint PageExecute = 0x10;
    private const uint PageExecuteRead = 0x20;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint PageExecuteWriteCopy = 0x80;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ResolveFunctionDelegate(uint guestAddress);

    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<nint, nint> _onResolved;
    private readonly bool _enablePlayerStatsProbe;
    private readonly bool _enableCoinAdditionProbe;

    public RexGlueProbe(
        ILogger logger,
        bool enablePlayerStatsProbe,
        bool enableCoinAdditionProbe,
        Action<nint, nint> onResolved
    )
    {
        _logger = logger;
        _onResolved = onResolved;
        _enablePlayerStatsProbe = enablePlayerStatsProbe;
        _enableCoinAdditionProbe = enableCoinAdditionProbe;
        _logger.WriteLine("[AP] Starting ReXGlue function resolution probe.");

        _ = Task.Run(() => ProbeAsync(_cts.Token));
    }

    private async Task ProbeAsync(CancellationToken token)
    {
        if (!_enablePlayerStatsProbe && !_enableCoinAdditionProbe)
        {
            _logger.WriteLine("[AP] Both runtime probes are disabled in configuration.");
            return;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                nint module = GetModuleHandle(RuntimeDll);

                if (module != 0)
                {
                    if (!NativeLibrary.TryGetExport(module, ResolverExport, out nint export))
                    {
                        _logger.WriteLine("[AP] ReXGlue resolver export not found.");
                        return;
                    }

                    var resolve = Marshal.GetDelegateForFunctionPointer<ResolveFunctionDelegate>(
                        export
                    );

                    /// ReXGlue returns its invalid-function trap for unregistered guest
                    /// addresses. Also require executable committed memory before hooking.
                    nint invalidFunction = resolve(0);
                    nint playerFunction = _enablePlayerStatsProbe
                        ? resolve(PlayerCreditsFunction)
                        : 0;
                    nint coinAdditionFunction = _enableCoinAdditionProbe
                        ? resolve(CoinAdditionFunction)
                        : 0;

                    bool playerReady =
                        !_enablePlayerStatsProbe
                        || IsRegisteredExecutable(playerFunction, invalidFunction);
                    bool coinAdditionReady =
                        !_enableCoinAdditionProbe
                        || IsRegisteredExecutable(coinAdditionFunction, invalidFunction);

                    if (playerReady && coinAdditionReady)
                    {
                        if (_enablePlayerStatsProbe)
                            _logger.WriteLine($"[AP] Player monitor target 0x{PlayerCreditsFunction:X8} -> native 0x{playerFunction:X} validated.");
                        if (_enableCoinAdditionProbe)
                            _logger.WriteLine($"[AP][CoinAdd] Registered executable target 0x{CoinAdditionFunction:X8} -> native 0x{coinAdditionFunction:X} validated.");

                        _onResolved(playerFunction, coinAdditionFunction);

                        return;
                    }
                }

                /// The runtime may not have registered its game
                /// functions when our mod first initializes.
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[AP] Resolver probe failed: {ex}");
        }
    }

    private static bool IsRegisteredExecutable(nint function, nint invalidFunction)
    {
        if (function == 0 || function == invalidFunction)
            return false;

        if (VirtualQuery(function, out MemoryBasicInformation info, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
            return false;

        if (info.State != MemCommit || (info.Protect & PageGuard) != 0)
            return false;

        uint protection = info.Protect & 0xFF;
        return protection is PageExecute or PageExecuteRead or PageExecuteReadWrite or PageExecuteWriteCopy;
    }

    public void Dispose()
    {
        _cts.Cancel();
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true
    )]
    private static extern nint GetModuleHandle(string moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQuery(
        nint address,
        out MemoryBasicInformation buffer,
        nuint length
    );
}
