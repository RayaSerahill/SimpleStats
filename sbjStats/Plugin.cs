using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.EzIpcManager;
using ECommons.Logging;
using sbjStats.Windows;

namespace sbjStats;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] internal static IPluginLog Log { get; set; } = null!;

    private const string CommandName = "/sbjstats";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("sbjStats");
    private ConfigWindow ConfigWindow { get; }
    private SimpleBlackjackIpc SimpleBlackjackIpc { get; set; }
    private bool ipcInitialized = false;
    
    

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this, Module.All);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;

        
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open SBJ stats uploader settings"
        });

        InitializeIpc();
    }
    
    private void InitializeIpc() {
        try
        {
            Log.Information("Initializing IPC for SimpleBlackjack...");
            SimpleBlackjackIpc = new SimpleBlackjackIpc(
                getStats: HandleGetStats,
                getArchives: HandleGetArchives,
                processRound: processRound
            );
            ipcInitialized = true;
            Log.Information("IPC initialized.");
        } catch (Exception ex)
        {
            Log.Information($"Failed to initialize IPC: {ex.Message}");
            ipcInitialized = false;
        }
    }

    private void processRound(StatsRecording obj)
    {
        Log.Information("Processing completed SBJ round, uploading stats...");
        CsvUploader.SendStatAsCsv(obj, Configuration.Endpoint, Configuration.ApiKey);
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args?.Trim().ToLowerInvariant();
        if (trimmedArgs == "archive")
        {
            var archives = SimpleBlackjackIpc.GetArchives();
            Log.Information(Newtonsoft.Json.JsonConvert.SerializeObject(archives));
            var output = string.Join(", ", archives.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            Log.Information(output);
            Log.Information("========== Available Archives ==========");
        }
        else
        {
            OpenConfigUi();
        }

    }

    private void DrawUi()
    {
        WindowSystem.Draw();
    }

    private void OpenConfigUi()
    {
        ConfigWindow.IsOpen = true;
    }

    private void HandleGameFinishedEx(StatsRecording stat)
    {
        Log.Information("Received SBJ game finished event, uploading stats...");
        try
        {
            if (!Configuration.EnableUpload)
            {
                PluginLog.Information("SBJ upload skipped: upload disabled.");
                return;
            }

            CsvUploader.SendStatAsCsv(
                stat,
                Configuration.Endpoint,
                Configuration.ApiKey);

            PluginLog.Information("SBJ stat uploaded.");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"SBJ upload failed: {ex}");
        }
    }
    
    
    private List<StatsRecording> HandleGetStats(string archiveId) {
        return new List<StatsRecording>();
    }

    private Dictionary<string, string> HandleGetArchives() {
        return new Dictionary<string, string>();
    }
}