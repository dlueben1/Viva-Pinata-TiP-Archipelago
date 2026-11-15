#include "pch.h"
#include "GameHooks.h"
#include <array>
#include <atomic>
#include <mutex>
#include <cstddef>
#include <limits>

namespace {
constexpr size_t kMaxHooks = 32, kQueueSize = 256;
struct Slot {
    uint64_t id = 0;
    RexFabricHookId nativeId = 0;
    uint32_t address = 0, fieldEnabled = 0, baseRegister = 0, offset = 0;
    std::atomic<bool> active{false};
};
const RexFabricApi* g_api = nullptr;
RexFabricMod* g_mod = nullptr;
std::array<Slot, kMaxHooks> g_slots;
size_t g_used = 0;
std::mutex g_registrationMutex, g_queueMutex;
std::array<VivaApHookEvent, kQueueSize> g_queue;
size_t g_head = 0, g_count = 0;
std::atomic<uint64_t> g_dropped{0};
std::atomic<bool> g_stopping{false};

void Hook(RexFabricCall* call, void* user) {
    auto& slot = *static_cast<Slot*>(user);
    VivaApHookEvent event{};
    event.hookId = slot.id;
    event.address = slot.address;
    event.r3 = g_api->get_gpr(call, 3);
    event.r4 = g_api->get_gpr(call, 4);
    uint32_t fieldAddress = 0;
    bool sample = slot.fieldEnabled != 0 && slot.active.load(std::memory_order_acquire);
    if (sample) {
        const uint64_t base = g_api->get_gpr(call, slot.baseRegister);
        // Guest pointers are 32-bit; reject null and address overflow.
        sample = base != 0 && base <= UINT32_MAX && slot.offset <= UINT32_MAX - base && base + slot.offset <= UINT32_MAX - 3;
        if (sample) {
            fieldAddress = static_cast<uint32_t>(base + slot.offset);
            event.before = g_api->read_u32(fieldAddress);
        }
    }
    g_api->call_next(call);
    if (sample && slot.active.load(std::memory_order_acquire)) {
        event.after = g_api->read_u32(fieldAddress);
        event.flags = 1;
    }
    if (!slot.active.load(std::memory_order_acquire) || g_stopping.load(std::memory_order_acquire)) return;
    if (!g_queueMutex.try_lock()) { ++g_dropped; return; }
    if (g_count == kQueueSize) ++g_dropped;
    else { g_queue[(g_head + g_count) % kQueueSize] = event; ++g_count; }
    g_queueMutex.unlock();
}
}
namespace vivaap {
void InitializeHooks(const RexFabricApi* api, RexFabricMod* mod) { g_api = api; g_mod = mod; }
void ShutdownHooks() {
    std::lock_guard lock(g_registrationMutex);
    g_stopping.store(true, std::memory_order_release);
    for (size_t i = 0; i < g_used; ++i) {
        auto& slot = g_slots[i];
        if (slot.active.exchange(false) && slot.nativeId) g_api->unhook(slot.nativeId);
    }
}
}
extern "C" REXFABRIC_MOD_EXPORT int VivaApHookRegister(uint32_t address, uint32_t fieldEnabled, uint32_t baseRegister, uint32_t offset, uint64_t* id) {
    if (!id) return 1;
    *id = 0;
    if (!address || fieldEnabled > 1 || (fieldEnabled && baseRegister > 31)) return 1;
    std::lock_guard lock(g_registrationMutex);
    if (g_stopping || !g_api || !g_mod || g_api->size < offsetof(RexFabricApi, read_u32) + sizeof(g_api->read_u32) ||
        !g_api->is_hookable || !g_api->hook || !g_api->unhook || !g_api->get_gpr || !g_api->call_next || !g_api->read_u32) return 2;
    if (!g_api->is_hookable(address)) return 3;
    if (g_used == kMaxHooks) return 4;
    auto& slot = g_slots[g_used++]; // Never reuse: an unhooked callback may still be in flight.
    slot.id = g_used; slot.address = address; slot.fieldEnabled = fieldEnabled; slot.baseRegister = baseRegister; slot.offset = offset;
    slot.active.store(true, std::memory_order_release);
    slot.nativeId = g_api->hook(g_mod, address, Hook, &slot);
    if (!slot.nativeId) { slot.active.store(false); return 5; }
    *id = slot.id;
    return 0;
}
extern "C" REXFABRIC_MOD_EXPORT int VivaApHookUnregister(uint64_t id) {
    std::lock_guard lock(g_registrationMutex);
    if (!id || id > g_used) return 0;
    auto& slot = g_slots[static_cast<size_t>(id - 1)];
    if (!slot.active.exchange(false)) return 0;
    return g_api->unhook(slot.nativeId);
}
extern "C" REXFABRIC_MOD_EXPORT int VivaApHookPoll(VivaApHookEvent* event) {
    if (!event) return 0;
    std::lock_guard lock(g_queueMutex);
    if (!g_count) return 0;
    *event = g_queue[g_head]; g_head = (g_head + 1) % kQueueSize; --g_count; return 1;
}
extern "C" REXFABRIC_MOD_EXPORT uint64_t VivaApHookTakeDropped() { return g_dropped.exchange(0); }
