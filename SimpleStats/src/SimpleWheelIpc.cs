using System;
using System.Collections.Generic;
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
    [EzIPC] private Func<List<WheelPreset>>? GetPresetsIPC;

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

    /// <summary>
    /// Fetches all of the dealer's saved presets, segments included. The gate is synchronous, so it is
    /// invoked on the framework thread to keep SimpleWheel's state access where it expects it.
    /// </summary>
    public async Task<List<WheelPreset>> GetPresetsAsync()
    {
        if (GetPresetsIPC is null)
        {
            PluginLog.Warning("SimpleWheel GetPresets IPC is not available.");
            return [];
        }

        var presets = await Plugin.Framework.RunOnFrameworkThread(() => GetPresetsIPC());
        return presets ?? [];
    }

    [EzIPCEvent("GameEndedIPC")]
    private void OnGameEnded(string json)
    {
        PluginLog.Information($"[SimpleWheel GameEnded] {json}");

        var archivedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        onGameEnded(json, archivedAtUnixSeconds);
    }

    /// <summary>Mirror of SimpleWheel's WheelPreset. Property names must match theirs for IPC conversion.</summary>
    public class WheelPreset
    {
        public string Name { get; set; } = string.Empty;
        public List<WheelSegment> Segments { get; set; } = [];
    }

    /// <summary>Mirror of SimpleWheel's SegmentDto with the defaults from the IPC reference.</summary>
    public class WheelSegment
    {
        public string Type { get; set; } = "string";
        public string Value { get; set; } = string.Empty;
        public bool ValueTypeGil { get; set; }
        public float gilValue { get; set; }
        public string PresetURL { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool Win { get; set; } = true;
        public bool Bankrupt { get; set; }
        public int Probability { get; set; } = 1;
        public string Color { get; set; } = string.Empty;
        public string PatternUrl { get; set; } = string.Empty;
        public int FreeSpins { get; set; }
        public bool ReverseSpin { get; set; }
        public List<string> Effects { get; set; } = [];
    }
}
