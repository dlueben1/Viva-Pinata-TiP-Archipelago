using System.Runtime.InteropServices;

namespace VivaAP.Client;

[StructLayout(LayoutKind.Sequential)]
internal struct HookEvent
{
    internal ulong HookId, R3, R4;
    internal uint Address, Before, After, Flags;
}

internal sealed class GameHooks : IDisposable
{
    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApHookRegister")]
    private static extern int RegisterNative(uint address, uint enabled, uint baseRegister, uint offset, out ulong id);
    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApHookUnregister")]
    private static extern int UnregisterNative(ulong id);
    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApHookPoll")]
    private static extern int PollNative(out HookEvent item);
    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApHookTakeDropped")]
    private static extern ulong TakeDroppedNative();

    private readonly Dictionary<ulong, Action<HookEvent>> subscriptions = new();
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;

    internal GameHooks()
    {
        if (Marshal.SizeOf<HookEvent>() != 40 ||
            Marshal.OffsetOf<HookEvent>(nameof(HookEvent.R3)).ToInt32() != 8 ||
            Marshal.OffsetOf<HookEvent>(nameof(HookEvent.R4)).ToInt32() != 16 ||
            Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Address)).ToInt32() != 24 ||
            Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Before)).ToInt32() != 28 ||
            Marshal.OffsetOf<HookEvent>(nameof(HookEvent.After)).ToInt32() != 32 ||
            Marshal.OffsetOf<HookEvent>(nameof(HookEvent.Flags)).ToInt32() != 36)
            throw new InvalidOperationException("Hook event ABI mismatch.");
        worker = Task.Run(DispatchAsync);
    }

    internal int Register(uint address, uint baseRegister, uint offset, Action<HookEvent> handler)
    {
        lock (gate)
        {
            int status = RegisterNative(address, 1, baseRegister, offset, out ulong id);
            if (status == 0) subscriptions.Add(id, handler);
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
                            try { handler(item); }
                            catch (Exception ex) { Logger.Error($"Hook observer failed: {ex.Message}"); }
                        }
                    }
                }
                ulong dropped = TakeDroppedNative();
                if (dropped != 0) Logger.Warn($"Dropped {dropped} hook observation(s).");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { Logger.Error($"Hook dispatcher stopped: {ex.Message}"); }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        worker.GetAwaiter().GetResult();
        lock (gate)
        {
            foreach (ulong id in subscriptions.Keys)
                if (UnregisterNative(id) == 0) Logger.Warn($"Could not unregister hook {id}.");
            subscriptions.Clear();
        }
        cancellation.Dispose();
    }
}
