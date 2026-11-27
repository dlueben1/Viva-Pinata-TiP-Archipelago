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
        /// Maps a Pinata's name to their home blueprints
        /// </summary>
        public static Dictionary<string, VivaBlueprints> BlueprintLookupTable { get; }

        /// <summary>
        /// Maps an Item's Display Name to their data
        /// </summary>
        public static Dictionary<string, VivaShopItem> ShopLottieLookupTable { get; }

        /// <summary>
        /// Maps a Paper Pets item's display name to its accessory data
        /// </summary>
        public static Dictionary<string, VivaAccessoryItem> ShopPaperPetsLookupTable { get; }

        /// <summary>
        /// Gets or sets the collection of Viva awards.
        /// </summary>
        public static VivaAward[] Awards { get; }

        static GameUtility()
        {
            // Build the simple lookup tables from the embedded CSVs
            SpeciesLookupTable = LoadInternalNameLookupTable("species_map.csv");
            ActivityLookupTable = LoadInternalNameLookupTable("activity_map.csv");

            // Build the Blueprint data
            BlueprintLookupTable = new Dictionary<string, VivaBlueprints>();
            var blueprintCsv = EmbeddedCsvReader.Read("blueprint_map.csv");
            var lines = blueprintCsv.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );
            for (int i = 1; i < lines.Length; i++)
            {
                var row = lines[i];

                // Ignore legendary pinatas
                if (row.Contains("Dragonache") || row.Contains("Choclodocus"))
                    continue;

                var cols = row.Split(',');
                var pinataName = cols[1];
                var blueprint = new VivaBlueprints
                {
                    DisplayName = $"{pinataName} Home Blueprints",
                    HouseblockTag = uint.Parse(cols[4]),
                    SourceOrdinal = uint.Parse(cols[5]),
                    ResidentSpeciesTag = uint.Parse(cols[6]),
                    UnlockQueueTriggerTag = uint.Parse(cols[7]),
                };
                BlueprintLookupTable[pinataName] = blueprint;
            }

            // Build Lottie's Data
            ShopLottieLookupTable = new Dictionary<string, VivaShopItem>();
            var lottieCsv = EmbeddedCsvReader.Read("lottie_catalog.csv");
            lines = lottieCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++)
            {
                var row = lines[i];
                var cols = row.Split(',');

                // If this item isn't in the itempool, ignore it
                if (!cols[0].Equals("1"))
                    continue;

                // Seeds need additional native registration support and are not
                // part of the ordinary Lottie lookup yet.
                if (cols[2].Equals("Seed"))
                    continue;

                var itemName = cols[3];
                var lottieItem = new VivaShopItem
                {
                    InternalName = cols[4],
                    DisplayName = itemName,
                    NativeTag = uint.Parse(cols[5]),
                    SourceOrdinal = uint.Parse(cols[6]),
                    ItemClass = uint.Parse(cols[20]),
                    LevelThreshold = uint.Parse(cols[9]),
                };
                ShopLottieLookupTable[itemName] = lottieItem;
            }

            // Build Paper Pets' data using its own catalog schema
            ShopPaperPetsLookupTable = new Dictionary<string, VivaAccessoryItem>();
            var accessoryTags = new HashSet<uint>();
            var paperPetsCsv = EmbeddedCsvReader.Read("paperpets_catalog.csv");
            lines = paperPetsCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length != 24 || (cols[0] != "0" && cols[0] != "1"))
                    throw new InvalidDataException($"Invalid Paper Pets catalog row {i + 1}.");

                // If this item isn't in the itempool, ignore it
                if (!cols[0].Equals("1"))
                    continue;

                var accessory = new VivaAccessoryItem
                {
                    InternalName = cols[3],
                    DisplayName = cols[2],
                    NativeTag = uint.Parse(cols[4]),
                    SourceOrdinal = uint.Parse(cols[5]),
                    ItemClass = uint.Parse(cols[19]),
                    LevelThreshold = uint.Parse(cols[8]),
                    ExpectedPendingMask = uint.Parse(cols[10]),
                };
                if (string.IsNullOrWhiteSpace(accessory.DisplayName) ||
                    string.IsNullOrWhiteSpace(accessory.InternalName) ||
                    accessory.NativeTag == 0 || accessory.ItemClass != 4 ||
                    accessory.SourceOrdinal >= 629 || accessory.LevelThreshold <= 1 ||
                    accessory.ExpectedPendingMask != 0x2 || cols[7] != "level")
                    throw new InvalidDataException($"Invalid accessory metadata at row {i + 1}.");
                if (!accessoryTags.Add(accessory.NativeTag) ||
                    !ShopPaperPetsLookupTable.TryAdd(accessory.DisplayName, accessory))
                    throw new InvalidDataException($"Duplicate accessory name or tag at row {i + 1}.");
            }
            if (ShopPaperPetsLookupTable.Count == 0)
                throw new InvalidDataException("No AP-controlled accessories in the Paper Pets catalog.");

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
