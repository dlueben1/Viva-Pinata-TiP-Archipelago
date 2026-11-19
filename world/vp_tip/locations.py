from BaseClasses import Location, LocationProgressType
from .constants import MIN_GARDENER_LEVEL, MAX_GARDENER_LEVEL, AWARD_CHECK_BASE_OFFSET
from . import data


# Instance Data for TiP Locations
class TIPLocation(Location):
    game = "Viva Pinata: Trouble in Paradise"


# region All Location Definitions

# Enumerate the Gardener Level/Rank Locations
rank_locations = {
    f"Reach Gardener Level {level}": level
    for level in range(MIN_GARDENER_LEVEL, MAX_GARDENER_LEVEL + 1)
}

# Load all the Award Locations
award_locations = {}
id = AWARD_CHECK_BASE_OFFSET
for award in data.awards.values():
    # TEMP: The "award for trapping a pinata" is ignored for now
    if (
        award["activity"] == "animal_resident_arctic"
        or award["activity"] == "animal_resident_desert"
    ):
        continue

    # Determine the Activity we're getting an award for
    activity_name = None
    for activity in data.activity_map:
        if activity in award["activity"]:
            activity_name = data.activity_map[activity]["display_name"]
            break

    # If the activity is invalid/unknown, then skip this award
    if activity_name is None:
        continue

    # TEMP: We're going to ignore non-pinata checks for now!
    if "animal" not in activity_name:
        continue

    # Find the target for the Award
    target = None
    for pinata in data.species_map:
        if award["subject"] == pinata:
            target = data.species_map[pinata]["display_name"]
            break

    # TEMP: Ignore subjects we can't find in the species map
    if target is None:
        continue

    # Build the check
    award_locations[f"{target} ({activity_name})"] = id

    # Enumerate the ID every check
    id += 1

# Collection of all Locations
all_locations = {**rank_locations, **award_locations}

# endregion

# region Location Helpers


def get_rank_locations(goal_level: int) -> dict[str, int]:
    """Return only Gardener Level checks up to the goal."""

    # If the gardener level is invalid, don't return any locations
    if goal_level < MIN_GARDENER_LEVEL or goal_level > MAX_GARDENER_LEVEL:
        return {}

    return {
        f"Reach Gardener Level {level}": rank_locations[f"Reach Gardener Level {level}"]
        for level in range(MIN_GARDENER_LEVEL, goal_level + 1)
    }


# endregion
