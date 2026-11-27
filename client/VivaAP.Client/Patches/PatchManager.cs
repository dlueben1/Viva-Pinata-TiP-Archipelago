using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Frozen;
using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Patches;

internal static unsafe class PatchManager
{
    private static FrozenDictionary<uint, Patch> patches = FrozenDictionary<uint, Patch>.Empty;
    private static long lastWarning;
    private static bool installed;
    internal static void Start(Patch[] registrations, ReadOnlySpan<GuestFunction> dependencies)
    {
        if (installed) throw new InvalidOperationException("Patches already registered.");
        foreach (var function in dependencies)
            if (NativePatchInterop.IsHookable(function.Address) == 0)
                throw new InvalidOperationException($"Required guest function {function.Address:X8} is unavailable; no patches activated.");
        patches = registrations.ToFrozenDictionary(p => p.Address); // Guest entry address is the stable patch ID.
        var specifications = new NativePatchInterop.Specification[registrations.Length];
        for (int i = 0; i < registrations.Length; ++i)
            specifications[i] = new(registrations[i].Address, registrations[i].Address);
        fixed (NativePatchInterop.Specification* data = specifications)
        {
            int result = NativePatchInterop.Install(&Dispatch, data, (uint)specifications.Length);
            if (result != 0) throw new InvalidOperationException($"Complete managed patch set could not be installed ({result}); no patches activated.");
        }
        installed = true;
        LogUtility.Info($"Activated {patches.Count} managed entry patches.");
    }

    internal static void Stop()
    {
        NativePatchInterop.Stop(); // Synchronous admission close and drain; no callback on return.
        installed = false;
        patches = FrozenDictionary<uint, Patch>.Empty;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispatch(uint id, ulong invocation)
    {
        // Every operation, including recovery, stays inside this exception boundary.
        try
        {
            var context = new PatchContext(invocation);
            if (!patches.TryGetValue(id, out var patch)) Environment.FailFast("Unknown native patch identifier.");
            try
            {
                patch.Execute(context);
                if (!context.HasOutcome) throw new InvalidOperationException("Patch returned without an execution outcome.");
            }
            catch (Exception ex)
            {
                Warn(patch.Name, ex);
                patch.OnFailure(context);
                if (!context.HasOutcome) Environment.FailFast($"{patch.Name} recovery supplied no outcome.");
            }
        }
        catch (Exception ex) { Environment.FailFast("Managed patch recovery failed; guest state cannot be safely resumed.", ex); }
    }

    private static void Warn(string name, Exception error)
    {
        long now = Environment.TickCount64;
        long previous = Volatile.Read(ref lastWarning);
        if (now - previous < 5000 || Interlocked.CompareExchange(ref lastWarning, now, previous) != previous) return;
        try { LogUtility.Warn($"Managed patch {name}: {error.Message}"); } catch { /* Logging cannot change recovery. */ }
    }

}
