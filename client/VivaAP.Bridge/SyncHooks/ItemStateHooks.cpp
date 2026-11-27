#include "pch.h"
#include "ItemStateHooks.h"
#include "../Logger.h"

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
constexpr uint32_t kSelectItem        = 0x823673F0;
constexpr uint32_t kAccessoryColours  = 0x82365DD8;
constexpr uint32_t kShopAction        = 0x824027F8;
constexpr uint32_t kShopRefresh       = 0x825B3688;
constexpr uint32_t kPublisher         = 0x825B1990;
constexpr uint32_t kHasMembership     = 0x825B2508;
constexpr uint32_t kTopGarden         = 0x824E1158;
constexpr uint32_t kHostPlayer        = 0x8252ECD0;
constexpr uint32_t kWillyCallback     = 0x824027A8;
constexpr uint32_t kLocalPlayer       = 0x83DBED50;
constexpr uint32_t kModeFlags         = 0x83DBED7C;
constexpr uint32_t kLiveSlots[]       = {0x82C4A4EC, 0x82C4A4F0, 0x82C4A4F4};

constexpr uint32_t kLottie = 0, kWilly = 4, kPaperPets = 5;
constexpr uint32_t kHouseClass = 0x13, kAccessoryClass = 4, kSeedClass = 28;
constexpr uint32_t kHouseRowSize = 0x24, kPointerRowSize = 4;
constexpr uint32_t kMaxRows = 1024, kMaxEntries = 629;

enum Diagnostic : uint32_t {
    SoldOutApplied = 1, PlacementDenied = 2, GrantConfirmed = 4,
    GrantDeferred = 8, ReelUnqualified = 16, PlacementUnqualified = 32,
    GrantAttemptFailed = 64
};

struct Entry {
    uint32_t tag = 0;
    uint32_t ordinal = 0;
    uint32_t itemClass = 0;
    uint32_t shop = 0;
    uint32_t queue = 0;
    uint32_t trigger = 0;
    uint32_t acquisitionTrigger = 0;
    uint32_t expectedPendingMask = 0;
    std::atomic<bool> owned{false};
    // A destructive publication must not be repeated if it failed midway.
    std::atomic<uint32_t> attemptedManager{0};
    std::atomic<uint32_t> deferredManager{0};
    std::atomic<uint32_t> warnings{0};
};

const RexFabricApi* api = nullptr;
RexFabricMod* mod = nullptr;
RexFabricHookId reelHook = 0, placementHook = 0, refreshHook = 0, selectionHook = 0;
RexFabricHookId colourHook = 0, actionHook = 0;
std::array<Entry, kMaxEntries> entries;
uint32_t entryCount = 0;            // Immutable once hooks are installed.
std::mutex registrationLock;
std::atomic<bool> active{false};
// Early publication follows AP ownership at the next matching shop refresh.
std::atomic<bool> earlyGrantsEnabled{true};
std::atomic<uint32_t> diagnostics{0};
std::atomic<uint32_t> reportedPaperPetsWarnings{0};
thread_local bool granting = false;

void WarnPaperPetsOnce(uint32_t bit, const char* message) {
    if ((reportedPaperPetsWarnings.fetch_or(bit, std::memory_order_relaxed) & bit) == 0)
        vivaap::Log(REXFABRIC_LOG_WARN, message);
}

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
bool FindRecord(const List& list, const Entry& entry, uint32_t& match) {
    match = 0;
    for (uint32_t row = list.begin; row < list.end; row += kHouseRowSize) {
        const auto* data = list.rows + (row - list.begin);
        if (Word(data) != entry.tag) continue;
        if (match || Word(data, 4) != entry.itemClass || Word(data, 8) != 0 ||
            Word(data, 0x10) != entry.ordinal ||
            (entry.shop == kWilly && Word(data, 0x14) != 0))
            return false;
        match = row;
    }
    return true;
}

struct Shop { uint32_t buyer{}, catalogAddress{}, workspace{}; List catalog, canonical; };

bool QualifyShop(RexFabricCall* call, uint32_t controller, uint32_t expectedShop, Shop& shop) {
    uint32_t shopId = 0, local = 0, state = 0, manager = 0, component = 0;
    if (!OrdinarySinglePlayer() || !Read(controller, 0x348, shopId) || shopId != expectedShop ||
        !Read(controller, 0xa8, shop.buyer) || !Read(kLocalPlayer, 0, local) ||
        !local || shop.buyer != local) return false;

    const uint32_t garden = GuestAddress(api->call_guest(call, kTopGarden, nullptr, 0));
    const uint32_t catalogOffset = 0x4c0c + expectedShop * 0x28;
    if (!Read(garden, 0x4a00, shop.workspace) ||
        !Read(shop.workspace, 0x8e4, component) || !component ||
        component > UINT32_MAX - catalogOffset ||
        !Read(controller, 0x358, shop.catalogAddress) ||
        shop.catalogAddress != component + catalogOffset ||
        !Read(local, 0x1e70, state) || !Read(state, 0x2d4, manager)) return false;

    return ReadList(shop.catalogAddress, kHouseRowSize, shop.catalog) &&
        ReadList(manager + 8, kHouseRowSize, shop.canonical) &&
        Disjoint(shop.catalog, shop.canonical);
}

// Called AFTER the original reel builder. The shop's ordinary selection path
// reads row+0x18 and renders native Sold Out when it is zero.
void OnReelBuilt(RexFabricCall* call, void*) {
    const uint32_t itemClass = static_cast<uint32_t>(api->get_gpr(call, 4));
    bool inspect = false;
    if (active.load(std::memory_order_acquire))
        for (uint32_t i = 0; i < entryCount; ++i)
            inspect |= entries[i].itemClass == itemClass;
    const uint32_t avatar = inspect ? GuestAddress(api->get_gpr(call, 3)) : 0;
    api->call_next(call);
    if (!avatar || !active.load(std::memory_order_acquire)) return;

    uint32_t callback = 0, controller = 0, shopId = 0, backLink = 0, avatarCatalog = 0;
    if (!Read(avatar, 0x1728, callback) || callback != kWillyCallback ||
        !Read(avatar, 0x172c, controller) || !Read(controller, 0x348, shopId) ||
        (shopId != kWilly && shopId != kLottie && shopId != kPaperPets)) return;

    Shop shop;
    List reel;
    if (!Read(controller, 0x1e4, backLink) || backLink != avatar ||
        !Read(avatar, 0xecc, avatarCatalog) ||
        !QualifyShop(call, controller, shopId, shop) ||
        avatarCatalog != shop.catalogAddress ||
        avatar > UINT32_MAX - 0xee4 ||
        !ReadList(avatar + 0xee4, kHouseRowSize, reel) ||
        !Disjoint(reel, shop.catalog) || !Disjoint(reel, shop.canonical)) {
        diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
        if (shopId == kPaperPets)
            WarnPaperPetsOnce(1, "Paper Pets reel qualification failed; access restrictions require a matching local shop.");
        return;
    }

    // No managed callback on the game thread: a C# worker publishes owned bits.
    for (uint32_t i = 0; i < entryCount; ++i) {
        auto& entry = entries[i];
        if (entry.shop != shopId || entry.itemClass != itemClass ||
            entry.owned.load(std::memory_order_acquire)) continue;

        uint32_t reelRow = 0, catalogRow = 0, canonicalRow = 0;
        if (!FindRecord(reel, entry, reelRow) ||
            !FindRecord(shop.catalog, entry, catalogRow) ||
            !FindRecord(shop.canonical, entry, canonicalRow)) {
            diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
            if (entry.shop == kPaperPets &&
                (entry.warnings.fetch_or(2, std::memory_order_relaxed) & 2) == 0)
                vivaap::Log(REXFABRIC_LOG_WARN, "Accessory reel record qualification failed for tag " + std::to_string(entry.tag));
            continue;
        }
        // An item not yet unlocked by vanilla is absent from the reel.
        if (!reelRow) continue;
        const auto* data = reel.rows + (reelRow - reel.begin);
        if (!catalogRow || !canonicalRow || Word(data, 0x20) != catalogRow ||
            !Map(reelRow + 0x18, 4, true)) {
            diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
            if (entry.shop == kPaperPets &&
                (entry.warnings.fetch_or(2, std::memory_order_relaxed) & 2) == 0)
                vivaap::Log(REXFABRIC_LOG_WARN, "Accessory reel record qualification failed for tag " + std::to_string(entry.tag));
            continue;
        }
        if (!entry.owned.load(std::memory_order_acquire) && Word(data, 0x18) != 0) {
            api->write_u32(reelRow + 0x18, 0);
            diagnostics.fetch_or(SoldOutApplied, std::memory_order_relaxed);
        }
    }
}

// Colour choices are copies of one accessory row, so duplicate tags in this
// particular reel are expected. Catalog and canonical records remain unique.
void OnAccessoryColours(RexFabricCall* call, void*) {
    const uint32_t avatar = GuestAddress(api->get_gpr(call, 3));
    api->call_next(call);
    if (!active.load(std::memory_order_acquire) || !avatar) return;

    uint32_t callback = 0, controller = 0, shopId = 0, backLink = 0, catalog = 0;
    if (!Read(avatar, 0x1728, callback) || callback != kWillyCallback ||
        !Read(avatar, 0x172c, controller) || !Read(controller, 0x348, shopId) ||
        shopId != kPaperPets) return;

    Shop shop;
    List reel;
    if (!Read(controller, 0x1e4, backLink) || backLink != avatar ||
        !Read(avatar, 0xecc, catalog) || !QualifyShop(call, controller, kPaperPets, shop) ||
        catalog != shop.catalogAddress || avatar > UINT32_MAX - 0xee4 ||
        !ReadList(avatar + 0xee4, kHouseRowSize, reel) ||
        !Disjoint(reel, shop.catalog) || !Disjoint(reel, shop.canonical)) {
        diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
        WarnPaperPetsOnce(2, "Paper Pets colour reel qualification failed.");
        return;
    }

    for (uint32_t i = 0; i < entryCount; ++i) {
        auto& entry = entries[i];
        if (entry.shop != kPaperPets || entry.owned.load(std::memory_order_acquire)) continue;
        uint32_t catalogRow = 0, canonicalRow = 0;
        bool found = false, valid = true;
        for (uint32_t row = reel.begin; row < reel.end; row += kHouseRowSize) {
            const auto* data = reel.rows + (row - reel.begin);
            if (Word(data) != entry.tag) continue;
            found = true;
            if (Word(data, 4) != entry.itemClass || Word(data, 8) != 0 ||
                Word(data, 0x10) != entry.ordinal || !Map(row + 0x18, 4, true))
                valid = false;
        }
        if (!found) continue;
        if (!valid || !FindRecord(shop.catalog, entry, catalogRow) || !catalogRow ||
            !FindRecord(shop.canonical, entry, canonicalRow) || !canonicalRow) {
            diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
            if ((entry.warnings.fetch_or(4, std::memory_order_relaxed) & 4) == 0)
                vivaap::Log(REXFABRIC_LOG_WARN, "Accessory colour record qualification failed for tag " + std::to_string(entry.tag));
            continue;
        }
        for (uint32_t row = reel.begin; row < reel.end; row += kHouseRowSize) {
            const auto* data = reel.rows + (row - reel.begin);
            if (Word(data) == entry.tag && Word(data, 0x20) != catalogRow) valid = false;
        }
        if (!valid) {
            diagnostics.fetch_or(ReelUnqualified, std::memory_order_relaxed);
            if ((entry.warnings.fetch_or(4, std::memory_order_relaxed) & 4) == 0)
                vivaap::Log(REXFABRIC_LOG_WARN, "Accessory colour record qualification failed for tag " + std::to_string(entry.tag));
            continue;
        }
        for (uint32_t row = reel.begin; row < reel.end; row += kHouseRowSize) {
            const auto* data = reel.rows + (row - reel.begin);
            if (Word(data) == entry.tag && Word(data, 0x18) != 0 &&
                !entry.owned.load(std::memory_order_acquire)) {
                api->write_u32(row + 0x18, 0);
                diagnostics.fetch_or(SoldOutApplied, std::memory_order_relaxed);
            }
        }
    }
}

// The controller receives the selected reel row in r5, including for colour
// variants. Verify the selected address, rather than requiring a unique tag.
bool QualifyAccessoryAction(RexFabricCall* call, uint32_t controller,
                            uint32_t selected, const Entry& entry) {
    uint32_t avatar = 0, callback = 0, owner = 0, catalog = 0, mode = 0;
    uint32_t major = 0, minor = 0, catalogRow = 0, canonicalRow = 0;
    Shop shop;
    List reel;
    if (!Read(controller, 0x1e4, avatar) || !avatar ||
        !Read(avatar, 0x1728, callback) || callback != kWillyCallback ||
        !Read(avatar, 0x172c, owner) || owner != controller ||
        !Read(avatar, 0xec4, mode) || mode != 2 ||
        !Read(avatar, 0xecc, catalog) || !QualifyShop(call, controller, kPaperPets, shop) ||
        catalog != shop.catalogAddress || avatar > UINT32_MAX - 0xee4 ||
        !ReadList(avatar + 0xee4, kHouseRowSize, reel) ||
        !Disjoint(reel, shop.catalog) || !Disjoint(reel, shop.canonical) ||
        !Read(avatar, 0xf18, major) || !Read(avatar, 0xf1c, minor)) return false;

    const uint64_t index = static_cast<uint64_t>(major) * 4 + minor;
    if (index >= (reel.end - reel.begin) / kHouseRowSize ||
        selected != reel.begin + index * kHouseRowSize) return false;
    const auto* row = reel.rows + static_cast<size_t>(index) * kHouseRowSize;
    return Word(row) == entry.tag && Word(row, 4) == kAccessoryClass &&
        Word(row, 8) == 0 && Word(row, 0x10) == entry.ordinal &&
        FindRecord(shop.catalog, entry, catalogRow) && catalogRow &&
        FindRecord(shop.canonical, entry, canonicalRow) && canonicalRow &&
        Word(row, 0x20) == catalogRow;
}

void OnShopAction(RexFabricCall* call, void*) {
    const uint32_t action = static_cast<uint32_t>(api->get_gpr(call, 4));
    if (!active.load(std::memory_order_acquire) || (action != 0x2e && action != 0x30)) {
        api->call_next(call);
        return;
    }
    const uint32_t controller = GuestAddress(api->get_gpr(call, 3));
    const uint32_t selected = GuestAddress(api->get_gpr(call, 5));
    uint32_t shopId = 0, tag = 0;
    if (!Read(controller, 0x348, shopId) || shopId != kPaperPets) {
        api->call_next(call);
        return;
    }
    if (!Read(selected, 0, tag)) {
        // An unreadable transaction row cannot safely be passed to the game.
        diagnostics.fetch_or(PlacementUnqualified, std::memory_order_relaxed);
        WarnPaperPetsOnce(16, "Paper Pets acquisition denied: the selected row could not be read.");
        api->set_gpr(call, 3, 0);
        return;
    }
    Entry* entry = FindEntry(tag);
    if (!entry || entry->shop != kPaperPets || entry->owned.load(std::memory_order_acquire)) {
        api->call_next(call);
        return;
    }

    if (!QualifyAccessoryAction(call, controller, selected, *entry)) {
        diagnostics.fetch_or(PlacementUnqualified, std::memory_order_relaxed);
        if ((entry->warnings.fetch_or(1, std::memory_order_relaxed) & 1) == 0)
            vivaap::Log(REXFABRIC_LOG_WARN, "Accessory transaction qualification failed for tag " + std::to_string(tag));
    }
    // Fail closed for a known unreceived accessory even if provenance could not
    // be fully verified. Confirmation cleanup is performed by the caller.
    if (!entry->owned.load(std::memory_order_acquire)) {
        api->set_gpr(call, 3, 0);
        diagnostics.fetch_or(PlacementDenied, std::memory_order_relaxed);
        return;
    }
    api->call_next(call);
}

bool ShouldDenyPlacement(RexFabricCall* call) {
    const uint32_t preview = GuestAddress(api->get_gpr(call, 4));
    uint32_t tag = 0, itemClass = 0;
    if (!Read(preview, 0x84, tag) || !Read(preview, 0x88, itemClass)) return false;
    Entry* entry = FindEntry(tag);
    if (!entry || entry->itemClass != itemClass ||
        entry->owned.load(std::memory_order_acquire)) return false;

    const uint32_t control = GuestAddress(api->get_gpr(call, 3));
    uint32_t mode = 0, tagInCursor = 0, buyer = 0, controller = 0;
    uint32_t cursor = 0, localCursor = 0, cursorControl = 0;
    uint32_t catalogRow = 0, canonicalRow = 0;
    Shop shop;
    const bool qualified = Read(control, 0, mode) && mode == 9 &&
        Read(control, 0x354, tagInCursor) && tagInCursor == tag &&
        Read(control, 0x130, buyer) && Read(control, 0x134, controller) &&
        QualifyShop(call, controller, entry->shop, shop) && buyer == shop.buyer &&
        Read(control, 8, cursor) && Read(shop.workspace, 0x8fc, localCursor) &&
        cursor == localCursor && Read(cursor, 0xe0, cursorControl) &&
        cursorControl == control && FindRecord(shop.catalog, *entry, catalogRow) &&
        catalogRow && FindRecord(shop.canonical, *entry, canonicalRow) && canonicalRow;
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

// Guard selection itself, before immediate one-time purchase side effects and
// before the controller opens a placement cursor. Only actual Lottie reel rows
// are eligible; class/category navigation and every unregistered row pass on.
void OnSelectItem(RexFabricCall* call, void*) {
    const uint32_t avatar = GuestAddress(api->get_gpr(call, 3));
    uint32_t mode = 0, callback = 0, controller = 0, backLink = 0, catalog = 0;
    uint32_t major = 0, minor = 0;
    Shop shop;
    List reel;
    if (active.load(std::memory_order_acquire) &&
        Read(avatar, 0xec4, mode) && mode == 2 &&
        Read(avatar, 0x1728, callback) && callback == kWillyCallback &&
        Read(avatar, 0x172c, controller) &&
        Read(controller, 0x1e4, backLink) && backLink == avatar &&
        Read(avatar, 0xecc, catalog) &&
        QualifyShop(call, controller, kLottie, shop) && catalog == shop.catalogAddress &&
        avatar <= UINT32_MAX - 0xee4 && ReadList(avatar + 0xee4, kHouseRowSize, reel) &&
        Disjoint(reel, shop.catalog) && Disjoint(reel, shop.canonical) &&
        Read(avatar, 0xf18, major) && Read(avatar, 0xf1c, minor)) {
        const uint64_t index = static_cast<uint64_t>(major) * 4 + minor;
        const uint64_t count = (reel.end - reel.begin) / kHouseRowSize;
        if (index < count) {
            const auto* row = reel.rows + static_cast<size_t>(index) * kHouseRowSize;
            Entry* entry = FindEntry(Word(row));
            uint32_t reelRow = 0, catalogRow = 0, canonicalRow = 0;
            if (entry && entry->shop == kLottie &&
                !entry->owned.load(std::memory_order_acquire) &&
                FindRecord(reel, *entry, reelRow) && reelRow == reel.begin + index * kHouseRowSize &&
                FindRecord(shop.catalog, *entry, catalogRow) && catalogRow &&
                FindRecord(shop.canonical, *entry, canonicalRow) && canonicalRow &&
                Word(row, 0x20) == catalogRow &&
                !entry->owned.load(std::memory_order_acquire)) {
                diagnostics.fetch_or(PlacementDenied, std::memory_order_relaxed);
                return;
            }
        }
    }
    api->call_next(call);
}

// Seeds use the same mutable metadata pointer in q1 and q2. Check all eight
// queues so no duplicate pointer, alternative row or unexplained registration
// can reach the destructive publisher. Mask 0 is also used after publication.
bool SeedQueuesMatch(uint32_t manager, const Entry& entry, uint32_t mask, uint32_t& metadata) {
    uint32_t queues = 0;
    if (!Read(manager, 0x1c, queues)) return false;
    for (uint32_t q = 1; q <= 8; ++q) {
        uint32_t queue = 0, type = 0, matches = 0;
        List pending;
        if (!Read(queues, (q - 1) * 4, queue) || !Read(queue, 0, type) || type != q ||
            !ReadList(queue + 4, kPointerRowSize, pending)) return false;
        for (uint32_t p = pending.begin; p < pending.end; p += 4) {
            const uint32_t row = Word(pending.rows + (p - pending.begin));
            uint32_t tag = 0;
            if (!Read(row, 4, tag)) return false;
            if (tag != entry.tag) continue;
            uint32_t ordinal = 0, level = 0, acquisition = 0, liveMask = 0;
            if (++matches > 1 || (metadata && metadata != row) ||
                !Read(row, 0x28, ordinal) || ordinal != entry.ordinal ||
                !Read(row, 8, level) || level != entry.trigger ||
                !Read(row, 0xc, acquisition) || acquisition != entry.acquisitionTrigger ||
                !Read(row, 0x2c, liveMask) || liveMask != mask) return false;
            metadata = row;
        }
        if (matches != ((mask & (1u << q)) ? 1u : 0u)) return false;
    }
    return metadata != 0;
}

// Validate a unique registration with exactly the expected single queue.
// Keep this contract unchanged for houses, ordinary Lottie items and accessories.
bool FindPendingRow(uint32_t manager, const Entry& entry, uint32_t& mask) {
    uint32_t queues = 0, queue = 0, type = 0;
    if (!Read(manager, 0x1c, queues) ||
        !Read(queues, (entry.queue - 1) * 4, queue) ||
        !Read(queue, 0, type) || type != entry.queue)
        return false;
    List pending;
    if (!ReadList(queue + 4, kPointerRowSize, pending)) return false;
    bool found = false;
    for (uint32_t p = pending.begin; p < pending.end; p += 4) {
        const uint32_t row = Word(pending.rows + (p - pending.begin));
        uint32_t tag = 0, ordinal = 0, trigger = 0, currentMask = 0;
        if (!Read(row, 4, tag) || !Read(row, 0x28, ordinal) ||
            !Read(row, 4 + entry.queue * 4, trigger) || !Read(row, 0x2c, currentMask))
            return false;
        if (tag != entry.tag) continue;
        if (found || ordinal != entry.ordinal || trigger != entry.trigger ||
            currentMask != (1u << entry.queue)) return false;
        mask = currentMask;
        found = true;
    }
    return found;
}

// Called BEFORE vanilla shop refresh so newly published items can appear in
// its normal derived catalog. The only guest-thread mutation is call_guest.
void OnShopRefresh(RexFabricCall* call, void*) {
    if (active.load(std::memory_order_acquire) &&
        earlyGrantsEnabled.load(std::memory_order_acquire) && !granting &&
        (api->get_gpr(call, 4) == kWilly || api->get_gpr(call, 4) == kLottie ||
         api->get_gpr(call, 4) == kPaperPets)) {
        const uint32_t refreshShop = static_cast<uint32_t>(api->get_gpr(call, 4));
        uint32_t manager = 0, local = 0;
        bool refreshQualified = false;
        for (uint32_t i = 0; i < entryCount; ++i)
            refreshQualified |= entries[i].shop == refreshShop;
        if (refreshQualified && (refreshShop == kLottie || refreshShop == kPaperPets)) {
            const uint32_t params = GuestAddress(api->get_gpr(call, 3));
            const uint32_t garden = GuestAddress(api->call_guest(call, kTopGarden, nullptr, 0));
            uint32_t workspace = 0, component = 0;
            const uint32_t offset = 0x4c0c + refreshShop * 0x28;
            refreshQualified = Read(garden, 0x4a00, workspace) &&
                Read(workspace, 0x8e4, component) && component >= 0x10000 &&
                component <= UINT32_MAX - offset && params == component + offset;
        }
        if (refreshQualified && ReadPlayerManager(manager, local)) {
            if (refreshShop == kPaperPets &&
                GuestAddress(api->call_guest(call, kHostPlayer, nullptr, 0)) != local) {
                diagnostics.fetch_or(GrantDeferred, std::memory_order_relaxed);
                WarnPaperPetsOnce(4, "Paper Pets publication deferred: the local and host player do not match.");
                api->call_next(call);
                return;
            }
            granting = true;
            for (uint32_t i = 0; i < entryCount; ++i) {
                auto& entry = entries[i];
                if (entry.shop != refreshShop ||
                    !entry.owned.load(std::memory_order_acquire) ||
                    entry.attemptedManager.load(std::memory_order_acquire) == manager)
                    continue;

                const uint64_t membershipArgs[2] = {manager, entry.tag};
                if (api->call_guest(call, kHasMembership, membershipArgs, 2) != 0)
                    continue;

                uint32_t liveMask = 0, seedMetadata = 0;
                const bool seed = entry.itemClass == kSeedClass;
                if (seed) liveMask = entry.expectedPendingMask;
                // Default seeds should already be canonical. Without membership
                // and valid pending alternatives, defer rather than fabricate a row.
                const bool pendingValid = seed
                    ? liveMask == 0x6 && SeedQueuesMatch(manager, entry, liveMask, seedMetadata)
                    : FindPendingRow(manager, entry, liveMask);
                if (!pendingValid) {
                    diagnostics.fetch_or(GrantDeferred, std::memory_order_relaxed);
                    if (entry.shop == kPaperPets &&
                        entry.deferredManager.exchange(manager, std::memory_order_relaxed) != manager)
                        vivaap::Log(REXFABRIC_LOG_WARN, "Accessory publication deferred for tag " + std::to_string(entry.tag) + ": pending row did not match.");
                    continue;
                }

                // Mark BEFORE mutating: a failed publisher must not be blindly retried.
                entry.attemptedManager.store(manager, std::memory_order_release);
                const uint64_t grantArgs[4] = {manager, entry.tag, liveMask, entry.ordinal};
                api->call_guest(call, kPublisher, grantArgs, 4);
                bool confirmed = api->call_guest(call, kHasMembership, membershipArgs, 2) != 0;
                if (seed && confirmed) {
                    uint32_t remaining = 0, canonicalRow = 0;
                    List canonical;
                    confirmed = Read(seedMetadata, 0x2c, remaining) && remaining == 0 &&
                        SeedQueuesMatch(manager, entry, 0, seedMetadata) &&
                        ReadList(manager + 8, kHouseRowSize, canonical) &&
                        FindRecord(canonical, entry, canonicalRow) && canonicalRow;
                }
                if (confirmed) {
                    diagnostics.fetch_or(GrantConfirmed, std::memory_order_relaxed);
                    if (entry.shop == kPaperPets)
                        vivaap::Log(REXFABRIC_LOG_INFO, "Native accessory unlock published for tag " + std::to_string(entry.tag));
                } else {
                    diagnostics.fetch_or(GrantAttemptFailed, std::memory_order_relaxed);
                    if (entry.shop == kPaperPets)
                        vivaap::Log(REXFABRIC_LOG_ERROR, "Native accessory publication failed for tag " + std::to_string(entry.tag) + "; not retrying for this manager.");
                }
            }
            granting = false;
        }
        else if (refreshShop == kPaperPets) {
            WarnPaperPetsOnce(8, "Paper Pets publication deferred: shop or player manager qualification failed.");
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
    if (actionHook) api->unhook(actionHook);
    if (colourHook) api->unhook(colourHook);
    if (selectionHook) api->unhook(selectionHook);
    if (placementHook) api->unhook(placementHook);
    if (reelHook) api->unhook(reelHook);
    refreshHook = actionHook = colourHook = selectionHook = placementHook = reelHook = 0;
}
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApHouseRegister(
    uint32_t tag, uint32_t ordinal, uint32_t species) {
    std::lock_guard lock(registrationLock);
    if (active.load() || !api || !tag || !species || ordinal >= 629) return 0;
    if (FindEntry(tag) || entryCount == kMaxEntries) return 0;
    auto& entry = entries[entryCount++];
    entry.tag = tag;
    entry.ordinal = ordinal;
    entry.itemClass = kHouseClass;
    entry.shop = kWilly;
    entry.queue = 5;
    entry.trigger = species;
    entry.owned.store(false);
    entry.attemptedManager.store(0);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApLottieRegister(
    uint32_t tag, uint32_t ordinal, uint32_t itemClass, uint32_t level) {
    std::lock_guard lock(registrationLock);
    // The caller supplies verified GeneralStore metadata for an in-scope item.
    // The pending-row check enforces a single ordinary level condition.
    if (active.load() || !api || !tag || ordinal >= 629 || level <= 1 ||
        itemClass >= 45 || itemClass == kHouseClass || itemClass == kSeedClass ||
        FindEntry(tag) || entryCount == kMaxEntries) return 0;
    auto& entry = entries[entryCount++];
    entry.tag = tag;
    entry.ordinal = ordinal;
    entry.itemClass = itemClass;
    entry.shop = kLottie;
    entry.queue = 1;
    entry.trigger = level;
    entry.owned.store(false);
    entry.attemptedManager.store(0);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApAccessoryRegister(
    uint32_t tag, uint32_t ordinal, uint32_t itemClass, uint32_t level, uint32_t mask) {
    std::lock_guard lock(registrationLock);
    if (active.load() || !api || !tag || ordinal >= 629 || level <= 1 ||
        itemClass != kAccessoryClass || mask != 0x2 ||
        FindEntry(tag) || entryCount == kMaxEntries) return 0;
    auto& entry = entries[entryCount++];
    entry.tag = tag;
    entry.ordinal = ordinal;
    entry.itemClass = itemClass;
    entry.shop = kPaperPets;
    entry.queue = 1;
    entry.trigger = level;
    entry.owned.store(false);
    entry.attemptedManager.store(0);
    entry.deferredManager.store(0);
    entry.warnings.store(0);
    return 1;
}

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApSeedRegister(
    uint32_t tag, uint32_t ordinal, uint32_t itemClass, uint32_t level,
    uint32_t acquisition, uint32_t mask) {
    std::lock_guard lock(registrationLock);
    const bool defaultSeed = ((tag == 441 && ordinal == 46) ||
                              (tag == 442 && ordinal == 45)) &&
        level == 0 && acquisition == 1 && mask == 0;
    const bool pendingSeed = level > 1 && acquisition == tag && mask == 0x6;
    if (active.load() || !api || !tag || ordinal >= kMaxEntries ||
        itemClass != kSeedClass || (!defaultSeed && !pendingSeed) ||
        FindEntry(tag) || entryCount == kMaxEntries) return 0;
    auto& entry = entries[entryCount++];
    entry.tag = tag;
    entry.ordinal = ordinal;
    entry.itemClass = itemClass;
    entry.shop = kLottie;
    entry.queue = 1;
    entry.trigger = level;
    entry.acquisitionTrigger = acquisition;
    entry.expectedPendingMask = mask;
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
    bool hasLottie = false, hasPaperPets = false;
    for (uint32_t i = 0; i < entryCount; ++i) {
        hasLottie |= entries[i].shop == kLottie;
        hasPaperPets |= entries[i].shop == kPaperPets;
    }
    if (!api->is_hookable(kReelBuilder) ||
        !api->is_hookable(kPlacementValidate) ||
        (hasLottie && !api->is_hookable(kSelectItem)) ||
        (hasPaperPets && (!api->is_hookable(kAccessoryColours) ||
                          !api->is_hookable(kShopAction) || !api->is_hookable(kHostPlayer))) ||
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
    if (hasLottie) selectionHook = api->hook(mod, kSelectItem, OnSelectItem, nullptr);
    if (hasLottie && !selectionHook) {
        vivaap::ShutdownItemStateHooks();
        return 0;
    }
    if (hasPaperPets) {
        colourHook = api->hook(mod, kAccessoryColours, OnAccessoryColours, nullptr);
        if (!colourHook) {
            vivaap::ShutdownItemStateHooks();
            return 0;
        }
        actionHook = api->hook(mod, kShopAction, OnShopAction, nullptr);
        if (!actionHook) {
            vivaap::ShutdownItemStateHooks();
            return 0;
        }
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

extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApItemSetOwned(uint32_t tag, int32_t owned) {
    if (owned != 0 && owned != 1) return 0;
    Entry* entry = FindEntry(tag);
    if (!entry) return 0;
    entry->owned.store(owned == 1, std::memory_order_release);
    return 1;
}
extern "C" REXFABRIC_MOD_EXPORT int32_t VivaApHouseSetOwned(uint32_t tag, int32_t owned) {
    return VivaApItemSetOwned(tag, owned);
}
extern "C" REXFABRIC_MOD_EXPORT uint32_t VivaApHouseTakeDiagnostics() {
    return diagnostics.exchange(0, std::memory_order_relaxed);
}
