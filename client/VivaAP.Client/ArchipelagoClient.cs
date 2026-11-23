using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using VivaAP.Client.Interop;
using VivaAP.Client.Utils;

namespace VivaAP.Client
{
    public static class ArchipelagoClient
    {
        /// <summary>
        /// Lock so that we only process one Archipelago message at a time
        /// </summary>
        private static readonly object _lock = new();

        public static string? Host { get; set; }
        public static int? Port { get; set; }
        public static string? Slot { get; set; }
        public static string? Seed { get; set; }
        public static string? Password { get; set; }

        /// <summary>
        /// The Archipelago Session
        /// </summary>
        public static ArchipelagoSession? Session { get; private set; }

        static ArchipelagoClient()
        {
            // Grab all the environmental variables that are passed in from the patch file
            Host = Environment.GetEnvironmentVariable("AP_SERVER");
            Port = int.TryParse(Environment.GetEnvironmentVariable("AP_PORT"), out var port)
                ? port
                : null;
            Slot = Environment.GetEnvironmentVariable("AP_SLOT");
            Seed = Environment.GetEnvironmentVariable("AP_SEED");
            Password = Environment.GetEnvironmentVariable("AP_PASSWORD");
        }

        /// <summary>
        /// Connect to the Archipelago Server as a specific slot.
        /// Technically, all of this info is stored inside the client from the environment variables,
        /// but I'll leave it with parameters for now in case I want to adjust the connection logic later.
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

                // Attach the Native Event Handler before connecting, because items can flood in
                NativeStateSynchronizer.Start();

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
