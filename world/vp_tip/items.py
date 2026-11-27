import typing
from BaseClasses import ItemClassification, Item
from . import data


# Instance Data for TiP Items
class TIPItem(Item):
    game = "Viva Pinata: Trouble in Paradise"


# Wrapper for item definitions
class ItemData(typing.NamedTuple):
    code: typing.Optional[int]
    classification: ItemClassification


# Item ID Bases
COIN_ID_BASE = 1
BLUEPRINT_ID_BASE = 100
GARDEN_ITEM_ID_BASE = 200
PRODUCE_ITEM_ID_BASE = 300
CANDY_ITEM_ID_BASE = 320
SEED_ITEM_ID_BASE = 400
ACCESSORY_ITEM_ID_BASE = 500

# Catalog Cleanup for large datasets
lottie_catalog_rows = list(data.catalog_costolot.items())

# Pinata Homes (Used for Romance Goal, Romance Checks, XP)
unique_home_names = sorted(
    {
        "Flutterscotch" if "Flutterscotch" in name else name
        for name in data.pinata_names
        if name not in data.legendary_pinata_names
    }
)
pinata_home_blueprints = {
    f"{name} Home Blueprints": ItemData(
        BLUEPRINT_ID_BASE + index, ItemClassification.progression
    )
    for index, name in enumerate(unique_home_names)
}

# Paper Pets Accessories (Used for Romance Goals)
paperpet_items = {
    name: ItemData(ACCESSORY_ITEM_ID_BASE + index, ItemClassification.progression)
    for index, name in enumerate(data.catalog_paperpets)
}

# Seeds (Progression, Core of the AP)
seed_items = {
    f"{name}s": ItemData(
        SEED_ITEM_ID_BASE + index,
        ItemClassification.progression,
    )
    for index, (name, row) in enumerate(
        (entry for entry in lottie_catalog_rows
         if entry[1]["ap_control"] == "1" and entry[1]["shop_section"] == "Seed")
    )
}

# Garden Items (Useful & Progression)
garden_items = {
    name: ItemData(
        GARDEN_ITEM_ID_BASE + index,
        (
            ItemClassification.useful
            if row["ap_item_type"] == "useful"
            else ItemClassification.progression
        ),
    )
    for index, (name, row) in enumerate(
        (entry for entry in lottie_catalog_rows
         if entry[1]["ap_control"] == "1" and entry[1]["shop_section"] == "Garden Items")
    )
}

# Produce
produce_items = {
    name: ItemData(
        PRODUCE_ITEM_ID_BASE + index,
        ItemClassification.progression,
    )
    for index, (name, row) in enumerate(
        (entry for entry in lottie_catalog_rows
         if entry[1]["ap_control"] == "1" and entry[1]["shop_section"] == "Produce")
    )
}

# Candy
candy_items = {
    "Happy Candy": ItemData(CANDY_ITEM_ID_BASE, ItemClassification.useful),
    "Joy Candy": ItemData(CANDY_ITEM_ID_BASE + 1, ItemClassification.useful),
    "Romance Candy": ItemData(CANDY_ITEM_ID_BASE + 2, ItemClassification.useful),
}

# Chocolate Coins (Repeatable Filler & Useful Variants)
chocolate_coins = {
    "10 Coins": ItemData(COIN_ID_BASE, ItemClassification.filler),
    "500 Coins": ItemData(COIN_ID_BASE + 1, ItemClassification.useful),
}

# Every possible item
all_items = {
    **chocolate_coins,
    **pinata_home_blueprints,
    **garden_items,
    **seed_items,
    **produce_items,
    **candy_items,
    **paperpet_items,
}
