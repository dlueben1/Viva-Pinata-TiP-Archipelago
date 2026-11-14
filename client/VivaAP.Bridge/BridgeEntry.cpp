#include "pch.h"
#include "include/rexfabric/api.h"
#include "ManagedHost.h"

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

    // Host .NET inside of our mod
    vivaap::TryStartManagedRuntime(api, mod);

    return 1; // RexFabric initialization succeeded, expects 1 as a success not 0
}