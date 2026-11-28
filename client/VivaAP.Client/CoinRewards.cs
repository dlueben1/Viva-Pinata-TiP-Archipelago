using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Newtonsoft.Json.Linq;
using VivaAP.Client.Patches;
using VivaAP.Client.Utils;

namespace VivaAP.Client;

internal static class CoinRewards
{
    private const string StorageKey = "vivaap_coins_granted";
    private const long TenCoins = (long)APItemType.Coin;
    private const long FiveHundredCoins = TenCoins + 1;
    private static readonly object Gate = new();
    private static (string Seed, int Team, int Slot)? scope;
    private static CancellationTokenSource? connection;
    private static long entitled;
    private static long applied;
    private static bool ready;

    internal static void Start(ArchipelagoSession session)
    {
        lock (Gate)
        {
            var slot = (session.RoomState.Seed, session.ConnectionInfo.Team, session.ConnectionInfo.Slot);
            if (scope != slot)
            {
                applied = 0;
                scope = slot;
            }

            var cancel = new CancellationTokenSource();
            connection = cancel;
            _ = Task.Run(() => SynchronizeAsync(session, cancel.Token));
        }
    }

    internal static void Disconnect()
    {
        lock (Gate)
        {
            ready = false;
            entitled = 0;
            connection?.Cancel();
            connection = null;
        }
    }

    internal static void TryGrant(PatchContext context, uint controller)
    {
        // Skip this tick if the AP worker is publishing a snapshot.
        if (!Monitor.TryEnter(Gate))
            return;

        try
        {
            if (ready && entitled > applied)
                applied += Patch_CoinRewards.Grant(context, controller, entitled - applied);
        }
        finally
        {
            Monitor.Exit(Gate);
        }
    }

    private static async Task SynchronizeAsync(ArchipelagoSession session, CancellationToken cancel)
    {
        IReadOnlyList<ItemInfo>? previousHistory = null;
        long historyTotal = 0;
        long? stored = null;
        bool reportedFailure = false;

        while (!cancel.IsCancellationRequested && session.Socket.Connected)
        {
            try
            {
                // Max(0) initializes a missing key and returns the existing total otherwise.
                stored ??= await StoreAtLeastAsync(session, 0, cancel);
                var history = session.Items.AllItemsReceived;
                if (!ReferenceEquals(history, previousHistory))
                {
                    historyTotal = 0;
                    // The library reconciles packets by index and exposes each receipt at its list position.
                    for (int index = 0; index < history.Count; index++)
                        historyTotal += history[index].ItemId switch
                        {
                            TenCoins => 10,
                            FiveHundredCoins => 500,
                            _ => 0
                        };
                    previousHistory = history;
                }

                long pending;
                lock (Gate)
                {
                    if (cancel.IsCancellationRequested)
                        return;

                    applied = Math.Max(applied, stored.Value);
                    entitled = historyTotal;
                    ready = true;
                    pending = applied;
                }

                if (pending > stored.Value)
                    stored = await StoreAtLeastAsync(session, pending, cancel);
                reportedFailure = false;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!reportedFailure)
                    LogUtility.Warn($"Archipelago coin counter synchronization will retry: {ex.Message}");
                reportedFailure = true;
            }

            await Task.Delay(reportedFailure ? 1000 : 200);
        }

        lock (Gate)
        {
            if (!cancel.IsCancellationRequested)
                ready = false;
        }
    }

    private static async Task<long> StoreAtLeastAsync(ArchipelagoSession session, long total, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var reply = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var counter = session.DataStorage[Scope.Slot, StorageKey];
        session.DataStorage[Scope.Slot, StorageKey] = counter + Operation.Max(total)
            + Callback.Add((_, value, _) => reply.TrySetResult(value));
        var value = await reply.Task.WaitAsync(TimeSpan.FromSeconds(5), cancel);
        if (value.Type != JTokenType.Integer || (long)value < total)
            throw new InvalidOperationException("The server did not confirm the cumulative coin total.");
        return (long)value;
    }
}
