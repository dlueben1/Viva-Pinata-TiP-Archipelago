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
ACCESSORY_ITEM_ID_BASE = 500

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

# Garden Items (Useful & Progression)
garden_items = {
    "Captains Cutlass": ItemData(GARDEN_ITEM_ID_BASE, ItemClassification.useful),
    "Dastardos Scarer": ItemData(GARDEN_ITEM_ID_BASE + 1, ItemClassification.useful),
    "Pirate Statue": ItemData(GARDEN_ITEM_ID_BASE + 2, ItemClassification.progression),
    "Red Eye Rainbow": ItemData(
        GARDEN_ITEM_ID_BASE + 3, ItemClassification.progression
    ),
    "Water Waiver": ItemData(GARDEN_ITEM_ID_BASE + 4, ItemClassification.useful),
    "Moon-on-a-Stick": ItemData(
        GARDEN_ITEM_ID_BASE + 5, ItemClassification.progression
    ),
    "Firebrand": ItemData(GARDEN_ITEM_ID_BASE + 6, ItemClassification.progression),
    "Fish Fountain": ItemData(GARDEN_ITEM_ID_BASE + 7, ItemClassification.progression),
    "Hay Bale": ItemData(GARDEN_ITEM_ID_BASE + 8, ItemClassification.progression),
    "Milk Churn": ItemData(GARDEN_ITEM_ID_BASE + 9, ItemClassification.progression),
    "Pile of Leaves": ItemData(
        GARDEN_ITEM_ID_BASE + 10, ItemClassification.progression
    ),
    "Lantern O' Loot": ItemData(GARDEN_ITEM_ID_BASE + 11, ItemClassification.useful),
    "Surfboard": ItemData(GARDEN_ITEM_ID_BASE + 12, ItemClassification.progression),
    "Tombstone": ItemData(GARDEN_ITEM_ID_BASE + 13, ItemClassification.progression),
    "Barrel": ItemData(GARDEN_ITEM_ID_BASE + 14, ItemClassification.progression),
    "Bird and Bear Statue": ItemData(
        GARDEN_ITEM_ID_BASE + 15, ItemClassification.progression
    ),
    "Birdbath": ItemData(GARDEN_ITEM_ID_BASE + 16, ItemClassification.progression),
}

# Produce
produce_items = {
    "Honey": ItemData(PRODUCE_ITEM_ID_BASE, ItemClassification.progression),
    "Milk": ItemData(PRODUCE_ITEM_ID_BASE + 1, ItemClassification.progression),
    "Bone": ItemData(PRODUCE_ITEM_ID_BASE + 2, ItemClassification.progression),
    "Bread": ItemData(PRODUCE_ITEM_ID_BASE + 3, ItemClassification.progression),
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
    **produce_items,
    **candy_items,
    **paperpet_items,
}
