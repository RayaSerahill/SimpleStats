using System;
using System.Threading.Tasks;
using ECommons.EzIpcManager;
using ECommons.Logging;

namespace sbjStats;

public sealed class SimpleAviatorIpc
{
    private readonly Action<string, long> onRoundEnded;

    public SimpleAviatorIpc(Action<string, long> onRoundEnded)
    {
        PluginLog.Information("SimpleAviatorIpc constructor called.");
        this.onRoundEnded = onRoundEnded;
        EzIPC.Init(this, "SimpleAviator");
        PluginLog.Information("EzIPC.Init called for SimpleAviatorIpc.");
    }

    [EzIPC] private Func<Task<string>>? GetStatsIPC;
    [EzIPC] private Func<Task<string>>? GetArchiveIPC;
    [EzIPC] private Func<string, Task<string>>? GetGameArchiveIPC;

    public async Task<string> GetStatsAsync()
    {
        if (GetStatsIPC is null)
        {
            PluginLog.Warning("SimpleAviator GetStatsIPC is not available.");
            return string.Empty;
        }

        return await GetStatsIPC();
    }

    public async Task<string> GetArchiveAsync()
    {
        if (GetArchiveIPC is null)
        {
            PluginLog.Warning("SimpleAviator GetArchiveIPC is not available.");
            return string.Empty;
        }

        return await GetArchiveIPC();
    }

    public async Task<string> GetGameArchiveAsync(string gameId)
    {
        if (GetGameArchiveIPC is null)
        {
            PluginLog.Warning("SimpleAviator GetGameArchiveIPC is not available.");
            return string.Empty;
        }

        return await GetGameArchiveIPC(gameId);
    }

    [EzIPCEvent("RoundEnded")]
    private void OnRoundEnded(string json)
    {
        DuoLog.Information($"[AviatorRoundEnded] {json}");

        var archivedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        onRoundEnded(json, archivedAtUnixSeconds);
    }
}
