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

# Chocolate Coins (Repeatable Filler & Useful Variants)
chocolate_coins = {
    "10 Coins": ItemData(COIN_ID_BASE, ItemClassification.filler),
    "500 Coins": ItemData(COIN_ID_BASE + 1, ItemClassification.useful),
}


# Every possible item
all_items = {
    **chocolate_coins,
    **pinata_home_blueprints,
}
