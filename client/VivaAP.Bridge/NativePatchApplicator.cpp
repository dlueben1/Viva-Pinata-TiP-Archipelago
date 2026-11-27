#include "pch.h"
#include "NativePatchApplicator.h"
#include <Windows.h>
#include <array>
#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstring>
#include <mutex>

namespace {
using Dispatcher = void(__cdecl*)(uint32_t, uint64_t);
struct Specification { uint32_t id, address; };
struct GuestCall { uint32_t address, count; uint64_t args[8], result; };
struct Slot {
    Specification spec{};
    RexFabricHookId hook{};
};
struct Invocation {
    Invocation* parent;
    uint64_t token;
    RexFabricCall* call;
    Slot* slot;
    uint32_t state{}; // 1 original started, 2 replacement, 4 side effects, 8 original completed
};
const RexFabricApi* api{};
RexFabricMod* mod{};
std::array<Slot, 64> slots;
size_t used{};
std::mutex registration, admission;
std::condition_variable drained;
Dispatcher dispatcher{};
bool enabled{}, stopping{};
uint32_t inFlight{};
std::atomic<uint64_t> nextToken{1};
thread_local Invocation* current{};

[[noreturn]] void ContractFailure() {
    // Continuing after partially executed guest policy has no generic safe rollback.
    OutputDebugStringA("VivaAP native patch contract violation; refusing unsafe original fallback.\n");
    RaiseFailFastException(nullptr, nullptr, 0);
    std::terminate();
}

const uint8_t* Map(uint32_t address, uint32_t size, bool write) {
    if (address < 0x10000 || !size || size > 65536 || size - 1 > UINT32_MAX - address) return nullptr;
    auto* first = static_cast<const uint8_t*>(api->guest_pointer(address));
    auto* last = static_cast<const uint8_t*>(api->guest_pointer(address + size - 1));
    if (!first || !last || reinterpret_cast<uintptr_t>(last) - reinterpret_cast<uintptr_t>(first) != size - 1) return nullptr;
    MEMORY_BASIC_INFORMATION region{};
    if (!VirtualQuery(first, &region, sizeof(region)) || region.State != MEM_COMMIT || (region.Protect & PAGE_GUARD)) return nullptr;
    const auto offset = reinterpret_cast<uintptr_t>(first) - reinterpret_cast<uintptr_t>(region.BaseAddress);
    if (offset >= region.RegionSize || size > region.RegionSize - offset) return nullptr;
    // Never permit executable memory modification through the managed patch API.
    switch (region.Protect & 0xff) {
    case PAGE_READWRITE: case PAGE_WRITECOPY: return first;
    case PAGE_READONLY: case PAGE_EXECUTE_READ: case PAGE_EXECUTE_READWRITE: case PAGE_EXECUTE_WRITECOPY:
        return write ? nullptr : first;
    default: return nullptr;
    }
}

void Run(Slot& slot, RexFabricCall* call, Dispatcher dispatch);
void Original(Invocation& invocation) {
    if (invocation.state & 3) ContractFailure();
    invocation.state |= 1; // Mark BEFORE entering guest code, including a failing call.
    api->call_next(invocation.call);
    invocation.state |= 8;
}
void Run(Slot& slot, RexFabricCall* call, Dispatcher dispatch) {
    Invocation invocation{current, nextToken.fetch_add(1, std::memory_order_relaxed), call, &slot};
    current = &invocation;
    try { dispatch(slot.spec.id, invocation.token); }
    catch (...) { ContractFailure(); } // Managed dispatcher must contain all managed exceptions.
    if (!(invocation.state & 3)) {
        if (invocation.state & 4) ContractFailure();
        Original(invocation); // Missing outcome never silently suppresses original execution.
    }
    current = invocation.parent;
}
void Hook(RexFabricCall* call, void* user) {
    Dispatcher dispatch;
    {
        std::lock_guard lock(admission);
        dispatch = enabled && !stopping ? dispatcher : nullptr;
        if (dispatch) ++inFlight;
    }
    if (!dispatch) { api->call_next(call); return; }
    Run(*static_cast<Slot*>(user), call, dispatch);
    {
        std::lock_guard lock(admission);
        if (--inFlight == 0) drained.notify_all();
    }
}
bool Available() {
    return api && mod && api->size >= offsetof(RexFabricApi, write_u32) + sizeof(api->write_u32) &&
        api->is_hookable && api->hook && api->unhook && api->call_next && api->get_gpr && api->set_gpr &&
        api->get_fpr && api->set_fpr && api->call_guest && api->guest_pointer;
}
}

namespace vivaap {
void InitializePatchApplicator(const RexFabricApi* value, RexFabricMod* owner) {
    api = value; mod = owner;
}
void ShutdownPatchApplicator() {
    if (current) ContractFailure(); // Shutdown cannot drain itself from inside a callback.
    std::unique_lock registrations(registration);
    {
        std::lock_guard lock(admission);
        enabled = false; stopping = true;
    }
    // Slots are process-lifetime storage. unhook need not drain a chain already copied by the runtime.
    for (size_t i = 0; i < used; ++i) {
        if (slots[i].hook && api->unhook(slots[i].hook)) slots[i].hook = 0;
    }
    registrations.unlock(); // No registration lock spans the callbacks being drained.
    {
        std::unique_lock lock(admission);
        drained.wait(lock, [] { return inFlight == 0; });
        dispatcher = nullptr;
    }
}
}

extern "C" REXFABRIC_MOD_EXPORT int VivaApPatchInstall(Dispatcher dispatch, const Specification* specs, uint32_t count) {
    std::lock_guard registrations(registration);
    if (!Available() || !dispatch || !specs || !count || count > slots.size() || used || stopping) return 1;
    for (uint32_t i = 0; i < count; ++i) {
        if (!specs[i].id || !api->is_hookable(specs[i].address)) return 2;
        for (uint32_t j = 0; j < i; ++j) if (specs[i].id == specs[j].id || specs[i].address == specs[j].address) return 3;
    }
    // All hooks stay inert until the complete registration set exists.
    for (uint32_t i = 0; i < count; ++i) {
        auto& slot = slots[used++];
        slot.spec = specs[i];
        slot.hook = api->hook(mod, specs[i].address, Hook, &slot);
        if (!slot.hook) {
            for (size_t j = 0; j < used; ++j) if (slots[j].hook && api->unhook(slots[j].hook)) slots[j].hook = 0;
            return 4;
        }
    }
    std::lock_guard lock(admission);
    dispatcher = dispatch; enabled = true;
    return 0;
}
extern "C" REXFABRIC_MOD_EXPORT void VivaApPatchStop() { vivaap::ShutdownPatchApplicator(); }
extern "C" REXFABRIC_MOD_EXPORT int VivaApPatchIsHookable(uint32_t address) {
    return api && api->size >= offsetof(RexFabricApi, is_hookable) + sizeof(api->is_hookable) &&
        api->is_hookable && api->is_hookable(address);
}

// The handle is a monotonically unique token, never a host pointer. Only the innermost invocation
// on THIS native thread can use it; stale, outer-during-nesting, and foreign-thread calls fail.
extern "C" REXFABRIC_MOD_EXPORT int VivaApPatchAccess(uint64_t token, uint32_t operation, uint32_t argument, void* data, uint32_t size) {
    auto* p = current;
    if (!p || p->token != token) return 1;
    if (operation == 7) { if (p->state & 3) return 2; Original(*p); return 0; }
    if (operation == 8) { if (p->state & 3) return 2; p->state |= 2; return 0; }
    if (!data) return 3;
    if (operation == 10 && size == 4) { *static_cast<uint32_t*>(data) = p->state; return 0; }
    if (operation >= 1 && operation <= 4) {
        if (argument > 31 || size != 8) return 3;
        auto& bits = *static_cast<uint64_t*>(data);
        auto& real = *static_cast<double*>(data);
        if (operation == 1) bits = api->get_gpr(p->call, argument);
        if (operation == 2) { p->state |= 4; api->set_gpr(p->call, argument, bits); }
        if (operation == 3) real = api->get_fpr(p->call, argument);
        if (operation == 4) { p->state |= 4; api->set_fpr(p->call, argument, real); }
        return 0;
    }
    if (operation == 5 || operation == 6) {
        const auto* memory = Map(argument, size, operation == 6);
        if (!memory) return 4;
        if (operation == 5) std::memcpy(data, memory, size);
        else { p->state |= 4; std::memcpy(const_cast<uint8_t*>(memory), data, size); }
        return 0;
    }
    if (operation == 9 && size == sizeof(GuestCall)) {
        auto& request = *static_cast<GuestCall*>(data);
        if (request.count > 8 || !api->is_hookable(request.address)) return 3;
        p->state |= 4; // A guest call may already have changed state even if it never returns.
        request.result = api->call_guest(p->call, request.address, request.args, request.count);
        return 0;
    }
    return 3;
}
