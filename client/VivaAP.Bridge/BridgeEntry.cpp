#include "pch.h"
#include "include/rexfabric/api.h"
#include "ManagedHost.h"
#include "Logger.h"
#include "GameHooks.h"
#include "SyncHooks/PretztailRestriction.h"
#include <Windows.h>
#include <cstddef>

extern "C" REXFABRIC_MOD_EXPORT int RexFabricModInit(const RexFabricApi* api, RexFabricMod* mod)
{
    if (!api || !mod || api->version < REXFABRIC_API_VERSION ||
        api->size < offsetof(RexFabricApi, log) + sizeof(api->log) || !api->log) return 0;
    // The API does not promise that unhook waits for in-flight callbacks. Keep
    // callback code and immutable slot storage resident for the process lifetime.
    HMODULE pinned = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&RexFabricModInit), &pinned)) return 0;
    vivaap::InitializeLogger(api, mod);
    vivaap::InitializeHooks(api, mod);
    vivaap::InitializePretztailRestriction(api, mod);
    vivaap::Log(REXFABRIC_LOG_INFO, "Native bridge initialized");
    vivaap::TryStartManagedRuntime(api, mod);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT void RexFabricModShutdown()
{
    vivaap::StopManagedRuntime();
    vivaap::ShutdownPretztailRestriction();
    vivaap::ShutdownHooks();
}
