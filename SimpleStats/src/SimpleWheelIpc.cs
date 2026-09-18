using System;
using System.Threading.Tasks;
using ECommons.EzIpcManager;
using ECommons.Logging;

namespace sbjStats;

public sealed class SimpleWheelIpc
{
    public const int DefaultArchiveLimit = 500;
    public const int MinArchiveLimit = 1;
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
    /// Fetches the most recent archived wheel games as a JSON array, newest first.
    /// Uses the limited gate when a limit is supplied, otherwise SimpleWheel's default window of 500 games.
    /// </summary>
    public async Task<string> GetArchiveAsync(int? limit = null)
    {
        if (limit is { } requestedLimit)
        {
            if (GetArchiveLimitIPC is not null)
            {
                var clamped = Math.Clamp(requestedLimit, MinArchiveLimit, MaxArchiveLimit);
                return await GetArchiveLimitIPC(clamped);
            }

            PluginLog.Warning("SimpleWheel GetArchiveLimit IPC is not available, falling back to the default archive window.");
        }

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
