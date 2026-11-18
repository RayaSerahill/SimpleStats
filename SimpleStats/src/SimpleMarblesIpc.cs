using System;
using System.Threading.Tasks;
using ECommons.EzIpcManager;
using ECommons.Logging;

namespace sbjStats;

public sealed class SimpleMarblesIpc
{
    private readonly Action<string, long> onRaceEnded;

    public SimpleMarblesIpc(Action<string, long> onRaceEnded)
    {
        PluginLog.Information("SimpleMarblesIpc constructor called.");
        this.onRaceEnded = onRaceEnded;
        EzIPC.Init(this, "SimpleMarbles");
        PluginLog.Information("EzIPC.Init called for SimpleMarblesIpc.");
    }

    // GetArchiveIPC is confirmed against a real payload. The other two follow SimpleAviator's
    // documented gates: async, JSON handed over as a string, and game ids are strings ("a46498").
    [EzIPC] private Func<Task<string>>? GetStatsIPC;
    [EzIPC] private Func<Task<string>>? GetArchiveIPC;
    [EzIPC] private Func<string, Task<string>>? GetGameArchiveIPC;

    /// <summary>Fetches the dealer's stats as JSON.</summary>
    public async Task<string> GetStatsAsync()
    {
        if (GetStatsIPC is null)
        {
            PluginLog.Warning("SimpleMarbles GetStats IPC is not available.");
            return string.Empty;
        }

        return await GetStatsIPC();
    }

    /// <summary>Fetches the dealer's game archive as JSON.</summary>
    public async Task<string> GetArchiveAsync()
    {
        if (GetArchiveIPC is null)
        {
            PluginLog.Warning("SimpleMarbles GetArchive IPC is not available.");
            return string.Empty;
        }

        return await GetArchiveIPC();
    }

    /// <summary>Fetches one game's full detail (races and per-player results) as JSON.</summary>
    public async Task<string> GetGameArchiveAsync(string gameId)
    {
        if (GetGameArchiveIPC is null)
        {
            PluginLog.Warning("SimpleMarbles GetGameArchive IPC is not available.");
            return string.Empty;
        }

        return await GetGameArchiveIPC(gameId);
    }

    // Unlike its siblings' GameEndedIPC, SimpleMarbles names this event without the IPC suffix.
    [EzIPCEvent("RaceEnded")]
    private void OnRaceEnded(string json)
    {
        PluginLog.Information($"[SimpleMarbles RaceEnded] {json}");

        var archivedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        onRaceEnded(json, archivedAtUnixSeconds);
    }
}
