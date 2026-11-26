using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualBasic.FileIO;
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

        /// <summary>Maps Paper Pets AP item names to their native accessory definitions.</summary>
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

                var itemName = cols[2];
                var lottieItem = new VivaShopItem
                {
                    InternalName = cols[3],
                    DisplayName = itemName,
                    NativeTag = uint.Parse(cols[4]),
                    SourceOrdinal = uint.Parse(cols[5]),
                    ItemClass = uint.Parse(cols[19]),
                    LevelThreshold = uint.Parse(cols[8]),
                };
                ShopLottieLookupTable[itemName] = lottieItem;
            }

            ShopPaperPetsLookupTable = LoadAccessories();

            // Build the Award data
            Awards = LoadAwards();
        }

        private static Dictionary<string, VivaAccessoryItem> LoadAccessories()
        {
            const string filename = "paperpets_catalog.csv";
            var accessories = new Dictionary<string, VivaAccessoryItem>(StringComparer.Ordinal);
            var tags = new HashSet<uint>();
            using var reader = new StringReader(EmbeddedCsvReader.Read(filename));
            using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited };
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = false;

            string[] headers = parser.ReadFields()
                ?? throw new InvalidDataException($"{filename} has no header.");
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < headers.Length; i++)
            {
                if (!columns.TryAdd(headers[i], i))
                    throw new InvalidDataException($"Duplicate column '{headers[i]}' in {filename}.");
            }

            string[] requiredColumns =
            {
                "ap_control", "display_name", "internal_name", "native_tag", "source_ordinal",
                "native_class", "unlock_level", "expected_pending_mask", "unlock_type"
            };
            foreach (string column in requiredColumns)
            {
                if (!columns.ContainsKey(column))
                    throw new InvalidDataException($"Missing column '{column}' in {filename}.");
            }

            while (!parser.EndOfData)
            {
                long line = parser.LineNumber;
                string[] fields = parser.ReadFields()
                    ?? throw new InvalidDataException($"Missing row at {filename}:{line}.");
                if (fields.Length != headers.Length)
                    throw new InvalidDataException($"Invalid column count at {filename}:{line}.");

                string Value(string column) => fields[columns[column]];
                string Required(string column) => !string.IsNullOrWhiteSpace(Value(column))
                    ? Value(column)
                    : throw new InvalidDataException($"Missing '{column}' at {filename}:{line}.");
                uint Number(string column) => uint.TryParse(
                    Required(column),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out uint value
                ) ? value : throw new InvalidDataException($"Invalid '{column}' at {filename}:{line}.");

                string controlled = Required("ap_control");
                if (controlled == "0")
                    continue;
                if (controlled != "1")
                    throw new InvalidDataException($"Invalid 'ap_control' at {filename}:{line}.");

                var accessory = new VivaAccessoryItem
                {
                    DisplayName = Required("display_name"),
                    InternalName = Required("internal_name"),
                    NativeTag = Number("native_tag"),
                    SourceOrdinal = Number("source_ordinal"),
                    ItemClass = Number("native_class"),
                    LevelThreshold = Number("unlock_level"),
                    ExpectedPendingMask = Number("expected_pending_mask"),
                };
                if (accessory.NativeTag == 0 || accessory.ItemClass != 4 ||
                    accessory.SourceOrdinal >= 629 || accessory.LevelThreshold <= 1 ||
                    accessory.ExpectedPendingMask != 0x2 || Value("unlock_type") != "level")
                    throw new InvalidDataException($"Invalid accessory metadata at {filename}:{line}.");
                if (!tags.Add(accessory.NativeTag) || !accessories.TryAdd(accessory.DisplayName, accessory))
                    throw new InvalidDataException($"Duplicate accessory name or tag at {filename}:{line}.");
            }

            if (accessories.Count == 0)
                throw new InvalidDataException($"No AP-controlled accessories in {filename}.");
            return accessories;
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
