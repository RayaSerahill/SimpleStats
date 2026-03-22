using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ECommons.EzIpcManager;
using ECommons.Logging;

namespace sbjStats;

public class SimpleScratchIpc
{
    public SimpleScratchIpc()
    {
        PluginLog.Information("SimpleScratchIpc constructor called.");
        EzIPC.Init(this, "SimpleScratch");
        PluginLog.Information("EzIPC.Init called for SimpleScratchIpc.");
    }
    

    [EzIPC] public Func<CreatePlayerMessage, Task<string>> CreatePlayerIPC;
    [EzIPC] public Func<Task<string>> GetPlayersIPC;
    [EzIPC] public Func<int, Task<bool>> EndPlayerIPC;
    [EzIPC] public Func<List<CardPreset>> GetPresetsIPC;
    [EzIPC] public Func<Task<string>> GetArchiveIPC;
    [EzIPC] public Func<string> GetPresetNamesIPC;
    [EzIPC] public Func<string> GetThemesIPC;

    [EzIPCEvent("GameEndedIPC")]
    private void OnGameEnded(string json)
    {
        DuoLog.Information($"[GameEnded] {json}");
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