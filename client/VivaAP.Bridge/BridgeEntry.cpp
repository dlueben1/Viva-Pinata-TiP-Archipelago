
#include "pch.h"
#include "include/rexfabric/api.h"
#include "ManagedHost.h"
#include "Logger.h"
#include "GameHooks.h"

#include <Windows.h>
#include <cstddef>
#include <cwchar>
#include <string>

namespace
{
    // Read an environment variable from the ReTiP process.
    std::wstring ReadEnvironment(const wchar_t* name)
    {
        DWORD required = GetEnvironmentVariableW(
            name, nullptr, 0);

        if (required <= 1)
            return {};

        std::wstring value(required, L'\0');

        DWORD written = GetEnvironmentVariableW(
            name, value.data(), required);

        if (written == 0 || written >= required)
            return {};

        value.resize(written);
        return value;
    }

    bool HasValidLauncherConfiguration()
    {
        // First, require the launcher's explicit flag.
        if (ReadEnvironment(L"AP_LAUNCHED") != L"1")
            return false;

        // Then check the required configuration.
        if (ReadEnvironment(L"AP_SERVER").empty() ||
            ReadEnvironment(L"AP_SLOT").empty() ||
            ReadEnvironment(L"AP_SEED").empty())
            return false;

        // Check the server port.
        std::wstring portText =
            ReadEnvironment(L"AP_PORT");

        if (portText.empty())
            return false;

        wchar_t* end = nullptr;
        long port = std::wcstol(
            portText.c_str(), &end, 10);

        if (*end != L'\0' || port < 1 || port > 65535)
            return false;

        // Verify the launcher-selected save directory exists.
        std::wstring saveRoot =
            ReadEnvironment(L"REX_USER_DATA_ROOT");

        if (saveRoot.empty())
            return false;

        DWORD attributes = GetFileAttributesW(
            saveRoot.c_str());

        if (attributes == INVALID_FILE_ATTRIBUTES ||
            !(attributes & FILE_ATTRIBUTE_DIRECTORY))
            return false;

        return true;
    }
}

extern "C" REXFABRIC_MOD_EXPORT int RexFabricModInit(
    const RexFabricApi* api,
    RexFabricMod* mod)
{
    if (!api || !mod ||
        api->version < REXFABRIC_API_VERSION ||
        api->size < offsetof(RexFabricApi, log) +
        sizeof(api->log) ||
        !api->log)
    {
        return 0;
    }

    // NEW: Reject launches without the required settings.
    // This must happen before DLL pinning or hook registration.
    if (!HasValidLauncherConfiguration())
    {
        MessageBoxW(
            nullptr,
            L"Viva Pinata Archipelago requires the "
            L"TiPAP launcher.\n\n"
            L"Please start the game through TiPAP.exe "
            L"and select your Archipelago patch file.\n\n"
            L"The Archipelago mod will be disabled "
            L"for this session. The game can continue normally.",
            L"VivaAP - Launcher Required",
            MB_OK | MB_ICONWARNING | MB_SETFOREGROUND
        );

        return 0;
    }

    // Existing native startup.
    HMODULE pinned = nullptr;

    if (!GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
        GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&RexFabricModInit),
        &pinned))
    {
        return 0;
    }

    vivaap::InitializeLogger(api, mod);
    vivaap::InitializeHooks(api, mod);

    vivaap::Log(
        REXFABRIC_LOG_INFO,
        "Native bridge initialized"
    );

    vivaap::TryStartManagedRuntime(api, mod);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT void RexFabricModShutdown()
{
    vivaap::StopManagedRuntime();
    vivaap::ShutdownHooks();
}
