#include "pch.h"
#include "Logger.h"
#include <Windows.h>
namespace {
const RexFabricApi* g_api = nullptr;
RexFabricMod* g_mod = nullptr;
void Write(RexFabricLogLevel level, const char* prefix, const char* message) {
    std::string line(prefix);
    if (message) line += message;
    if (g_api && g_api->log && g_mod) g_api->log(g_mod, level, line.c_str());
    OutputDebugStringA(line.c_str());
    OutputDebugStringA("\n");
}
}
namespace vivaap {
void InitializeLogger(const RexFabricApi* api, RexFabricMod* mod) { g_api = api; g_mod = mod; }
void Log(RexFabricLogLevel level, const std::string& message) { Write(level, "[Bridge] ", message.c_str()); }
}
extern "C" REXFABRIC_MOD_EXPORT void VivaApLog(int severity, const char* message) {
    try {
        if (severity < REXFABRIC_LOG_DEBUG || severity > REXFABRIC_LOG_ERROR) severity = REXFABRIC_LOG_ERROR;
        Write(static_cast<RexFabricLogLevel>(severity), "[Client] ", message);
    } catch (...) { OutputDebugStringA("[Bridge] Managed log failed\n"); }
}
