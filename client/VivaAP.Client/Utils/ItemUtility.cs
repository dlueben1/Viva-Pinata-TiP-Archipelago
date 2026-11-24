using System;
using System.Collections.Generic;
using System.Text;
using Archipelago.MultiClient.Net.Models;

namespace VivaAP.Client.Utils
{
    /// <summary>
    /// The category of a given AP Item, where the ID of the enum value is the base ID for that category.
    ///
    /// One should assume that every ID between an enum value and the next one is within that same category.
    /// That said, there will be blanks in the enum, as not every ID is used for an item.
    /// </summary>
    public enum APItemType : long
    {
        Coin = 1,
        House = 100,
        GardenItem = 200,
        Produce = 300,
        Candy = 320
    }

    public static class ItemUtility
    {
        /// <summary>
        /// Determines what type of Archipelago Item we've received
        /// </summary>
        public static APItemType? ClassifyItem(ItemInfo item)
        {
            APItemType? result = null;

            foreach (APItemType type in Enum.GetValues<APItemType>())
            {
                if ((long)type > item.ItemId)
                    break;

                result = type;
            }

            return result;
        }
    }
}
