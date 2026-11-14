from dataclasses import dataclass
from constants import MIN_GARDENER_LEVEL, MAX_GARDENER_LEVEL
from Options import OptionDict, OptionError, Choice, Range, Option, OptionGroup, Toggle, DeathLink, DefaultOnToggle, PerGameCommonOptions
from BaseClasses import PlandoOptions
from worlds.AutoWorld import World
import numbers

#region Goal Options

# Eventually, when there's more than one goal, might be better to make this a GoalDict
# Shoutout to the DK64 Archipelago which gave me a good template to work with on this kind of option
class Goals(OptionDict):
    """Determines your clear condition(s) for this Multiworld session.
    
    Valid Keys/Options:
    - reach_gardener_level: If non-zero, reaching the specified gardener level is required to clear the session. Valid values are 2 - 196, inclusive.
    """
    display_name = "Goals"

    # The minimum value for any goal option (not including 0, which is always an option, and represents an "unused" goal) 
    min_values_dict: dict[str, int] = {
        "reach_gardener_level": MIN_GARDENER_LEVEL
    }

    # The maximum value for each goal option
    max_values_dict: dict[str, int] = {
        "reach_gardener_level": MAX_GARDENER_LEVEL
    }

    # The default/recommended goal(s)
    default = {"reach_gardener_level": 30}

    # Validator for Goals
    def verify(self, world: type[World], player_name: str, plando_options: PlandoOptions) -> None:
        super(Goals, self).verify(world, player_name, plando_options)

        # Instead of failing on one error and hiding others, which takes multiple generations, collect all
        all_errors = []

        # Validate each goal
        for key, value in self.value.items():
            # Minimum allowed value for this goal
            min = self.min_values_dict[key]

            # Maximum allowed value for this goal
            max = self.max_values_dict[key]

            # Check if it's a valid number
            if isinstance(value, numbers.Integral):
                value = int(value)
                if value > max:
                    all_errors.append(f"{key}: {value} is higher than the goal's maximum allowed value ({max})")
                elif value < min  and value > 0:
                    all_errors.append(f"{key}: {value} is lower than the goal's minimum allowed value ({min}). Use 0 if the goal should not be used.")
            # It's not a valid number
            else:
                all_errors.append(f"{key}: {value} is not a valid value. Please use a number between {min} and {max}, or 0 if this goal should not be used.")
        
        # Return all errors
        if all_errors:
            raise OptionError("Found error(s) with option 'Goals':\n" + "\n".join(all_errors))

#endregion