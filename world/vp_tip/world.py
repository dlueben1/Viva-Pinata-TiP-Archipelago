from worlds.AutoWorld import World
from BaseClasses import Region

from .locations import (
    TIPLocation,
    all_locations,
    get_rank_locations,
)

class TIPWorld(World):
  game = "Viva Piñata: Trouble in Paradise"

def create_regions(self) -> None:
    menu = Region("Menu", self.player, self.multiworld)
    garden = Region("Garden", self.player, self.multiworld)

    # Determine the player's goals for this session
    goals = self.options.goals.value

    # See if there are any Gardener Level goals set
    enabled_rank_locations = {}
    if(goals["reach_gardener_level"]):
        goal_level = goals["reach_gardener_level"]
        enabled_rank_locations = get_rank_locations(goal_level)

    garden.add_locations(enabled_rank_locations, TIPLocation)

    menu.connect(garden)
    self.multiworld.regions.extend([menu, garden])
