using System;
using System.Collections.Generic;
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

    [EzIPC]
    private Func<string, List<StatsRecording>>? GetStatsIpc;

    [EzIPC]
    private Func<Dictionary<string, string>>? GetArchivesIpc;

    public IReadOnlyDictionary<string, string> GetArchives()
    {
        return GetArchivesIpc?.Invoke() ?? new Dictionary<string, string>();
    }

    public IReadOnlyList<StatsRecording> GetStats(string archiveId)
    {
        return GetStatsIpc?.Invoke(archiveId) ?? [];
    }

    [EzIPCEvent]
    public void OnGameFinished()
    {
        Log.Information("OnGameFinished called in SimpleBlackjackIpc.");
    }

    [EzIPCEvent]
    public void OnGameFinishedEx(StatsRecording stats)
    {
        Log.Information("OnGameFinished called in SimpleBlackjackIpc EX.");
        Log.Information(stats.Time.ToString());
        onRoundCompleted(stats);
    }
}
