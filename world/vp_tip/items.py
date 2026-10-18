import typing
from BaseClasses import ItemClassification, Item

# Instance Data for TiP Items
class TIPItem(Item):
    game = "Viva Piñata: Trouble in Paradise"

# Wrapper for item definitions
class ItemData(typing.NamedTuple):
    code: typing.Optional[int]
    classification: ItemClassification

all_items = {
    # Fertilizers
    "Orange Fertilizer": ItemData(10, ItemClassification.progression),
    "Purple Fertilizer": ItemData(11, ItemClassification.progression),
    "Yellow Fertilizer": ItemData(12, ItemClassification.progression),
    "Red Fertilizer": ItemData(13, ItemClassification.progression),
    "Blue Fertilizer": ItemData(14, ItemClassification.progression),
    "Brown Fertilizer": ItemData(15, ItemClassification.progression),
    "Green Fertilizer": ItemData(16, ItemClassification.progression),
    "Special Mix Fertilizer": ItemData(17, ItemClassification.useful), # Not sure how this fits in yet, maybe disabled?

    # Money
    "10 Coins": ItemData(200000, ItemClassification.filler),
    "50 Coins": ItemData(200001, ItemClassification.filler),
}