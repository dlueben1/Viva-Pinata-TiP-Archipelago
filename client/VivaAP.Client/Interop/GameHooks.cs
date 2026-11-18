using System.Runtime.InteropServices;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct HookEvent
{
    internal ulong HookId,
        R3,
        R4,
        KeyBefore,
        KeyAfter;
    internal uint Address,
        Before,
        After,
        Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 72)]
    internal string Text;
}

/// <summary>
/// Which optional captures landed in a HookEvent (HookEvent.Flags bits).
/// </summary>
internal static class HookEventFlags
{
    /// <summary>The u32 Before/After pair at baseRegister + offset was sampled around the call.</summary>
    internal const uint Field = 1;

    /// <summary>The u64 KeyBefore/KeyAfter pair at keyBaseRegister + keyOffset was sampled around the call.</summary>
    internal const uint Key = 2;

    /// <summary>Text was read as a NUL-terminated C string at hook entry.</summary>
    internal const uint Text = 4;
}

/// <summary>
/// Handles hooking into TiP using RexFabric and our C++ Bridge
/// </summary>
internal sealed class GameHooks : IDisposable
{
    [DllImport(
        "VivaAP.Bridge",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "VivaApHookRegister"
    )]
    private static extern int RegisterNative(
        uint address,
        uint fieldEnabled,
        uint fieldBaseRegister,
        uint fieldOffset,
        uint keyEnabled,
        uint keyBaseRegister,
        uint keyOffset,
        uint textEnabled,
        uint textBaseRegister,
        uint textOffset,
        out ulong id
    );

    [DllImport(
        "VivaAP.Bridge",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "VivaApHookUnregister"
    )]
    private static extern int UnregisterNative(ulong id);

    [DllImport(
        "VivaAP.Bridge",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "VivaApHookPoll"
    )]
    private static extern int PollNative(out HookEvent item);

    [DllImport(
        "VivaAP.Bridge",
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "VivaApHookTakeDropped"
    )]
    private static extern ulong TakeDroppedNative();

    private readonly Dictionary<ulong, Action<HookEvent>> subscriptions = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;

    internal GameHooks()
    {
        if (
            Marshal.SizeOf<HookEvent>() != 128
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.R3)).ToInt32() != 8
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.R4)).ToInt32() != 16
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.KeyBefore)).ToInt32() != 24
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.KeyAfter)).ToInt32() != 32
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Address)).ToInt32() != 40
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Before)).ToInt32() != 44
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.After)).ToInt32() != 48
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Flags)).ToInt32() != 52
            || Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Text)).ToInt32() != 56
        )
            throw new InvalidOperationException("Hook event ABI mismatch.");
        worker = Task.Run(DispatchAsync);
    }

    /// <summary>
    /// Hooks a guest function and watches the u32 at baseRegister + offset across the call.
    /// </summary>
    internal int Register(uint address, uint baseRegister, uint offset, Action<HookEvent> handler) =>
        Register(address, 1, baseRegister, offset, 0, 0, 0, 0, 0, 0, handler);

    /// <summary>
    /// Hooks a guest function with up to three synchronous captures, performed on the guest thread
    /// while arguments are live: a u32 watched before/after the call (<paramref name="fieldEnabled"/>,
    /// <paramref name="fieldBaseRegister"/> + <paramref name="fieldOffset"/>), a u64 sampled at entry
    /// and exit (<paramref name="keyEnabled"/>, <paramref name="keyBaseRegister"/> +
    /// <paramref name="keyOffset"/>), and a NUL-terminated C string read at entry
    /// (<paramref name="textEnabled"/>, <paramref name="textBaseRegister"/> +
    /// <paramref name="textOffset"/>). See <see cref="HookEventFlags"/>.
    /// </summary>
    internal int Register(
        uint address,
        uint fieldEnabled,
        uint fieldBaseRegister,
        uint fieldOffset,
        uint keyEnabled,
        uint keyBaseRegister,
        uint keyOffset,
        uint textEnabled,
        uint textBaseRegister,
        uint textOffset,
        Action<HookEvent> handler
    )
    {
        lock (gate)
        {
            int status = RegisterNative(
                address,
                fieldEnabled,
                fieldBaseRegister,
                fieldOffset,
                keyEnabled,
                keyBaseRegister,
                keyOffset,
                textEnabled,
                textBaseRegister,
                textOffset,
                out ulong id
            );
            if (status == 0)
                subscriptions.Add(id, handler);
            return status;
        }
    }

    private async Task DispatchAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(100, cancellation.Token).ConfigureAwait(false);
                while (!cancellation.IsCancellationRequested && PollNative(out HookEvent item) != 0)
                {
                    lock (gate)
                    {
                        if (subscriptions.TryGetValue(item.HookId, out var handler))
                        {
                            try
                            {
                                handler(item);
                            }
                            catch (Exception ex)
                            {
                                LogUtility.Error($"Hook observer failed: {ex.Message}");
                            }
                        }
                    }
                }
                ulong dropped = TakeDroppedNative();
                if (dropped != 0)
                    LogUtility.Warn($"Dropped {dropped} hook observation(s).");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogUtility.Error($"Hook dispatcher stopped: {ex.Message}");
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        worker.GetAwaiter().GetResult();
        lock (gate)
        {
            foreach (ulong id in subscriptions.Keys)
                if (UnregisterNative(id) == 0)
                    LogUtility.Warn($"Could not unregister hook {id}.");
            subscriptions.Clear();
        }
        cancellation.Dispose();
    }
}
