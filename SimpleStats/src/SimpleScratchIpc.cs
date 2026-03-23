using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ECommons.EzIpcManager;
using ECommons.Logging;

namespace sbjStats;

public sealed class SimpleScratchIpc
{
    private readonly Action<string> onGameEnded;

    public SimpleScratchIpc(Action<string> onGameEnded)
    {
        PluginLog.Information("SimpleScratchIpc constructor called.");
        this.onGameEnded = onGameEnded;
        EzIPC.Init(this, "SimpleScratch");
        PluginLog.Information("EzIPC.Init called for SimpleScratchIpc.");
    }

    [EzIPC] private Func<CreatePlayerMessage, Task<string>>? CreatePlayerIPC;
    [EzIPC] private Func<Task<string>>? GetPlayersIPC;
    [EzIPC] private Func<int, Task<bool>>? EndPlayerIPC;
    [EzIPC] private Func<List<CardPreset>>? GetPresetsIPC;
    [EzIPC] private Func<Task<string>>? GetArchiveIPC;
    [EzIPC] private Func<string>? GetPresetNamesIPC;
    [EzIPC] private Func<string>? GetThemesIPC;

    public async Task<string> GetArchiveAsync()
    {
        if (GetArchiveIPC is null)
        {
            PluginLog.Warning("SimpleScratch GetArchive IPC is not available.");
            return string.Empty;
        }

        return await GetArchiveIPC();
    }

    [EzIPCEvent("GameEndedIPC")]
    private void OnGameEnded(string json)
    {
        DuoLog.Information($"[GameEnded] {json}");
        onGameEnded(json);
    }

    public class CreatePlayerMessage
    {
        public string PlayerName { get; set; } = string.Empty;
        public int CardCount { get; set; } = 5;
        public string? ThemeName { get; set; }
        public string? PresetName { get; set; }
        public string? PresetJson { get; set; }
        public bool ShowPrizeText { get; set; } = true;
    }

    public class CardPreset
    {
        public string Name { get; set; } = "";
        public List<object> Segments { get; set; } = [];
    }
}
