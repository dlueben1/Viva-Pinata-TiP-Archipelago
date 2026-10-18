using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using ReloadedIIHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;
using X64CallingConvention = Reloaded.Hooks.Definitions.X64.CallingConventions;
using X64Function = Reloaded.Hooks.Definitions.X64.FunctionAttribute;

namespace VivaPinata.AP.Client;

internal sealed class PlayerHookProbe : IDisposable
{
    private const int StatsReadStartOffset = 0x04;
    private const int StatsReadLength = 0x14;

    [X64Function(X64CallingConvention.Microsoft)]
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PpcFunction(nint context, nint memoryBase);

    private readonly ILogger _logger;
    private readonly IHook<PpcFunction> _hook;
    private readonly CancellationTokenSource _cts = new();

    // Published by the native callback, read by the coin watcher.
    private int _playerGuestAddress;
    private long _memoryBase;

    public PlayerHookProbe(ILogger logger, ReloadedIIHooks hooks, nint resolvedFunction)
    {
        _logger = logger;

        _hook = hooks.CreateHook<PpcFunction>(OnPlayerCreditsUpdate, resolvedFunction.ToInt64());

        _hook.Activate();

        _logger.WriteLine("[AP] Player hook activated.");

        _ = Task.Run(() => MonitorCoinsAsync(_cts.Token));
    }

    private void OnPlayerCreditsUpdate(nint context, nint memoryBase)
    {
        try
        {
            if (context != 0 && memoryBase != 0)
            {
                uint guestPlayer = unchecked((uint)Marshal.ReadInt32(context));

                if (guestPlayer != 0)
                {
                    Interlocked.Exchange(ref _memoryBase, memoryBase.ToInt64());

                    Volatile.Write(ref _playerGuestAddress, unchecked((int)guestPlayer));
                }
            }
        }
        catch
        {
            // Avoid propagating managed exceptions into native code.
        }

        // ALWAYS preserve the existing game function.
        _hook.OriginalFunction(context, memoryBase);
    }

    private async Task MonitorCoinsAsync(CancellationToken token)
    {
        uint lastPlayer = 0;
        int? previousCoins = null;
        int? previousExperience = null;
        int? previousLevel = null;
        bool readFailed = false;

        var buffer = new byte[StatsReadLength];
        nint process = GetCurrentProcess();

        try
        {
            while (!token.IsCancellationRequested)
            {
                uint player = unchecked((uint)Volatile.Read(ref _playerGuestAddress));

                long memoryBase = Interlocked.Read(ref _memoryBase);

                if (player != 0 && memoryBase != 0)
                {
                    if (player != lastPlayer)
                    {
                        lastPlayer = player;
                        previousCoins = null;
                        previousExperience = null;
                        previousLevel = null;
                        readFailed = false;

                        _logger.WriteLine($"[AP] Captured player guest address: 0x{player:X8}");
                    }

                    /// Read one contiguous range from playerMain+0x04 through +0x17.
                    /// This includes coins (+0x04), experience (+0x10), and level (+0x14).
                    ulong hostAddress = unchecked((ulong)memoryBase) + player + StatsReadStartOffset;

                    bool success =
                        ReadProcessMemory(
                            process,
                            new nint(unchecked((long)hostAddress)),
                            buffer,
                            (nuint)buffer.Length,
                            out nuint bytesRead
                        )
                        && bytesRead == (nuint)buffer.Length;

                    if (success)
                    {
                        // Xbox 360 game data is big-endian - important to remember, MR3 is little endian
                        int coins = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4));
                        int experience = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0x0C, 4));
                        int level = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0x10, 4));

                        LogChangedValue("coins", coins, ref previousCoins, IsPlausibleCoins(coins));
                        LogChangedValue(
                            "experience (layout unverified)",
                            experience,
                            ref previousExperience,
                            IsPlausibleExperience(experience)
                        );
                        LogChangedValue(
                            "gardenerLevel (layout unverified)",
                            level,
                            ref previousLevel,
                            IsPlausibleLevel(level)
                        );

                        readFailed = false;
                    }
                    else if (!readFailed)
                    {
                        _logger.WriteLine("[AP] Player coin memory is not readable.");

                        previousCoins = null;
                        previousExperience = null;
                        previousLevel = null;
                        readFailed = true;
                    }
                }

                await Task.Delay(150, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[AP] Coin monitor failed: {ex}");
        }
    }

    private void LogChangedValue(string name, int value, ref int? previous, bool plausible)
    {
        string validity = plausible ? "plausible" : "SUSPICIOUS";

        if (!previous.HasValue)
        {
            _logger.WriteLine($"[AP] Initial {name}: {value} (0x{value:X8}; {validity})");
        }
        else if (value != previous.Value)
        {
            _logger.WriteLine(
                $"[AP] {name}: {previous.Value} -> {value} (0x{value:X8}; {validity})"
            );
        }

        previous = value;
    }

    private static bool IsPlausibleCoins(int coins) => coins >= 0;

    // These wide bounds are sanity checks only. They do not validate the proposed layout.
    private static bool IsPlausibleExperience(int experience) =>
        experience >= 0 && experience <= 1_000_000_000;

    private static bool IsPlausibleLevel(int level) => level >= 0 && level <= 100;

    public void Dispose()
    {
        _cts.Cancel();
        _hook.Disable();
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        nint process,
        nint address,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead
    );
}
