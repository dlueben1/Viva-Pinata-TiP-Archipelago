#pragma once
#include "include/rexfabric/api.h"
#include <cstdint>
#include <cstddef>
struct VivaApHookEvent {
    uint64_t hookId;
    uint64_t r3;
    uint64_t r4;
    uint64_t keyBefore; // u64 capture at call entry, when key capture is enabled
    uint64_t keyAfter;  // reserved, always 0: the argument record may be transient after the call
    uint32_t address;
    uint32_t before;
    uint32_t after;
    uint32_t flags; // bit 0: u32 pair captured, bit 1: u64 captured at entry, bit 2: text captured at entry
    char text[72]; // NUL-terminated C string read at call entry, when text capture is enabled
};
static_assert(sizeof(VivaApHookEvent) == 128);
static_assert(offsetof(VivaApHookEvent, hookId) == 0 && offsetof(VivaApHookEvent, r3) == 8 &&
    offsetof(VivaApHookEvent, r4) == 16 && offsetof(VivaApHookEvent, keyBefore) == 24 &&
    offsetof(VivaApHookEvent, keyAfter) == 32 && offsetof(VivaApHookEvent, address) == 40 &&
    offsetof(VivaApHookEvent, before) == 44 && offsetof(VivaApHookEvent, after) == 48 &&
    offsetof(VivaApHookEvent, flags) == 52 && offsetof(VivaApHookEvent, text) == 56);
namespace vivaap {
void InitializeHooks(const RexFabricApi* api, RexFabricMod* mod);
void ShutdownHooks();
}
extern "C" {
REXFABRIC_MOD_EXPORT int VivaApHookRegister(uint32_t address,
    uint32_t fieldEnabled, uint32_t fieldBaseRegister, uint32_t fieldOffset,
    uint32_t keyEnabled, uint32_t keyBaseRegister, uint32_t keyOffset,
    uint32_t textEnabled, uint32_t textBaseRegister, uint32_t textOffset, uint64_t* id);
REXFABRIC_MOD_EXPORT int VivaApHookUnregister(uint64_t id);
REXFABRIC_MOD_EXPORT int VivaApHookPoll(VivaApHookEvent* event);
REXFABRIC_MOD_EXPORT uint64_t VivaApHookTakeDropped();
}
