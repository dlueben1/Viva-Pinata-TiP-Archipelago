#pragma once
#include "../include/rexfabric/api.h"
#include <cstdint>

namespace vivaap {
    void InitializeItemStateHooks(const RexFabricApi* api, RexFabricMod* mod);
    void ShutdownItemStateHooks();
}

// C# calls these exports via DllImport("VivaAP.Bridge", CallingConvention=Cdecl).
// Register every AP-controlled ordinary house before Prepare (including houses not yet owned).
// Source identity fields come from the verified retail shopUnlockables rows.
extern "C" {
    REXFABRIC_MOD_EXPORT int32_t VivaApHouseRegister(uint32_t houseBlockTag,
        uint32_t sourceOrdinal, uint32_t residentSpeciesTag);
    // Register before Prepare. Metadata must identify an ordinary GeneralStore
    // source row with only the level condition (queue 1, mask 0x2).
    REXFABRIC_MOD_EXPORT int32_t VivaApLottieRegister(uint32_t tag,
        uint32_t sourceOrdinal, uint32_t itemClass, uint32_t levelThreshold);
    REXFABRIC_MOD_EXPORT int32_t VivaApHousePrepare();
    // Enable or disable early publication at a matching shop refresh. Defaults on.
    REXFABRIC_MOD_EXPORT int32_t VivaApHouseEnableEarlyGrants(int32_t enabled);
    REXFABRIC_MOD_EXPORT int32_t VivaApItemSetOwned(uint32_t tag, int32_t owned);
    REXFABRIC_MOD_EXPORT int32_t VivaApHouseSetOwned(uint32_t houseBlockTag, int32_t owned);
    REXFABRIC_MOD_EXPORT uint32_t VivaApHouseTakeDiagnostics();
}
