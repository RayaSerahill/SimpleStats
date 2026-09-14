using System;
using System.Collections.Generic;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons.EzIpcManager;
using ECommons.Logging;
using Serilog;

namespace sbjStats;

public sealed class SimpleBlackjackIpc
{
    private readonly Action<StatsRecording> onRoundCompleted;

    public SimpleBlackjackIpc(Action<StatsRecording> onRoundCompleted)
    {
        PluginLog.Information("SimpleBlackjackIpc constructor called.");
        this.onRoundCompleted = onRoundCompleted;
        EzIPC.Init(this, "SimpleBlackjack");
        PluginLog.Information("EzIPC.Init called for SimpleBlackjack.");
    }

    // Subscribed as object so Dalamud hands over SimpleBlackjack's payload as JSON instead of
    // converting it by member name; see SimpleBlackjackPayloadConverter for why.
    [EzIPC("GetStats")]
    private Func<string, object>? GetStatsIpc;

    [EzIPC("GetArchives")]
    private Func<Dictionary<string, string>>? GetArchivesIpc;

    public IReadOnlyDictionary<string, string> GetArchives()
    {
        return InvokeFirstReady(
            "GetArchives",
            new (string IpcName, Func<IReadOnlyDictionary<string, string>>? Invoke)[]
            {
                ("GetArchives", GetArchivesIpc is null ? null : () => GetArchivesIpc.Invoke()),
            },
            new Dictionary<string, string>());
    }

    public IReadOnlyList<StatsRecording> GetStats(string archiveId)
    {
        return InvokeFirstReady(
            "GetStats",
            new (string IpcName, Func<IReadOnlyList<StatsRecording>>? Invoke)[]
            {
                ("GetStats", GetStatsIpc is null
                    ? null
                    : () => SimpleBlackjackPayloadConverter.ToRecordings(GetStatsIpc.Invoke(archiveId), $"GetStats({archiveId})")),
            },
            []);
    }

    private static T InvokeFirstReady<T>(
        string operationName,
        IEnumerable<(string IpcName, Func<T>? Invoke)> candidates,
        T unavailableValue)
    {
        IpcNotReadyError? lastNotReady = null;

        foreach (var (ipcName, invoke) in candidates)
        {
            if (invoke is null)
                continue;

            try
            {
                var result = invoke();
                PluginLog.Information($"SimpleBlackjack {operationName} IPC succeeded via {ipcName}.");
                return result;
            }
            catch (IpcNotReadyError ex)
            {
                lastNotReady = ex;
                PluginLog.Warning($"SimpleBlackjack {operationName} IPC candidate {ipcName} is not ready: {ex.Message}");
            }
        }

        if (lastNotReady is not null)
            throw lastNotReady;

        PluginLog.Warning($"SimpleBlackjack {operationName} IPC is not available.");
        return unavailableValue;
    }

    [EzIPCEvent]
    public void OnGameFinished()
    {
        Log.Information("OnGameFinished called in SimpleBlackjackIpc.");
    }

    [EzIPCEvent]
    public void OnGameFinishedEx(object payload)
    {
        Log.Information("OnGameFinished called in SimpleBlackjackIpc EX.");
        var stats = SimpleBlackjackPayloadConverter.ToRecording(payload, "OnGameFinishedEx");
        Log.Information(stats.Time.ToString());
        onRoundCompleted(stats);
    }
}
