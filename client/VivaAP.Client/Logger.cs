using System.Reflection;
using System.Runtime.InteropServices;

namespace VivaAP.Client;

internal static class Logger
{
    private static bool initialized;
    internal static void Initialize()
    {
        if (initialized) return;
        NativeLibrary.SetDllImportResolver(typeof(Logger).Assembly, Resolve);
        initialized = true;
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? paths)
    {
        if (name != "VivaAP.Bridge") return IntPtr.Zero;
        string managedDirectory = Path.GetDirectoryName(assembly.Location)!;
        return NativeLibrary.Load(Path.GetFullPath(Path.Combine(managedDirectory, "..", "plugins", "VivaAP.Bridge.dll")));
    }

    [DllImport("VivaAP.Bridge", CallingConvention = CallingConvention.Cdecl, EntryPoint = "VivaApLog")]
    private static extern void LogNative(int severity, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    internal static void Debug(string message) => LogNative(0, message);
    internal static void Info(string message) => LogNative(1, message);
    internal static void Warn(string message) => LogNative(2, message);
    internal static void Error(string message) => LogNative(3, message);
}
