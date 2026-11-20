using System;
using System.Collections.Generic;
using System.Text;
using VivaAP.Client.Models;

namespace VivaAP.Client.Utils
{
    public static class GameUtility
    {
        /// <summary>
        /// Maps Award Category internal codenames to the user-friendly display names
        /// </summary>
        public static Dictionary<string, string> ActivityLookupTable { get; }

        /// <summary>
        /// Maps Pinata internal codenames to the user-friendly display names
        /// </summary>
        public static Dictionary<string, string> SpeciesLookupTable { get; }

        /// <summary>
        /// Gets or sets the collection of Viva awards.
        /// </summary>
        public static VivaAward[] Awards { get; }

        static GameUtility()
        {
            // Build the lookup tables from the embedded CSVs
            SpeciesLookupTable = LoadInternalNameLookupTable("species_map.csv");
            ActivityLookupTable = LoadInternalNameLookupTable("activity_map.csv");

            // Build the Award data
            Awards = LoadAwards();
        }

        private static VivaAward[] LoadAwards()
        {
            // Load the CSV file
            var csvContent = EmbeddedCsvReader.Read("awards.csv");
            var lines = csvContent.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

            // Allocate the correct amount of space for the awards
            var awards = new VivaAward[lines.Length - 1];

            // Build each award (and skip the first row)
            for (int i = 1; i < lines.Length; i++)
            {
                var columns = lines[i].Split(',');

                // Get the shorthand version of the activity
                int cutoffIndex = columns[0].IndexOf('_', columns[0].IndexOf('_') + 1);
                string activityShort =
                    cutoffIndex != -1 ? columns[0].Substring(0, cutoffIndex) : columns[0];

                // Lookup the user-friendly name for this activity
                string activityName = ActivityLookupTable.TryGetValue(
                    activityShort,
                    out var displayName
                )
                    ? displayName
                    : activityShort;

                // Lookup the user-friendly name for the award subject
                string subjectRaw = columns[2];
                string subjectName = SpeciesLookupTable.ContainsKey(subjectRaw)
                    ? SpeciesLookupTable[subjectRaw]
                    : subjectRaw;

                // Build the award
                awards[i - 1] = new VivaAward
                {
                    EventId = columns[1].Trim(),
                    Subject = subjectName,
                    Category = activityName,
                    XP = int.TryParse(columns[3].Trim(), out int xp) ? xp : 0,
                };
            }

            // Finally, return all the awards
            return awards;
        }

        /// <summary>
        /// Builds simple lookup tables that map internal codenames to user friendly names
        /// </summary>
        private static Dictionary<string, string> LoadInternalNameLookupTable(string csvFileName)
        {
            // Setup the table
            var lookupTable = new Dictionary<string, string>();

            // Read the CSV file
            var csvContent = EmbeddedCsvReader.Read(csvFileName);
            var lines = csvContent.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

            // Skip the header line and process each subsequent line
            for (int i = 1; i < lines.Length; i++)
            {
                var columns = lines[i].Split(',');
                if (columns.Length >= 2)
                {
                    var internalName = columns[0].Trim();
                    var displayName = columns[1].Trim();
                    if (!string.IsNullOrEmpty(internalName) && !string.IsNullOrEmpty(displayName))
                    {
                        lookupTable[internalName] = displayName;
                    }
                }
            }

            // And return it
            return lookupTable;
        }
    }
}
