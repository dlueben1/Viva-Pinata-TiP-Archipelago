#include "pch.h"
#include "GameHooks.h"
#include <array>
#include <atomic>
#include <mutex>
#include <cstddef>
#include <limits>

namespace {
constexpr size_t kMaxHooks = 32, kQueueSize = 256, kTextCap = 71;
struct Slot {
    uint64_t id = 0;
    RexFabricHookId nativeId = 0;
    uint32_t address = 0;
    uint32_t fieldEnabled = 0, fieldBaseRegister = 0, fieldOffset = 0;
    uint32_t keyEnabled = 0, keyBaseRegister = 0, keyOffset = 0;
    uint32_t textEnabled = 0, textBaseRegister = 0, textOffset = 0;
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

// Guest pointers are 32-bit; reject null and ranges that wrap past the guest address space.
bool FitsGuestRange(uint64_t base, uint32_t offset, uint32_t width) {
    return base != 0 && base <= UINT32_MAX && offset <= UINT32_MAX - (width - 1) &&
        base + offset <= UINT32_MAX - (width - 1);
}

void Hook(RexFabricCall* call, void* user) {
    auto& slot = *static_cast<Slot*>(user);
    VivaApHookEvent event{};
    event.hookId = slot.id;
    event.address = slot.address;
    event.r3 = g_api->get_gpr(call, 3);
    event.r4 = g_api->get_gpr(call, 4);
    const bool active = slot.active.load(std::memory_order_acquire);
    // u32 watch sampled before and after the original call.
    uint32_t fieldAddress = 0;
    bool fieldOk = false;
    if (active && slot.fieldEnabled) {
        const uint64_t base = g_api->get_gpr(call, slot.fieldBaseRegister);
        fieldOk = FitsGuestRange(base, slot.fieldOffset, 4);
        if (fieldOk) {
            fieldAddress = static_cast<uint32_t>(base + slot.fieldOffset);
            event.before = g_api->read_u32(fieldAddress);
        }
    }
    // u64 identity snapshot at entry and exit.
    uint32_t keyAddress = 0;
    bool keyOk = false;
    if (active && slot.keyEnabled) {
        const uint64_t base = g_api->get_gpr(call, slot.keyBaseRegister);
        keyOk = FitsGuestRange(base, slot.keyOffset, 8);
        if (keyOk) {
            keyAddress = static_cast<uint32_t>(base + slot.keyOffset);
            event.keyBefore = g_api->read_u64(keyAddress);
        }
    }
    // C string captured at entry only: caller-owned buffers (e.g. the award record embedded at
    // msg+0x48 on the message grant route) may be freed by the time the original call returns.
    if (active && slot.textEnabled) {
        const uint64_t base = g_api->get_gpr(call, slot.textBaseRegister);
        if (FitsGuestRange(base, slot.textOffset, kTextCap)) {
            const uint32_t textAddress = static_cast<uint32_t>(base + slot.textOffset);
            size_t i = 0;
            for (; i < kTextCap; ++i) {
                const char c = static_cast<char>(g_api->read_u8(textAddress + static_cast<uint32_t>(i)));
                if (!c) break;
                event.text[i] = c;
            }
            event.text[i] = '\0';
            event.flags |= 4;
        }
    }
    g_api->call_next(call);
    if (fieldOk && slot.active.load(std::memory_order_acquire)) {
        event.after = g_api->read_u32(fieldAddress);
        event.flags |= 1;
    }
    if (keyOk && slot.active.load(std::memory_order_acquire)) {
        event.keyAfter = g_api->read_u64(keyAddress);
        event.flags |= 2;
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
extern "C" REXFABRIC_MOD_EXPORT int VivaApHookRegister(uint32_t address,
    uint32_t fieldEnabled, uint32_t fieldBaseRegister, uint32_t fieldOffset,
    uint32_t keyEnabled, uint32_t keyBaseRegister, uint32_t keyOffset,
    uint32_t textEnabled, uint32_t textBaseRegister, uint32_t textOffset, uint64_t* id) {
    if (!id) return 1;
    *id = 0;
    if (!address || fieldEnabled > 1 || keyEnabled > 1 || textEnabled > 1) return 1;
    if ((fieldEnabled && fieldBaseRegister > 31) || (keyEnabled && keyBaseRegister > 31) ||
        (textEnabled && textBaseRegister > 31)) return 1;
    std::lock_guard lock(g_registrationMutex);
    if (g_stopping || !g_api || !g_mod || g_api->size < offsetof(RexFabricApi, read_u64) + sizeof(g_api->read_u64) ||
        !g_api->is_hookable || !g_api->hook || !g_api->unhook || !g_api->get_gpr || !g_api->call_next ||
        !g_api->read_u8 || !g_api->read_u32 || !g_api->read_u64) return 2;
    if (!g_api->is_hookable(address)) return 3;
    if (g_used == kMaxHooks) return 4;
    auto& slot = g_slots[g_used++]; // Never reuse: an unhooked callback may still be in flight.
    slot.id = g_used; slot.address = address;
    slot.fieldEnabled = fieldEnabled; slot.fieldBaseRegister = fieldBaseRegister; slot.fieldOffset = fieldOffset;
    slot.keyEnabled = keyEnabled; slot.keyBaseRegister = keyBaseRegister; slot.keyOffset = keyOffset;
    slot.textEnabled = textEnabled; slot.textBaseRegister = textBaseRegister; slot.textOffset = textOffset;
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
