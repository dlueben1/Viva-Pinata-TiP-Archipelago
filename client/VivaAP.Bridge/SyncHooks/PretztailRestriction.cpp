#include "pch.h"
#include "PretztailRestriction.h"
#include <atomic>
#include <cstddef>
#include <cstring>
#include <intrin.h>

namespace {
// TODO: Investigate migrating this hook to C# if it can be simplified without synchronous managed callbacks.
constexpr uint32_t kReelBuilder = 0x823652A8, kPlacementValidator = 0x822D6E00;
constexpr uint32_t kTopGarden = 0x824E1158, kWillyCallback = 0x824027A8;
constexpr uint32_t kLocalPlayer = 0x83DBED50, kModeFlags = 0x83DBED7C;
constexpr uint32_t kLiveSlots[] = {0x82C4A4EC, 0x82C4A4F0, 0x82C4A4F4};
constexpr uint32_t kTag = 1493, kOrdinal = 339, kClass = 0x13, kWilly = 4;
constexpr uint32_t kRowSize = 0x24, kAvailability = 0x18, kMaxRows = 1024;
enum Diagnostic : uint32_t { ReelDisabled = 1, PlacementDenied = 2, ReelRejected = 4, PlacementRejected = 8 };

const RexFabricApi* api = nullptr;
RexFabricMod* mod = nullptr;
RexFabricHookId reelHook = 0, placementHook = 0;
// Only managed startup/shutdown changes the hook registrations. After startup the
// managed worker publishes the state independently of the borrowed game-thread calls.
std::atomic<bool> ready{false}, restricted{false};
std::atomic<uint32_t> diagnostics{0};

bool IsRestricted() {
    return ready.load(std::memory_order_acquire) && restricted.load(std::memory_order_acquire);
}

uint32_t GuestAddress(uint64_t value) {
    return value <= UINT32_MAX ? static_cast<uint32_t>(value) : 0;
}

// RexFabric translates the guest address. An arena mapping by itself does not mean
// a range is committed or writable. Validate the entire span before using it.
const uint8_t* Map(uint32_t address, uint32_t size, bool write = false) {
    if (address < 0x10000 || (address & 3) != 0 || size == 0 || size - 1 > UINT32_MAX - address) return nullptr;
    const auto* begin = static_cast<const uint8_t*>(api->guest_pointer(address));
    const auto* last = static_cast<const uint8_t*>(api->guest_pointer(address + size - 1));
    const auto base = reinterpret_cast<uintptr_t>(begin);
    if (!begin || !last || reinterpret_cast<uintptr_t>(last) - base != size - 1) return nullptr;

    MEMORY_BASIC_INFORMATION region{};
    if (!VirtualQuery(begin, &region, sizeof(region)) || region.State != MEM_COMMIT ||
        (region.Protect & PAGE_GUARD)) return nullptr;
    const auto offset = base - reinterpret_cast<uintptr_t>(region.BaseAddress);
    if (offset >= region.RegionSize || size > region.RegionSize - offset) return nullptr;

    switch (region.Protect & 0xff) {
    case PAGE_READWRITE: case PAGE_WRITECOPY: case PAGE_EXECUTE_READWRITE: case PAGE_EXECUTE_WRITECOPY:
        return begin;
    case PAGE_READONLY: case PAGE_EXECUTE_READ:
        return write ? nullptr : begin;
    default:
        return nullptr;
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
    const auto* data = base >= 0x10000 && offset <= UINT32_MAX - base ? Map(base + offset, 4) : nullptr;
    if (!data) return false;
    value = Word(data);
    return true;
}

struct List {
    uint32_t begin{}, end{};
    const uint8_t* rows{};
};

bool ReadList(uint32_t address, List& list) {
    const auto* data = Map(address, 0x14);
    if (!data || Half(data + 0xc) != kRowSize) return false;
    list.begin = Word(data);
    list.end = Word(data, 4);
    const uint32_t count = Half(data + 0x10);
    if (count > kMaxRows || list.end < list.begin || list.end - list.begin != count * kRowSize) return false;
    list.rows = count ? Map(list.begin, count * kRowSize) : nullptr;
    return count == 0 || list.rows;
}

bool Disjoint(const List& a, const List& b) {
    return a.end <= b.begin || b.end <= a.begin;
}

bool IsPretztailRow(const uint8_t* row) {
    return Word(row) == kTag && Word(row, 4) == kClass && Word(row, 8) == 0 &&
        Word(row, 0x10) == kOrdinal && Word(row, 0x14) == 0;
}

bool FindPretztailRow(const List& list, uint32_t& match) {
    match = 0;
    for (uint32_t row = list.begin; row < list.end; row += kRowSize) {
        const auto* bytes = list.rows + (row - list.begin);
        if (Word(bytes) != kTag) continue;
        if (match || !IsPretztailRow(bytes)) return false;
        match = row;
    }
    return true;
}

struct Shop {
    uint32_t buyer{}, catalogAddress{}, workspace{};
    List catalog, canonical;
};

bool QualifyShop(RexFabricCall* call, uint32_t controller, Shop& shop) {
    uint32_t shopId, local, state, manager, component;
    const auto* modes = Map(kModeFlags, 8);
    if (!modes || Word(modes) || Word(modes, 4) || !Read(controller, 0x348, shopId) || shopId != kWilly ||
        !Read(controller, 0xa8, shop.buyer) || !Read(kLocalPlayer, 0, local) || !local || shop.buyer != local)
        return false;

    for (uint32_t slot : kLiveSlots) {
        // 0x8223A0D0 reads +0x1EC as the multiplayer flag. Single-player gardens
        // can have a session object with state 4 at +0xC.
        uint32_t session, multiplayer;
        if (!Read(slot, 0, session) || (session && (!Read(session, 0x1ec, multiplayer) || multiplayer))) return false;
    }

    // 0x82400888 binds controller+0x358 to this garden's Willy shop params.
    const auto garden = GuestAddress(api->call_guest(call, kTopGarden, nullptr, 0));
    constexpr uint32_t willyCatalogOffset = 0x4c0c + kWilly * 0x28;
    if (!Read(garden, 0x4a00, shop.workspace) || !Read(shop.workspace, 0x8e4, component) ||
        !component || component > UINT32_MAX - willyCatalogOffset ||
        !Read(controller, 0x358, shop.catalogAddress) || shop.catalogAddress != component + willyCatalogOffset ||
        !Read(local, 0x1e70, state) || !Read(state, 0x2d4, manager) || manager < 0x10000 ||
        manager > UINT32_MAX - 8)
        return false;

    return ReadList(shop.catalogAddress, shop.catalog) && ReadList(manager + 8, shop.canonical) &&
        Disjoint(shop.catalog, shop.canonical);
}

void ApplySoldOut(RexFabricCall* call, void*) {
    const bool inspect = IsRestricted() && api->get_gpr(call, 4) == kClass;
    const auto avatar = inspect ? GuestAddress(api->get_gpr(call, 3)) : 0;
    api->call_next(call);
    if (!avatar || !IsRestricted()) return;

    uint32_t callback, controller, shopId, backLink, avatarCatalog;
    if (!Read(avatar, 0x1728, callback) || callback != kWillyCallback ||
        !Read(avatar, 0x172c, controller) || !Read(controller, 0x348, shopId) || shopId != kWilly) return;

    List reel;
    Shop shop;
    if (!Read(controller, 0x1e4, backLink) || backLink != avatar ||
        !Read(avatar, 0xecc, avatarCatalog) || !QualifyShop(call, controller, shop) ||
        avatarCatalog != shop.catalogAddress || avatar > UINT32_MAX - 0xee4 ||
        !ReadList(avatar + 0xee4, reel) || !Disjoint(reel, shop.catalog) || !Disjoint(reel, shop.canonical)) {
        diagnostics.fetch_or(ReelRejected, std::memory_order_relaxed);
        return;
    }

    uint32_t catalogRow, canonicalRow, reelRow;
    if (!FindPretztailRow(reel, reelRow)) {
        diagnostics.fetch_or(ReelRejected, std::memory_order_relaxed);
        return;
    }
    if (!reelRow) return; // The current building subgroup may not contain it.
    const auto* bytes = reel.rows + (reelRow - reel.begin);
    if (!FindPretztailRow(shop.catalog, catalogRow) || !catalogRow ||
        !FindPretztailRow(shop.canonical, canonicalRow) || !canonicalRow || Word(bytes, 0x20) != catalogRow ||
        !Map(reelRow + kAvailability, sizeof(uint32_t), true)) {
        diagnostics.fetch_or(ReelRejected, std::memory_order_relaxed);
        return;
    }

    if (IsRestricted() && Word(bytes, kAvailability) != 0) {
        api->write_u32(reelRow + kAvailability, 0);
        diagnostics.fetch_or(ReelDisabled, std::memory_order_relaxed);
    }
}

bool ShouldDenyPlacement(RexFabricCall* call) {
    const auto preview = GuestAddress(api->get_gpr(call, 4));
    uint32_t tag, itemClass;
    if (!Read(preview, 0x84, tag) || tag != kTag || !Read(preview, 0x88, itemClass) || itemClass != kClass)
        return false;

    const auto control = GuestAddress(api->get_gpr(call, 3));
    uint32_t mode, tagInCursor, buyer, controller, cursor, localCursor, cursorControl, catalogRow, canonicalRow;
    Shop shop;
    const bool qualified = Read(control, 0, mode) && mode == 9 &&
        Read(control, 0x354, tagInCursor) && tagInCursor == kTag && Read(control, 0x130, buyer) &&
        Read(control, 0x134, controller) && QualifyShop(call, controller, shop) && buyer == shop.buyer &&
        Read(control, 8, cursor) && Read(shop.workspace, 0x8fc, localCursor) && cursor == localCursor &&
        Read(cursor, 0xe0, cursorControl) && cursorControl == control &&
        FindPretztailRow(shop.catalog, catalogRow) && catalogRow &&
        FindPretztailRow(shop.canonical, canonicalRow) && canonicalRow;
    if (!qualified) diagnostics.fetch_or(PlacementRejected, std::memory_order_relaxed);
    return qualified;
}

void GuardPlacement(RexFabricCall* call, void*) {
    if (IsRestricted() && ShouldDenyPlacement(call) && IsRestricted()) {
        // 0x822D6B74 consumes a false result before the purchased clone or billing.
        api->set_gpr(call, 3, 0);
        diagnostics.fetch_or(PlacementDenied, std::memory_order_relaxed);
        return;
    }
    api->call_next(call);
}
}

void vivaap::InitializePretztailRestriction(const RexFabricApi* fabric, RexFabricMod* owner) {
    api = fabric;
    mod = owner;
}

void vivaap::ShutdownPretztailRestriction() {
    ready.store(false, std::memory_order_release);
    restricted.store(false, std::memory_order_release);
    if (placementHook) api->unhook(placementHook);
    if (reelHook) api->unhook(reelHook);
    placementHook = reelHook = 0;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApPretztailGetUserDataRoot(char* buffer, int32_t capacity) {
    if (!api || api->size < offsetof(RexFabricApi, get_cvar) + sizeof(api->get_cvar) ||
        !api->get_cvar || !buffer || capacity <= 0) return -1;
    return api->get_cvar("user_data_root", buffer, capacity);
}

extern "C" REXFABRIC_MOD_EXPORT int VivaApPretztailPrepare() {
    if (ready.load(std::memory_order_acquire)) return 0;
    if (!api || !mod || api->size < offsetof(RexFabricApi, write_u32) + sizeof(api->write_u32) ||
        !api->is_hookable || !api->hook || !api->unhook || !api->call_next || !api->get_gpr || !api->set_gpr ||
        !api->call_guest || !api->guest_pointer || !api->write_u32) return 1;
    if (!api->is_hookable(kReelBuilder) || !api->is_hookable(kPlacementValidator) ||
        !api->is_hookable(kTopGarden)) return 2;

    reelHook = api->hook(mod, kReelBuilder, ApplySoldOut, nullptr);
    if (!reelHook) return 3;
    placementHook = api->hook(mod, kPlacementValidator, GuardPlacement, nullptr);
    if (!placementHook) {
        vivaap::ShutdownPretztailRestriction();
        return 4;
    }
    ready.store(true, std::memory_order_release);
    return 0;
}

extern "C" REXFABRIC_MOD_EXPORT int VivaApPretztailSetRestricted(int value) {
    if (value && !ready.load(std::memory_order_acquire)) return 0;
    restricted.store(value != 0, std::memory_order_release);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT uint32_t VivaApPretztailTakeDiagnostics() {
    return diagnostics.exchange(0, std::memory_order_relaxed);
}
