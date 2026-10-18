from BaseClasses import Location, LocationProgressType

# Instance Data for TiP Locations
class TIPLocation(Location):
    game = "Viva Piñata: Trouble in Paradise"

# Gardener Level/Rank Locations
rank_locations = {
    "Gardener Level 2": 2,
    "Gardener Level 3": 3,
    "Gardener Level 4": 4,
    "Gardener Level 5": 5,
    "Gardener Level 6": 6,
    "Gardener Level 7": 7,
    "Gardener Level 8": 8,
    "Gardener Level 9": 9,
    "Gardener Level 10": 10
}

# Collection of all Locations
all_locations = {**rank_locations}