using System;
using System.Threading.Tasks;
using ECommons.EzIpcManager;
using ECommons.Logging;

namespace sbjStats;

public sealed class SimpleWheelIpc
{
    /// <summary>The largest limit SimpleWheel accepts on GetArchiveLimitIPC.</summary>
    public const int MaxArchiveLimit = 20000;

    private readonly Action<string, long> onGameEnded;

    public SimpleWheelIpc(Action<string, long> onGameEnded)
    {
        PluginLog.Information("SimpleWheelIpc constructor called.");
        this.onGameEnded = onGameEnded;
        EzIPC.Init(this, "SimpleWheel");
        PluginLog.Information("EzIPC.Init called for SimpleWheelIpc.");
    }

    [EzIPC] private Func<Task<string>>? GetArchiveIPC;
    [EzIPC] private Func<int, Task<string>>? GetArchiveLimitIPC;

    /// <summary>
    /// Fetches the entire archive as a JSON array, newest first.
    /// Asks the limited gate for the maximum SimpleWheel allows and falls back to the default 500-game window
    /// if the limited gate is not available.
    /// </summary>
    public async Task<string> GetFullArchiveAsync()
    {
        if (GetArchiveLimitIPC is not null)
            return await GetArchiveLimitIPC(MaxArchiveLimit);

        PluginLog.Warning("SimpleWheel GetArchiveLimit IPC is not available, falling back to the default archive window.");

        if (GetArchiveIPC is null)
        {
            PluginLog.Warning("SimpleWheel GetArchive IPC is not available.");
            return string.Empty;
        }

        return await GetArchiveIPC();
    }

    [EzIPCEvent("GameEndedIPC")]
    private void OnGameEnded(string json)
    {
        PluginLog.Information($"[SimpleWheel GameEnded] {json}");

        var archivedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        onGameEnded(json, archivedAtUnixSeconds);
    }
}
