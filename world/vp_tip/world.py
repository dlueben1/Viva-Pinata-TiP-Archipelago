from world.vp_tip.constants import MIN_GARDENER_LEVEL
from worlds.AutoWorld import World
from BaseClasses import Region
from .options import TIPOptions
from .items import TIPItem, all_items
from rule_builder.rules import CanReachLocation

from .locations import (
    TIPLocation,
    all_locations,
    get_rank_locations,
)


class TIPWorld(World):
    game = "Viva Pinata: Trouble in Paradise"

    options_dataclass = TIPOptions
    options: TIPOptions

    # All Locations and Items (whether used or not)
    location_name_to_id = all_locations
    item_name_to_id: dict[str, int] = {
        name: data.code for name, data in all_items.items() if data.code is not None
    }

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        garden = Region("Garden", self.player, self.multiworld)

        # Determine the player's goals for this session
        goals = self.options.goals.value

        # See if there are any Gardener Level goals set
        enabled_rank_locations = {}
        if goals["reach_gardener_level"]:
            goal_level = goals["reach_gardener_level"]
            enabled_rank_locations = get_rank_locations(goal_level)

        garden.add_locations(enabled_rank_locations, TIPLocation)

        menu.connect(garden)
        self.multiworld.regions.extend([menu, garden])

    def set_rules(self) -> None:
        goal_level = self.options.goals.value["reach_gardener_level"]

        for level in range(MIN_GARDENER_LEVEL + 1, goal_level + 1):
            current = self.get_location(f"Reach Gardener Level {level}")

            previous = f"Reach Gardener Level {level - 1}"

            self.set_rule(current, CanReachLocation(previous))

    # region Items

    def create_item(self, name: str) -> TIPItem:
        """Creates an item instance based on its name"""
        data = all_items[name]
        return TIPItem(name, data.classification, data.code, self.player)

    def get_filler_item_name(self) -> str:
        """Returns the name of the filler item needed if the pool needs more items"""
        return "Placeholder Item"

    def create_filler(self) -> TIPItem:
        """Creates a filler item instance"""
        return self.create_item(self.get_filler_item_name())

    def create_items(self) -> None:
        """Build the itempool based on user settings"""
        locations = self.multiworld.get_unfilled_locations(self.player)

        for _ in locations:
            self.multiworld.itempool.append(self.create_filler())

    # endregion
