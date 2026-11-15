#pragma once
#include "include/rexfabric/api.h"
#include <cstdint>
#include <cstddef>
struct VivaApHookEvent {
    uint64_t hookId;
    uint64_t r3;
    uint64_t r4;
    uint32_t address;
    uint32_t before;
    uint32_t after;
    uint32_t flags; // bit 0: both field samples captured
};
static_assert(sizeof(VivaApHookEvent) == 40);
static_assert(offsetof(VivaApHookEvent, hookId) == 0 && offsetof(VivaApHookEvent, r3) == 8 &&
    offsetof(VivaApHookEvent, r4) == 16 && offsetof(VivaApHookEvent, address) == 24 &&
    offsetof(VivaApHookEvent, before) == 28 && offsetof(VivaApHookEvent, after) == 32 &&
    offsetof(VivaApHookEvent, flags) == 36);
namespace vivaap {
void InitializeHooks(const RexFabricApi* api, RexFabricMod* mod);
void ShutdownHooks();
}
extern "C" {
REXFABRIC_MOD_EXPORT int VivaApHookRegister(uint32_t address, uint32_t fieldEnabled, uint32_t baseRegister, uint32_t offset, uint64_t* id);
REXFABRIC_MOD_EXPORT int VivaApHookUnregister(uint64_t id);
REXFABRIC_MOD_EXPORT int VivaApHookPoll(VivaApHookEvent* event);
REXFABRIC_MOD_EXPORT uint64_t VivaApHookTakeDropped();
}
