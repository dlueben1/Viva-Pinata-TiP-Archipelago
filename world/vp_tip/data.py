import csv
from pathlib import Path

# Get our path to the data directory
DATA_DIR = Path(__file__).resolve().parent / "data"


def load_csv(filename, key_column):
    """Load a CSV file from the data folder and return a dictionary keyed by the specified column."""
    with open(DATA_DIR / filename, newline="", encoding="utf-8-sig") as f:
        return {row[key_column]: row for row in csv.DictReader(f)}


# Maps internal species name to their display name
species_map = load_csv("species_map.csv", "internal_name")

# Maps internal activity substring to it's display name
activity_map = load_csv("activity_map.csv", "internal_name")

# Collection of in-game awards
awards = load_csv("awards.csv", "native_event_award_id")
