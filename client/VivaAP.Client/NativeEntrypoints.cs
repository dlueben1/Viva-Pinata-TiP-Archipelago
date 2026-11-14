using System.Runtime.InteropServices;

namespace VivaAP.Client;

/// <summary>
/// The managed half of the Phase 2 sign-of-life test, invoked from the
/// native VivaAP.Bridge.dll through hostfxr's
/// load_assembly_and_get_function_pointer delegate.
///
/// Keep the entry point signature strictly blittable (no object, string,
/// nullable, or struct-by-value parameters) so the UNMANAGEDCALLERSONLY_METHOD
/// resolution in the native bridge keeps working.
/// </summary>
public static class NativeEntrypoints
{
    /// <summary>
    /// Value returned to the native bridge on success ('V', 'I', 'V', '1').
    /// Must match kManagedSuccessToken in ManagedHost.cpp.
    /// </summary>
    public const int SuccessToken = 0x56495631;

    [UnmanagedCallersOnly]
    public static int VivaApManagedEntry()
    {
        return SuccessToken;
    }
}
