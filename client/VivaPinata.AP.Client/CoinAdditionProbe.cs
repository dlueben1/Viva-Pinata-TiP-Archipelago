using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using ReloadedIIHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;
using X64CallingConvention = Reloaded.Hooks.Definitions.X64.CallingConventions;
using X64Function = Reloaded.Hooks.Definitions.X64.FunctionAttribute;

namespace VivaPinata.AP.Client;

/// <summary>
/// Passively records natural calls to the recompiled Chocolate Coin add helper.
/// The callback only reads registers/memory and writes to a preallocated bounded queue.
/// </summary>
internal sealed class CoinAdditionProbe : IDisposable
{
    private const int R3Offset = 0x00;
    private const int R4Offset = 0x20;
    private const uint CoinsOffset = 0x04;
    private const int QueueCapacity = 256;

    [X64Function(X64CallingConvention.Microsoft)]
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RecompiledPpcFunction(nint context, nint memoryBase);

    private readonly ILogger _logger;
    private readonly IHook<RecompiledPpcFunction> _hook;
    private readonly CancellationTokenSource _cts = new();
    private readonly BoundedQueue _queue = new(QueueCapacity);
    private long _droppedSamples;

    public CoinAdditionProbe(ILogger logger, ReloadedIIHooks hooks, nint resolvedFunction)
    {
        _logger = logger;
        _hook = hooks.CreateHook<RecompiledPpcFunction>(OnCoinAddition, resolvedFunction.ToInt64());
        _hook.Activate();
        _logger.WriteLine("[AP][CoinAdd] Passive hook activated at guest 0x8252B960.");
        _ = Task.Run(() => DrainLogQueueAsync(_cts.Token));
    }

    private void OnCoinAddition(nint context, nint memoryBase)
    {
        Sample sample = default;
        bool captured = TryCaptureBefore(context, memoryBase, ref sample);

        // Always preserve the original recompiled function and its exact incoming context.
        _hook.OriginalFunction(context, memoryBase);

        if (!captured)
            return;

        if (TryReadBalance(memoryBase, sample.PlayerGuestAddress, out int after))
        {
            sample.BalanceAfter = after;
            sample.HasBalanceAfter = true;
        }

        if (!_queue.TryEnqueue(sample))
            Interlocked.Increment(ref _droppedSamples);
    }

    private static bool TryCaptureBefore(nint context, nint memoryBase, ref Sample sample)
    {
        if (context == 0 || memoryBase == 0)
            return false;

        try
        {
            /// ReXGlue PPCContext has an 8-byte register union: r3 at +0, r4 at +0x20.
            /// Guest pointers occupy r3.u32. Static instructions at 0x8252B998-0x8252B9B0
            /// consume r4 as the amount and use word arithmetic; r5 is not the delta half.
            sample.PlayerGuestAddress = unchecked((uint)Marshal.ReadInt32(context, R3Offset));
            sample.R4Raw = unchecked((ulong)Marshal.ReadInt64(context, R4Offset));
            sample.ProposedDelta = unchecked((uint)sample.R4Raw);

            if (sample.PlayerGuestAddress == 0)
                return true;

            if (TryReadBalance(memoryBase, sample.PlayerGuestAddress, out int before))
            {
                sample.BalanceBefore = before;
                sample.HasBalanceBefore = true;
            }

            return true;
        }
        catch
        {
            // Observational failures must not prevent the original game function from running.
            return false;
        }
    }

    private static bool TryReadBalance(nint memoryBase, uint playerGuestAddress, out int balance)
    {
        balance = 0;
        if (memoryBase == 0 || playerGuestAddress == 0)
            return false;

        try
        {
            ulong hostAddress = unchecked((ulong)memoryBase.ToInt64()) + playerGuestAddress + CoinsOffset;
            int guestBigEndian = Marshal.ReadInt32(new nint(unchecked((long)hostAddress)));
            balance = BinaryPrimitives.ReverseEndianness(guestBigEndian);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DrainLogQueueAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                while (_queue.TryDequeue(out Sample sample))
                    LogSample(sample);

                long dropped = Interlocked.Exchange(ref _droppedSamples, 0);
                if (dropped > 0)
                    _logger.WriteLine($"[AP][CoinAdd] Dropped {dropped} samples; bounded log queue was full.");

                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException)
        {
            while (_queue.TryDequeue(out Sample sample))
                LogSample(sample);
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[AP][CoinAdd] Log worker failed: {ex}");
        }
    }

    private void LogSample(Sample sample)
    {
        string before = sample.HasBalanceBefore ? sample.BalanceBefore.ToString() : "unreadable";
        string after = sample.HasBalanceAfter ? sample.BalanceAfter.ToString() : "unreadable";
        string change = sample.HasBalanceBefore && sample.HasBalanceAfter
            ? $"; observedChange={sample.BalanceAfter - sample.BalanceBefore:+#;-#;0}"
            : string.Empty;

        _logger.WriteLine(
            $"[AP][CoinAdd] player=0x{sample.PlayerGuestAddress:X8} "
                + $"r4=0x{sample.R4Raw:X16} delta_u32={sample.ProposedDelta} "
                + $"balanceBefore={before} balanceAfter={after}{change}"
        );
    }

    public void Dispose()
    {
        _cts.Cancel();
        _hook.Disable();
    }

    private struct Sample
    {
        public uint PlayerGuestAddress;
        public ulong R4Raw;
        public uint ProposedDelta;
        public int BalanceBefore;
        public int BalanceAfter;
        public bool HasBalanceBefore;
        public bool HasBalanceAfter;
    }

    /// <summary>A fixed-size, non-blocking MPMC queue. Storage is allocated at probe startup.</summary>
    private sealed class BoundedQueue
    {
        private sealed class Cell
        {
            public long Sequence;
            public Sample Value;
        }

        private readonly Cell[] _cells;
        private readonly int _mask;
        private long _enqueuePosition;
        private long _dequeuePosition;

        public BoundedQueue(int capacity)
        {
            if (capacity < 2 || (capacity & (capacity - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");

            _cells = new Cell[capacity];
            _mask = capacity - 1;
            for (int i = 0; i < capacity; i++)
                _cells[i] = new Cell { Sequence = i };
        }

        public bool TryEnqueue(Sample value)
        {
            while (true)
            {
                long position = Volatile.Read(ref _enqueuePosition);
                Cell cell = _cells[(int)position & _mask];
                long sequence = Volatile.Read(ref cell.Sequence);
                long difference = sequence - position;

                if (difference == 0)
                {
                    if (Interlocked.CompareExchange(ref _enqueuePosition, position + 1, position) != position)
                        continue;

                    cell.Value = value;
                    Volatile.Write(ref cell.Sequence, position + 1);
                    return true;
                }

                if (difference < 0)
                    return false;
            }
        }

        public bool TryDequeue(out Sample value)
        {
            while (true)
            {
                long position = Volatile.Read(ref _dequeuePosition);
                Cell cell = _cells[(int)position & _mask];
                long sequence = Volatile.Read(ref cell.Sequence);
                long difference = sequence - (position + 1);

                if (difference == 0)
                {
                    if (Interlocked.CompareExchange(ref _dequeuePosition, position + 1, position) != position)
                        continue;

                    value = cell.Value;
                    Volatile.Write(ref cell.Sequence, position + _cells.Length);
                    return true;
                }

                if (difference < 0)
                {
                    value = default;
                    return false;
                }
            }
        }
    }
}
