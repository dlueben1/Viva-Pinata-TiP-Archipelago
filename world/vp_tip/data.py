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

# Build a collection of user-facing Pinata names
pinata_names = {species["display_name"] for species in species_map.values()}

# Names of Legendary Pinatas (lack many checks)
legendary_pinata_names = {"Dragonache", "Choclodocus"}

# Maps internal activity substring to it's display name
activity_map = load_csv("activity_map.csv", "internal_name")

# Collection of in-game awards
awards = load_csv("awards.csv", "native_event_award_id")

# All items offered by Paper Pets (why I didn't do this for Lottie's idk, but I'm not unnecessarily revisiting that step lol)
catalog_paperpets = load_csv("paperpets_catalog.csv", "display_name")
