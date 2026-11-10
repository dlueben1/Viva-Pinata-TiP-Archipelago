// Phase 2 sign-of-life: initialize the .NET 10 runtime from the native
// bridge and execute one managed method in VivaAP.Client.dll.
//
// Only Microsoft's supported native hosting APIs are used:
//   nethost.dll      -> get_hostfxr_path   (official runtime discovery)
//   hostfxr.dll      -> hostfxr_initialize_for_runtime_config
//                       hostfxr_get_runtime_delegate
//   coreclr delegate -> load_assembly_and_get_function_pointer
//                       (UNMANAGEDCALLERSONLY_METHOD)
//
// Design notes:
// - Every path is derived from the location of the loaded VivaAP.Bridge.dll
//   (mods\VivaAP\plugins\..), never from the process working directory.
//   Everything is wide-char so paths with spaces are safe.
// - nethost is not link-time dependent: a deployed copy of nethost.dll next
//   to the managed assemblies is loaded by absolute path, and its official
//   get_hostfxr_path probing locates hostfxr in the global .NET install.
// - The host context is intentionally never closed: once hostpolicy/CoreCLR
//   is loaded there is no supported way to unload it, and closing the
//   context while managed code could still run is unsafe. Leaking the one
//   handle is the conservative choice.
// - Every failure path logs an actionable line and returns false. Nothing
//   here may crash ReTiP or undo the Phase 1 native sign-of-life.

#include "pch.h"
#include "ManagedHost.h"
#include "include/rexfabric/api.h"

// GetProcAddress is used for nethost; keep the header from dllimporting it.
#ifndef NETHOST_USE_AS_STATIC
#define NETHOST_USE_AS_STATIC
#endif
#include "include/nethost/nethost.h"
#include "include/nethost/hostfxr.h"
#include "include/nethost/coreclr_delegates.h"

#include <Windows.h>

#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <string>

namespace vivaap
{
namespace
{
    namespace fs = std::filesystem;

    // Must match VivaAP.Client.NativeEntrypoints.SuccessToken ("VIV1").
    constexpr int32_t kManagedSuccessToken = 0x56495631;

    constexpr char_t kManagedTypeName[] =
        L"VivaAP.Client.NativeEntrypoints, VivaAP.Client";
    constexpr char_t kManagedMethodName[] = L"VivaApManagedEntry";

    using get_hostfxr_path_fn =
        int(NETHOST_CALLTYPE*)(char_t*, size_t*, const get_hostfxr_parameters*);

    // The managed entry point is [UnmanagedCallersOnly] with a zero-argument,
    // int-returning signature. x64 Windows has a single calling convention.
    using managed_entry_fn = int32_t(*)();

    void Log(const RexFabricApi* api, RexFabricMod* mod,
             RexFabricLogLevel level, const std::string& line)
    {
        if (api && api->log && mod)
        {
            api->log(mod, level, line.c_str());
        }

        OutputDebugStringA(line.c_str());
        OutputDebugStringA("\n");

        try
        {
            const auto path = fs::temp_directory_path() / L"VivaAP-bridge.log";
            std::ofstream logFile(path, std::ios::app);
            logFile << line << '\n';
        }
        catch (...)
        {
            // Logging failures must never crash the game.
        }
    }

    std::string WideToUtf8(const std::wstring& text)
    {
        if (text.empty())
        {
            return {};
        }

        const int size = WideCharToMultiByte(
            CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
            nullptr, 0, nullptr, nullptr);
        if (size <= 0)
        {
            return {"<unprintable>"};
        }

        std::string out(static_cast<size_t>(size), '\0');
        WideCharToMultiByte(
            CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
            out.data(), size, nullptr, nullptr);
        return out;
    }

    std::string Format(const char* format, ...)
    {
        char buffer[1024];
        va_list args;
        va_start(args, format);
        const int written =
            _vsnprintf_s(buffer, sizeof(buffer),
                _TRUNCATE, format, args);
        va_end(args);
        return written < 0 ? std::string("<log truncated>") : std::string(buffer);
    }

    bool FileExists(const fs::path& path)
    {
        std::error_code ec;
        return fs::is_regular_file(path, ec);
    }

    // Directory containing the loaded VivaAP.Bridge.dll.
    fs::path GetBridgeModuleDirectory()
    {
        HMODULE self = nullptr;
        if (!GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                    GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&GetBridgeModuleDirectory),
                &self))
        {
            SetLastError(ERROR_FUNCTION_FAILED);
            return {};
        }

        std::wstring buffer(32768, L'\0');
        const DWORD length = GetModuleFileNameW(self, buffer.data(),
                                                static_cast<DWORD>(buffer.size()));
        if (length == 0 || length >= buffer.size())
        {
            return {};
        }

        buffer.resize(length);
        return fs::path(buffer).parent_path();
    }
}

bool TryStartManagedRuntime(const RexFabricApi* api, RexFabricMod* mod)
{
    try
    {
        Log(api, mod, REXFABRIC_LOG_INFO, "VivaAP: initializing .NET runtime");

        // All hosting files live in <modRoot>\managed, beside the
        // RexFabric-scanned plugins\ folder.
        const auto pluginDirectory = GetBridgeModuleDirectory();
        const DWORD gleResolve = GetLastError();
        if (pluginDirectory.empty())
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: .NET hosting aborted: could not resolve the path "
                       "of VivaAP.Bridge.dll (gle=%lu)",
                       static_cast<unsigned long>(gleResolve)));
            return false;
        }

        const auto modRoot = pluginDirectory.parent_path();
        const auto managedDirectory = modRoot / L"managed";
        const auto nethostPath = managedDirectory / L"nethost.dll";
        const auto runtimeConfigPath = managedDirectory / L"VivaAP.Client.runtimeconfig.json";
        const auto assemblyPath = managedDirectory / L"VivaAP.Client.dll";

        if (!FileExists(runtimeConfigPath))
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                "VivaAP: missing " + WideToUtf8(runtimeConfigPath.native()) +
                " (build/deploy VivaAP.Client first)");
            return false;
        }
        if (!FileExists(assemblyPath))
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                "VivaAP: missing " + WideToUtf8(assemblyPath.native()) +
                " (build/deploy VivaAP.Client first)");
            return false;
        }
        if (!FileExists(nethostPath))
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                "VivaAP: missing " + WideToUtf8(nethostPath.native()) +
                " (deploy an nethost.dll copy from the .NET host pack first)");
            return false;
        }

        // Stage 1: nethost's official get_hostfxr_path discovery.
        HMODULE nethost = LoadLibraryExW(
            nethostPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!nethost)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: failed to load %s (gle=%lu)",
                       WideToUtf8(nethostPath.native()).c_str(),
                       static_cast<unsigned long>(GetLastError())));
            return false;
        }

        const auto getHostfxrPath = reinterpret_cast<get_hostfxr_path_fn>(
            GetProcAddress(nethost, "get_hostfxr_path"));
        if (!getHostfxrPath)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: %s has no get_hostfxr_path export (gle=%lu)",
                       WideToUtf8(nethostPath.native()).c_str(),
                       static_cast<unsigned long>(GetLastError())));
            return false;
        }

        std::wstring hostfxrPath(4096, L'\0');
        size_t hostfxrPathSize = hostfxrPath.size();
        const int probeResult = getHostfxrPath(
            hostfxrPath.data(), &hostfxrPathSize, nullptr);
        if (probeResult != 0)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: get_hostfxr_path found no .NET runtime "
                       "(hr=0x%08X). Install the .NET 10 x64 runtime or set "
                       "DOTNET_ROOT.",
                       static_cast<unsigned>(static_cast<uint32_t>(probeResult))));
            return false;
        }
        hostfxrPath.resize(hostfxrPathSize > 0 ? hostfxrPathSize - 1 : 0);

        Log(api, mod, REXFABRIC_LOG_DEBUG,
            "VivaAP: hostfxr located at " + WideToUtf8(hostfxrPath));

        // Stage 2: load hostfxr and initialize the hosting components from
        // the managed app's runtimeconfig.json.
        HMODULE hostfxr = LoadLibraryExW(
            hostfxrPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!hostfxr)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: failed to load %s (gle=%lu)",
                       WideToUtf8(hostfxrPath).c_str(),
                       static_cast<unsigned long>(GetLastError())));
            return false;
        }

        const auto initializeForRuntimeConfig =
            reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
                GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config"));
        const auto getRuntimeDelegate =
            reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
                GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
        if (!initializeForRuntimeConfig || !getRuntimeDelegate)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: %s is missing hostfxr exports (gle=%lu)",
                       WideToUtf8(hostfxrPath).c_str(),
                       static_cast<unsigned long>(GetLastError())));
            return false;
        }

        hostfxr_handle hostContext = nullptr;
        int32_t hr = initializeForRuntimeConfig(
            runtimeConfigPath.c_str(), nullptr, &hostContext);
        // 0x0 Success, 0x1 Success_HostAlreadyInitialized,
        // 0x2 Success_DifferentRuntimeProperties are all usable.
        if (hr < 0 || !hostContext)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: hostfxr could not initialize %s (hr=0x%08X)",
                       WideToUtf8(runtimeConfigPath.native()).c_str(),
                       static_cast<unsigned>(static_cast<uint32_t>(hr))));
            if (hostContext)
            {
                if (const auto closeFn = reinterpret_cast<hostfxr_close_fn>(
                        GetProcAddress(hostfxr, "hostfxr_close")))
                {
                    closeFn(hostContext);
                }
            }
            return false;
        }

        Log(api, mod, REXFABRIC_LOG_INFO, "VivaAP: hostfxr initialized");

        // Stage 3: obtain load_assembly_and_get_function_pointer (this is
        // the call that actually brings CoreCLR online), then resolve the
        // managed entry point.
        void* loaderDelegate = nullptr;
        hr = getRuntimeDelegate(
            hostContext, hdt_load_assembly_and_get_function_pointer,
            &loaderDelegate);
        if (hr != 0 || !loaderDelegate)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: hostfxr_get_runtime_delegate("
                       "load_assembly_and_get_function_pointer) failed "
                       "(hr=0x%08X)",
                       static_cast<unsigned>(static_cast<uint32_t>(hr))));
            // The runtime may already be loaded and impossible to unload;
            // leave the host context open.
            return false;
        }

        const auto loadAssemblyAndGetFunctionPointer =
            reinterpret_cast<load_assembly_and_get_function_pointer_fn>(
                loaderDelegate);

        void* entryPointer = nullptr;
        hr = loadAssemblyAndGetFunctionPointer(
            assemblyPath.c_str(), kManagedTypeName, kManagedMethodName,
            UNMANAGEDCALLERSONLY_METHOD, nullptr, &entryPointer);
        if (hr != 0 || !entryPointer)
        {
            Log(api, mod, REXFABRIC_LOG_ERROR,
                Format("VivaAP: could not resolve %s.%s in %s (hr=0x%08X)",
                       "VivaAP.Client.NativeEntrypoints", "VivaApManagedEntry",
                       WideToUtf8(assemblyPath.native()).c_str(),
                       static_cast<unsigned>(static_cast<uint32_t>(hr))));
            return false;
        }

        // Stage 4: invoke the managed sign-of-life method. The host context
        // is deliberately left open (see file header): unloading the CLR is
        // unsupported and unsafe while managed code may still run.
        const auto managedEntry =
            reinterpret_cast<managed_entry_fn>(entryPointer);
        const int32_t result = managedEntry();

        if (result == kManagedSuccessToken)
        {
            Log(api, mod, REXFABRIC_LOG_INFO,
                "VivaAP: managed entry returned successfully");
            return true;
        }

        Log(api, mod, REXFABRIC_LOG_WARN,
            Format("VivaAP: managed entry returned unexpected result 0x%08X "
                   "(expected 0x%08X)",
                   static_cast<unsigned>(static_cast<uint32_t>(result)),
                   static_cast<unsigned>(static_cast<uint32_t>(kManagedSuccessToken))));
        return false;
    }
    catch (...)
    {
        Log(api, mod, REXFABRIC_LOG_ERROR,
            "VivaAP: .NET hosting aborted by an unexpected native exception");
        return false;
    }
}
}
