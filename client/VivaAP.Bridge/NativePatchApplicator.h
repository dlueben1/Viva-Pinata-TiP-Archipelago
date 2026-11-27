#pragma once
#include "include/rexfabric/api.h"

namespace vivaap {
void InitializePatchApplicator(const RexFabricApi* api, RexFabricMod* mod);
// Close admission, remove hooks, and drain callbacks BEFORE managed shutdown.
void ShutdownPatchApplicator();
}
