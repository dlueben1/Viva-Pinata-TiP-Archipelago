using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using VivaAP.Client.Utils;

namespace VivaAP.Client.Interop
{
    /// <summary>
    /// Publishes state to the Native side of the Mod, so that when it handles hooks that it can't pass to the Managed side, it can still know what state the game is in.
    /// Used for knowing what items are available to the player.
    /// </summary>
    public static class NativeStateSynchronizer
    {
        #region Native Functions

        [DllImport(
            InteropConstants.NativeDLL,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "VivaApHouseRegister"
        )]
        private static extern int RegisterNative(
            uint houseBlockTag,
            uint sourceOrdinal,
            uint residentSpeciesTag
        );

        [DllImport(
            InteropConstants.NativeDLL,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "VivaApLottieRegister"
        )]
        private static extern int RegisterLottieNative(
            uint tag,
            uint sourceOrdinal,
            uint itemClass,
            uint levelThreshold
        );

        [DllImport(
            InteropConstants.NativeDLL,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "VivaApHousePrepare"
        )]
        private static extern int PrepareNative();

        [DllImport(
            InteropConstants.NativeDLL,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "VivaApItemSetOwned"
        )]
        private static extern int SetOwnedNative(uint tag, int owned);

        [DllImport(
            InteropConstants.NativeDLL,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "VivaApHouseTakeDiagnostics"
        )]
        private static extern uint TakeDiagnosticsNative();

        [DllImport(
            InteropConstants.NativeDLL,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "VivaApHouseEnableEarlyGrants"
        )]
        private static extern int EnableEarlyGrantsNative(int enabled);

        #endregion

        #region Initialization

        public static void Start()
        {
            // Register all Items with the native side, so it can start tracking them
            RegisterBlueprints();
            RegisterLottiesItems();

            // Prepare the native side to start tracking state
            var result = PrepareNative();
            if (result != 1)
            {
                throw new InvalidOperationException("Failed to prepare native item hooks.");
            }

            // Listen for Archipelago Items
            ArchipelagoClient.Session!.Items.ItemReceived += OnItemReceived;
        }

        private static void RegisterBlueprints()
        {
            foreach (
                var item in GameUtility.BlueprintLookupTable.DistinctBy(x => x.Value.HouseblockTag)
            )
            {
                var blueprint = item.Value;
                var result = RegisterNative(
                    blueprint.HouseblockTag,
                    blueprint.SourceOrdinal,
                    blueprint.UnlockQueueTriggerTag
                );
                if (result != 1)
                {
                    throw new InvalidOperationException(
                        $"Failed to register {blueprint.DisplayName}"
                    );
                }
            }
        }

        private static void RegisterLottiesItems()
        {
            foreach (var item in GameUtility.ShopLottieLookupTable)
            {
                var itemData = item.Value;
                var result = RegisterLottieNative(
                    itemData.NativeTag,
                    itemData.SourceOrdinal,
                    itemData.ItemClass,
                    itemData.LevelThreshold
                );
                if (result != 1)
                {
                    throw new InvalidOperationException(
                        $"Failed to register {itemData.DisplayName}"
                    );
                }
            }
        }

        #endregion

        #region Handle AP Items

        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            // Determine what category the item is from
            ItemInfo item = helper.PeekItem();
            APItemType? category = ItemUtility.ClassifyItem(item);

            // If somehow we don't know what category this item is, we can't handle it
            if (category == null)
            {
                LogUtility.Error(
                    $"Item '{item.ItemDisplayName}' (ID: {item.ItemId}) is not within the range of any known Archipelago Item Type."
                );
            }

            // Handle Blueprints/Houses
            switch (category)
            {
                case APItemType.House:
                {
                    // Grab the blueprint data
                    const string suffix = " Home Blueprints";

                    if (!item.ItemDisplayName.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        LogUtility.Error($"Unexpected blueprint: {item.ItemDisplayName}");
                        helper.DequeueItem();
                        return;
                    }

                    string pinata = item.ItemDisplayName[..^suffix.Length];

                    // Simple fix for the Flutterscotch shared home name
                    if (pinata == "Flutterscotch")
                        pinata = "White Flutterscotch";

                    var blueprint = GameUtility.BlueprintLookupTable.TryGetValue(pinata, out var bp)
                        ? bp
                        : null;

                    if (blueprint != null)
                    {
                        // Unlock the house on the native side
                        SetShopItemOwned(blueprint.HouseblockTag, true);
                    }
                    else
                    {
                        // We had a problem with getting the blueprint data, so we can't handle this item
                        LogUtility.Error(
                            $"Could not find blueprint data for house item '{item.ItemDisplayName}' (ID: {item.ItemId})."
                        );
                    }
                    break;
                }
                case APItemType.GardenItem:
                case APItemType.Produce:
                case APItemType.Candy:
                {
                    var itemData = GameUtility.ShopLottieLookupTable.TryGetValue(
                        item.ItemDisplayName,
                        out var lottie
                    )
                        ? lottie
                        : null;

                    if (itemData != null)
                        SetShopItemOwned(itemData.NativeTag, true);
                    else
                        LogUtility.Error(
                            $"Could not find Lottie Shop ItemData for item '{item.ItemDisplayName}' (ID: {item.ItemId})."
                        );
                    break;
                }
            }

            // Finally, dequeue the item since we've successfully handled it
            helper.DequeueItem();
        }

        #endregion

        #region API - Lottie Items

        /// <summary>
        /// Registers a verified, ordinary Lottie item before Start prepares the hooks.
        /// Its source row must be level-only (queue 1, pending mask 0x2); pass
        /// the retail class, source ordinal and original level threshold.
        /// </summary>
        public static void RegisterLottieItem(
            uint tag,
            uint sourceOrdinal,
            uint itemClass,
            uint levelThreshold
        )
        {
            if (RegisterLottieNative(tag, sourceOrdinal, itemClass, levelThreshold) != 1)
                throw new InvalidOperationException($"Failed to register Lottie item {tag}.");
        }

        /// <summary>Publishes independent AP ownership for any registered shop item.</summary>
        public static void SetShopItemOwned(uint tag, bool owned)
        {
            if (SetOwnedNative(tag, owned ? 1 : 0) != 1)
                throw new InvalidOperationException($"Shop item {tag} is not registered.");
        }

        #endregion

        #region Cleanup

        public static void Stop()
        {
            if (ArchipelagoClient.Session != null)
            {
                ArchipelagoClient.Session.Items.ItemReceived -= OnItemReceived;
            }
        }

        #endregion
    }
}
