import csv
from io import StringIO
from importlib.resources import files
from pathlib import Path


def load_csv(filename, key_column):
    resource = files(__package__).joinpath("data", filename)
    content = resource.read_text(encoding="utf-8-sig")

    return {row[key_column]: row for row in csv.DictReader(StringIO(content))}


# Maps internal species name to their display name
species_map = load_csv("species_map.csv", "internal_name")

# Maps internal activity substring to it's display name
activity_map = load_csv("activity_map.csv", "internal_name")

# Collection of in-game awards
awards = load_csv("awards.csv", "native_event_award_id")
