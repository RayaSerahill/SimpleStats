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

    // Only the names of SimpleMarbles' gates are known. Their signatures are assumed to match
    // SimpleScratch and SimpleWheel: async, JSON handed over as a string, integer game ids.
    [EzIPC] private Func<Task<string>>? GetStatsIPC;
    [EzIPC] private Func<Task<string>>? GetArchiveIPC;
    [EzIPC] private Func<int, Task<string>>? GetGameArchiveIPC;

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

    /// <summary>Fetches the full archive of one game as JSON.</summary>
    public async Task<string> GetGameArchiveAsync(int gameId)
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
