from BaseClasses import Location, LocationProgressType
from constants import MAX_GARDENER_LEVEL, MIN_GARDENER_LEVEL

# Instance Data for TiP Locations
class TIPLocation(Location):
    game = "Viva Piñata: Trouble in Paradise"

#region All Location Definitions

# Gardener Level/Rank Locations
rank_locations = {
    f"Reach Gardener Level {level}": level
    for level in range(MIN_GARDENER_LEVEL, MAX_GARDENER_LEVEL + 1)
}

# Collection of all Locations
all_locations = {**rank_locations}

#endregion

#region Location Helpers

def get_rank_locations(goal_level: int) -> dict[str, int]:
    """Return only Gardener Level checks up to the goal."""

    # If the gardener level is invalid, don't return any locations
    if goal_level < MIN_GARDENER_LEVEL or goal_level > MAX_GARDENER_LEVEL:
        return {}

    return {
        f"Reach Gardener Level {level}": rank_locations[f"Reach Gardener Level {level}"]
        for level in range(MIN_GARDENER_LEVEL, goal_level + 1)
    }


#endregion