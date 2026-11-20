#pragma once
#include "../include/rexfabric/api.h"

namespace vivaap {
void InitializePretztailRestriction(const RexFabricApi* api, RexFabricMod* mod);
void ShutdownPretztailRestriction();
}

extern "C" {
REXFABRIC_MOD_EXPORT int32_t VivaApPretztailGetUserDataRoot(char* buffer, int32_t capacity);
REXFABRIC_MOD_EXPORT int VivaApPretztailPrepare();
REXFABRIC_MOD_EXPORT int VivaApPretztailSetRestricted(int restricted);
REXFABRIC_MOD_EXPORT uint32_t VivaApPretztailTakeDiagnostics();
}
