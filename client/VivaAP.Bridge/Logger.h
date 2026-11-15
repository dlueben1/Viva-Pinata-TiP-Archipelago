#pragma once
#include "include/rexfabric/api.h"
#include <string>
namespace vivaap {
void InitializeLogger(const RexFabricApi* api, RexFabricMod* mod);
void Log(RexFabricLogLevel level, const std::string& message);
}
extern "C" REXFABRIC_MOD_EXPORT void VivaApLog(int severity, const char* message);
