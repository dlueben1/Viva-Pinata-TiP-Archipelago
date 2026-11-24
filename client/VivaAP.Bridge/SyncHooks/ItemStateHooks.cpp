#include "pch.h"
#include "ItemStateHooks.h"

#include <Windows.h>
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <intrin.h>
#include <mutex>

namespace {
// RETAIL guest addresses. Do not use with another recomp/runtime until revalidated.
constexpr uint32_t kReelBuilder       = 0x823652A8;
constexpr uint32_t kPlacementValidate = 0x822D6E00;
constexpr uint32_t kShopRefresh       = 0x825B3688;
constexpr uint32_t kPublisher         = 0x825B1990;
constexpr uint32_t kHasMembership     = 0x825B2508;
constexpr uint32_t kTopGarden         = 0x824E1158;
constexpr uint32_t kWillyCallback     = 0x824027A8;
constexpr uint32_t kLocalPlayer       = 0x83DBED50;
constexpr uint32_t kModeFlags         = 0x83DBED7C;
constexpr uint32_t kLiveSlots[]       = {0x82C4A4EC, 0x82C4A4F0, 0x82C4A4F4};

constexpr uint32_t kWilly = 4, kHouseClass = 0x13;
constexpr uint32_t kHouseRowSize = 0x24, kPointerRowSize = 4;
constexpr uint32_t kMaxRows = 1024, kMaxBlueprints = 160;
constexpr uint32_t kQ5Mask = 0x20;

enum Diagnostic : uint32_t {
    SoldOutApplied = 1, PlacementDenied = 2, GrantConfirmed = 4,
    GrantDeferred = 8, ReelUnqualified = 16, PlacementUnqualified = 32,
    GrantAttemptFailed = 64
};

struct Entry {
    uint32_t tag = 0;
    uint32_t ordinal = 0;
    uint32_t species = 0;
    std::atomic<bool> owned{false};
    // A destructive publication must not be repeated if it failed midway.
    std::atomic<uint32_t> attemptedManager{0};
};

const RexFabricApi* api = nullptr;
RexFabricMod* mod = nullptr;
RexFabricHookId reelHook = 0, placementHook = 0, refreshHook = 0;
std::array<Entry, kMaxBlueprints> entries;
uint32_t entryCount = 0;            // Immutable once hooks are installed.
std::mutex registrationLock;
std::atomic<bool> active{false};
// Mutating early publication must be enabled explicitly after a controlled test.
std::atomic<bool> earlyGrantsEnabled{true};
std::atomic<uint32_t> diagnostics{0};
thread_local bool granting = false;

uint32_t GuestAddress(uint64_t value) {
    return value >= 0x10000 && value <= UINT32_MAX ? static_cast<uint32_t>(value) : 0;
}

const uint8_t* Map(uint32_t address, uint32_t size, bool write = false) {
    if (address < 0x10000 || (address & 3) || !size || size - 1 > UINT32_MAX - address)
        return nullptr;
    const auto* begin = static_cast<const uint8_t*>(api->guest_pointer(address));
    const auto* last = static_cast<const uint8_t*>(api->guest_pointer(address + size - 1));
    if (!begin || !last || reinterpret_cast<uintptr_t>(last) - reinterpret_cast<uintptr_t>(begin) != size - 1)
        return nullptr;
    MEMORY_BASIC_INFORMATION region{};
    if (!VirtualQuery(begin, &region, sizeof(region)) || region.State != MEM_COMMIT ||
        (region.Protect & PAGE_GUARD))
        return nullptr;
    const auto offset = reinterpret_cast<uintptr_t>(begin) - reinterpret_cast<uintptr_t>(region.BaseAddress);
    if (offset >= region.RegionSize || size > region.RegionSize - offset)
        return nullptr;
    switch (region.Protect & 0xff) {
        case PAGE_READWRITE: case PAGE_WRITECOPY:
        case PAGE_EXECUTE_READWRITE: case PAGE_EXECUTE_WRITECOPY: return begin;
        case PAGE_READONLY: case PAGE_EXECUTE_READ: return write ? nullptr : begin;
        default: return nullptr;
    }
}

uint32_t Word(const uint8_t* data, uint32_t offset = 0) {
    uint32_t value;
    std::memcpy(&value, data + offset, sizeof(value));
    return _byteswap_ulong(value);
}
uint16_t Half(const uint8_t* data) {
    return static_cast<uint16_t>((data[0] << 8) | data[1]);
}
bool Read(uint32_t base, uint32_t offset, uint32_t& value) {
    const auto* data = base >= 0x10000 && offset <= UINT32_MAX - base
        ? Map(base + offset, 4) : nullptr;
    if (!data) return false;
    value = Word(data);
    return true;
}

struct List { uint32_t begin{}, end{}; const uint8_t* rows{}; };
bool ReadList(uint32_t address, uint32_t stride, List& list) {
    const auto* header = Map(address, 0x14);
    if (!header || Half(header + 0xc) != stride) return false;
    list.begin = Word(header);
    list.end = Word(header, 4);
    const uint32_t count = Half(header + 0x10);
    if (count > kMaxRows || list.end < list.begin || list.end - list.begin != count * stride)
        return false;
    list.rows = count ? Map(list.begin, count * stride) : nullptr;
    return count == 0 || list.rows != nullptr;
}
bool Disjoint(const List& a, const List& b) { return a.end <= b.begin || b.end <= a.begin; }

Entry* FindEntry(uint32_t tag) {
    for (uint32_t i = 0; i < entryCount; ++i)
        if (entries[i].tag == tag) return &entries[i];
    return nullptr;
}

bool OrdinarySinglePlayer() {
    uint32_t local = 0;
    const auto* modes = Map(kModeFlags, 8);
    if (!modes || Word(modes) || Word(modes, 4) ||
        !Read(kLocalPlayer, 0, local) || !local) return false;
    for (uint32_t slot : kLiveSlots) {
        uint32_t session = 0, multiplayer = 0;
        if (!Read(slot, 0, session) ||
            (session && (!Read(session, 0x1ec, multiplayer) || multiplayer))) return false;
    }
    return true;
}

bool ReadPlayerManager(uint32_t& manager, uint32_t& local) {
    uint32_t state = 0;
    manager = local = 0;
    return OrdinarySinglePlayer() && Read(kLocalPlayer, 0, local) && local &&
        Read(local, 0x1e70, state) && state &&
        Read(state, 0x2d4, manager) && manager >= 0x10000;
}

// The shop/reel/catalog are separate 0x24-byte lists. Do not edit canonical records.
bool FindHouse(const List& list, const Entry& entry, uint32_t& match) {
    match = 0;
    for (uint32_t row = list.begin; row < list.end; row += kHouseRowSize) {
        const auto* data = list.rows + (row - list.begin);
        if (Word(data) != entry.tag) continue;
        if (match || Word(data, 4) != kHouseClass || Word(data, 8) != 0 ||
            Word(data, 0x10) != entry.ordinal || Word(data, 0x14) != 0)
            return false;
        match = row;
    }
    return true;
}

struct Shop { uint32_t buyer{}, catalogAddress{}, workspace{}; List catalog, canonical; };

bool QualifyShop(RexFabricCall* call, uint32_t controller, Shop& shop) {
    uint32_t shopId = 0, local = 0, state = 0, manager = 0, component = 0;
    if (!OrdinarySinglePlayer() || !Read(controller, 0x348, shopId) || shopId != kWilly ||
        !Read(controller, 0xa8, shop.buyer) || !Read(kLocalPlayer, 0, local) ||
        !local || shop.buyer != local) return false;

    const uint32_t garden = GuestAddress(api->call_guest(call, kTopGarden, nullptr, 0));
    constexpr uint32_t willyCatalogOffset = 0x4c0c + kWilly * 0x28;
    if (!Read(garden, 0x4a00, shop.workspace) ||
        !Read(shop.workspace, 0x8e4, component) || !component ||
        component > UINT32_MAX - willyCatalogOffset ||
        !Read(controller, 0x358, shop.catalogAddress) ||
        shop.catalogAddress != component + willyCatalogOffset ||
        !Read(local, 0x1e70, state) || !Read(state, 0x2d4, manager)) return false;

    return ReadList(shop.catalogAddress, kHouseRowSize, shop.catalog) &&
        ReadList(manager + 8, kHouseRowSize, shop.canonical) &&
        Disjoint(shop.catalog, shop.canonical);
}

// Called AFTER the original reel builder. The shop's ordinary selection path
// reads row+0x18 and renders native Sold Out when it is zero.
void OnReelBuilt(RexFabricCall* call, void*) {
    const bool inspect = active.load(std::memory_order_acquire) &&
        api->get_gpr(call, 4) == kHouseClass;
    const uint32_t avatar = inspect ? GuestAddress(api->get_gpr(call, 3)) : 0;
    api->call_next(call);
    if (!avatar || !active.load(std::memory_order_acquire)) return;

    uint32_t callback = 0, controller = 0, shopId = 0, backLink = 0, avatarCatalog = 0;
    if (!Read(avatar, 0x1728, callback) || callback != kWillyCallback ||
        !Read(avatar, 0x172c, controller) || !Read(controller, 0x348, shopId) ||
        shopId != kWilly) return;

    Shop shop;
    List reel;
    if (!Read(controller, 0x1e4, backLink) || backLink != avatar ||
        !Read(avatar, 0xecc, avatarCatalog) ||
        !QualifyShop(call, controller, shop) ||
        avatarCatalog != shop.catalogAddress ||
        avatar > UINT32_MAX - 0xee4 ||
        !ReadList(avatar + 0xee4, kHouseRowSize, reel) ||
        !Disjoint(reel, shop.catalog) || !Disjoint(reel, shop.canonical)) {
        diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
        return;
    }

    // No managed callback on the game thread: a C# worker publishes owned bits.
    for (uint32_t i = 0; i < entryCount; ++i) {
        auto& entry = entries[i];
        if (entry.owned.load(std::memory_order_acquire)) continue;

        uint32_t reelRow = 0, catalogRow = 0, canonicalRow = 0;
        if (!FindHouse(reel, entry, reelRow) ||
            !FindHouse(shop.catalog, entry, catalogRow) ||
            !FindHouse(shop.canonical, entry, canonicalRow)) {
            diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
            continue;
        }
        // If vanilla has not unlocked the house, it is absent from the reel.
        if (!reelRow) continue;
        const auto* data = reel.rows + (reelRow - reel.begin);
        if (!catalogRow || !canonicalRow || Word(data, 0x20) != catalogRow ||
            !Map(reelRow + 0x18, 4, true)) {
            diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
            continue;
        }
        if (!entry.owned.load(std::memory_order_acquire) && Word(data, 0x18) != 0) {
            api->write_u32(reelRow + 0x18, 0);
            diagnostics.fetch_or(SoldOutApplied, std::memory_order_relaxed);
        }
    }
}

bool ShouldDenyPlacement(RexFabricCall* call) {
    const uint32_t preview = GuestAddress(api->get_gpr(call, 4));
    uint32_t tag = 0, itemClass = 0;
    if (!Read(preview, 0x84, tag) || !Read(preview, 0x88, itemClass) ||
        itemClass != kHouseClass) return false;
    Entry* entry = FindEntry(tag);
    if (!entry || entry->owned.load(std::memory_order_acquire)) return false;

    const uint32_t control = GuestAddress(api->get_gpr(call, 3));
    uint32_t mode = 0, tagInCursor = 0, buyer = 0, controller = 0;
    uint32_t cursor = 0, localCursor = 0, cursorControl = 0;
    uint32_t catalogRow = 0, canonicalRow = 0;
    Shop shop;
    const bool qualified = Read(control, 0, mode) && mode == 9 &&
        Read(control, 0x354, tagInCursor) && tagInCursor == tag &&
        Read(control, 0x130, buyer) && Read(control, 0x134, controller) &&
        QualifyShop(call, controller, shop) && buyer == shop.buyer &&
        Read(control, 8, cursor) && Read(shop.workspace, 0x8fc, localCursor) &&
        cursor == localCursor && Read(cursor, 0xe0, cursorControl) &&
        cursorControl == control && FindHouse(shop.catalog, *entry, catalogRow) &&
        catalogRow && FindHouse(shop.canonical, *entry, canonicalRow) && canonicalRow;
    if (!qualified) diagnostics.fetch_or(PlacementUnqualified, std::memory_order_relaxed);
    return qualified;
}

void OnPlacement(RexFabricCall* call, void*) {
    if (active.load(std::memory_order_acquire) && ShouldDenyPlacement(call)) {
        // Validator returns false before clone, coin debit, or house construction.
        api->set_gpr(call, 3, 0);
        diagnostics.fetch_or(PlacementDenied, std::memory_order_relaxed);
        return;
    }
    api->call_next(call);
}

// Validate a live, single q5 pending registration before side-effect publication.
// This intentionally skips unusual/multi-queue house rows, rather than guessing.
bool FindQ5PendingRow(uint32_t manager, const Entry& entry, uint32_t& mask) {
    uint32_t queues = 0, queue5 = 0, type = 0;
    if (!Read(manager, 0x1c, queues) ||
        !Read(queues, 4 * 4, queue5) || !Read(queue5, 0, type) || type != 5)
        return false;
    List pending;
    if (!ReadList(queue5 + 4, kPointerRowSize, pending)) return false;
    bool found = false;
    for (uint32_t p = pending.begin; p < pending.end; p += 4) {
        const uint32_t row = Word(pending.rows + (p - pending.begin));
        uint32_t tag = 0, ordinal = 0, species = 0, currentMask = 0;
        if (!Read(row, 4, tag) || !Read(row, 0x28, ordinal) ||
            !Read(row, 0x18, species) || !Read(row, 0x2c, currentMask))
            return false;
        if (tag != entry.tag) continue;
        if (found || ordinal != entry.ordinal || species != entry.species ||
            currentMask != kQ5Mask) return false;
        mask = currentMask;
        found = true;
    }
    return found;
}

// Called BEFORE vanilla shop refresh so newly published houses can appear in
// its normal derived catalog. The only guest-thread mutation is call_guest.
void OnShopRefresh(RexFabricCall* call, void*) {
    if (active.load(std::memory_order_acquire) &&
        earlyGrantsEnabled.load(std::memory_order_acquire) && !granting &&
        api->get_gpr(call, 4) == kWilly) {
        uint32_t manager = 0, local = 0;
        if (ReadPlayerManager(manager, local)) {
            granting = true;
            for (uint32_t i = 0; i < entryCount; ++i) {
                auto& entry = entries[i];
                if (!entry.owned.load(std::memory_order_acquire) ||
                    entry.attemptedManager.load(std::memory_order_acquire) == manager)
                    continue;

                const uint64_t membershipArgs[2] = {manager, entry.tag};
                if (api->call_guest(call, kHasMembership, membershipArgs, 2) != 0)
                    continue;

                uint32_t liveMask = 0;
                if (!FindQ5PendingRow(manager, entry, liveMask)) {
                    diagnostics.fetch_or(GrantDeferred, std::memory_order_relaxed);
                    continue;
                }

                // Mark BEFORE mutating: a failed publisher must not be blindly retried.
                entry.attemptedManager.store(manager, std::memory_order_release);
                const uint64_t grantArgs[4] = {manager, entry.tag, liveMask, entry.ordinal};
                api->call_guest(call, kPublisher, grantArgs, 4);
                if (api->call_guest(call, kHasMembership, membershipArgs, 2) != 0)
                    diagnostics.fetch_or(GrantConfirmed, std::memory_order_relaxed);
                else
                    diagnostics.fetch_or(GrantAttemptFailed, std::memory_order_relaxed);
            }
            granting = false;
        }
    }
    api->call_next(call);
}
} // anonymous namespace

namespace vivaap {
void InitializeItemStateHooks(const RexFabricApi* fabric, RexFabricMod* owner) {
    api = fabric;
    mod = owner;
}
void ShutdownItemStateHooks() {
    earlyGrantsEnabled.store(false, std::memory_order_release);
    active.store(false, std::memory_order_release);
    if (refreshHook) api->unhook(refreshHook);
    if (placementHook) api->unhook(placementHook);
    if (reelHook) api->unhook(reelHook);
    refreshHook = placementHook = reelHook = 0;
}
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApHouseRegister(
    uint32_t tag, uint32_t ordinal, uint32_t species) {
    std::lock_guard lock(registrationLock);
    if (active.load() || !api || !tag || !species || ordinal >= 629) return 0;
    if (FindEntry(tag) || entryCount == kMaxBlueprints) return 0;
    auto& entry = entries[entryCount++];
    entry.tag = tag;
    entry.ordinal = ordinal;
    entry.species = species;
    entry.owned.store(false);
    entry.attemptedManager.store(0);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApHousePrepare() {
    std::lock_guard lock(registrationLock);
    if (active.load()) return 1;
    if (!entryCount || !api || !mod ||
        api->size < offsetof(RexFabricApi, get_cvar) + sizeof(api->get_cvar) ||
        !api->is_hookable || !api->hook || !api->unhook || !api->call_next ||
        !api->get_gpr || !api->set_gpr || !api->call_guest || !api->guest_pointer ||
        !api->read_u32 || !api->write_u32) return 0;
    if (!api->is_hookable(kReelBuilder) ||
        !api->is_hookable(kPlacementValidate) ||
        !api->is_hookable(kShopRefresh) ||
        !api->is_hookable(kPublisher) ||
        !api->is_hookable(kHasMembership) || !api->is_hookable(kTopGarden)) return 0;

    reelHook = api->hook(mod, kReelBuilder, OnReelBuilt, nullptr);
    if (!reelHook) return 0;
    placementHook = api->hook(mod, kPlacementValidate, OnPlacement, nullptr);
    if (!placementHook) {
        vivaap::ShutdownItemStateHooks();
        return 0;
    }
    refreshHook = api->hook(mod, kShopRefresh, OnShopRefresh, nullptr);
    if (!refreshHook) {
        vivaap::ShutdownItemStateHooks();
        return 0;
    }
    active.store(true, std::memory_order_release);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApHouseEnableEarlyGrants(int32_t enabled) {
    if (enabled != 0 && enabled != 1) return 0;
    earlyGrantsEnabled.store(enabled == 1, std::memory_order_release);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApHouseSetOwned(uint32_t tag, int32_t owned) {
    if (owned != 0 && owned != 1) return 0;
    Entry* entry = FindEntry(tag);
    if (!entry) return 0;
    entry->owned.store(owned == 1, std::memory_order_release);
    return 1;
}
extern "C" REXFABRIC_MOD_EXPORT uint32_t VivaApHouseTakeDiagnostics() {
    return diagnostics.exchange(0, std::memory_order_relaxed);
}
