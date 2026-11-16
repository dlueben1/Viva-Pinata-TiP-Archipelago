using System;
using System.Collections.Generic;
using System.Text;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using VivaAP.Client.Utils;
using static System.Collections.Specialized.BitVector32;

namespace VivaAP.Client
{
    public static class ArchipelagoClient
    {
        /// <summary>
        /// Lock so that we only process one Archipelago message at a time
        /// </summary>
        private static readonly object _lock = new();

        /// <summary>
        /// The Archipelago Session
        /// </summary>
        public static ArchipelagoSession? Session { get; private set; }

        /// <summary>
        /// Connect to the Archipelago Server as a specific slot
        /// </summary>
        /// <param name="host">Server IP Address/Location</param>
        /// <param name="port">Server's Port Number</param>
        /// <param name="slot">Player's Slot Name</param>
        /// <param name="password">Optional Password for the Server</param>
        /// <returns>Whether the connection succeed or not</returns>
        public static async Task<bool> ConnectAsync(
            string host,
            int port,
            string slot,
            string? password = null
        )
        {
            try
            {
                Session = ArchipelagoSessionFactory.CreateSession(host, port);

                await Session.ConnectAsync();

                var result = await Session.LoginAsync(
                    "Viva Pinata: Trouble in Paradise",
                    slot,
                    ItemsHandlingFlags.AllItems,
                    password: password
                );

                if (!result.Successful)
                {
                    LogUtility.Warn("Archipelago login failed.");
                    return false;
                }

                LogUtility.Info($"Connected to Archipelago as {slot}");
                return true;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Archipelago connection failed: {ex.Message}");
                return false;
            }
        }
    }
}
