using System.Runtime.InteropServices;

namespace VivaAP.Client.Interop;

internal static unsafe class NativePatchInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Specification(uint id, uint address)
    {
        internal readonly uint Id = id;
        internal readonly uint Address = address;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuestCall
    {
        internal uint Address, Count;
        internal fixed ulong Arguments[8];
        internal ulong Result;
    }

    [DllImport(InteropConstants.NativeDLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApPatchInstall")]
    internal static extern int Install(delegate* unmanaged[Cdecl]<uint, ulong, void> dispatcher,
        Specification* specifications, uint count);

    [DllImport(InteropConstants.NativeDLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApPatchAccess")]
    internal static extern int Access(ulong invocation, uint operation, uint argument, void* data, uint size);

    [DllImport(InteropConstants.NativeDLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApPatchStop")]
    internal static extern void Stop();

    [DllImport(InteropConstants.NativeDLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApPatchIsHookable")]
    internal static extern int IsHookable(uint guestAddress);

}
