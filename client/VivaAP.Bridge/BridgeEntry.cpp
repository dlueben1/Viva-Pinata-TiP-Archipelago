#include "pch.h"
#include "include/rexfabric/api.h"

#include <Windows.h>
#include <filesystem>
#include <fstream>

extern "C" REXFABRIC_MOD_EXPORT
int RexFabricModInit(
    const RexFabricApi* api,
    RexFabricMod* mod)
{
    if (!api || !mod ||
        api->version < REXFABRIC_API_VERSION ||
        !api->log)
    {
        return 0;
    }

    constexpr const char* message =
        "[VivaAP] Native bridge initialized!";

    // ReTiP/RexFabric logging
    api->log(mod, REXFABRIC_LOG_INFO, message);

    // Visual Studio debugger output
    OutputDebugStringA(message);
    OutputDebugStringA("\n");

    // Independent sign-of-life file
    try
    {
        const auto path =
            std::filesystem::temp_directory_path()
            / "VivaAP-bridge.log";

        std::ofstream log(path, std::ios::app);
        log << "RexFabricModInit reached\n";
    }
    catch (...)
    {
        // Logging failures must not crash the game.
    }

    return 1; // RexFabric initialization succeeded
}