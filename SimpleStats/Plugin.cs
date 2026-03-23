using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using sbjStats.Windows;

namespace sbjStats;

public sealed class Plugin : IDalamudPlugin
{
    public static Plugin Instance { get; private set; } = null!;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] internal static IPluginLog Log { get; set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; set; } = null!;

    private const string CommandName = "/simplestats";
    private const string Endpoint = "https://stats.serahill.net/api/admin/games/import";
    public const string EndpointScratch = "https://stats.serahill.net/api/admin/scratch/import";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("sbjStats");
    public string StatsEndpoint => Endpoint;

    private readonly ConfigWindow configWindow;
    private readonly BlackjackUploadHandler blackjackUploadHandler;
    private readonly ScratchUploadHandler scratchUploadHandler;

    private SimpleBlackjackIpc? simpleBlackjackIpc;
    private SimpleScratchIpc? simpleScratchIpc;

    public Plugin()
    {
        Instance = this;
        ECommonsMain.Init(PluginInterface, this, Module.All);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        blackjackUploadHandler = new BlackjackUploadHandler(this);
        scratchUploadHandler = new ScratchUploadHandler(this);

        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open SBJ stats uploader settings"
        });

        InitializeIpc();
    }

    private void InitializeIpc()
    {
        try
        {
            Log.Information("Initializing IPC for SimpleBlackjack...");
            simpleBlackjackIpc = new SimpleBlackjackIpc(blackjackUploadHandler.HandleCompletedRound);
            Log.Information("SimpleBlackjack IPC initialized.");
        }
        catch (Exception ex)
        {
            Log.Information($"Failed to initialize SimpleBlackjack IPC: {ex.Message}");
        }

        try
        {
            Log.Information("Initializing IPC for SimpleScratch...");
            simpleScratchIpc = new SimpleScratchIpc(scratchUploadHandler.HandleGameEnded);
            Log.Information("SimpleScratch IPC initialized.");
        }
        catch (Exception ex)
        {
            Log.Information($"Failed to initialize SimpleScratch IPC: {ex.Message}");
        }
    }

    public async Task UploadExistingStatsSbjAsync()
    {
        if (simpleBlackjackIpc is null)
        {
            ShowToast("SimpleBlackjack IPC is not available.", NotificationType.Error);
            return;
        }

        await blackjackUploadHandler.UploadExistingAsync(simpleBlackjackIpc);
    }

    public async Task UploadExistingStatsScratchAsync()
    {
        if (simpleScratchIpc is null)
        {
            ShowToast("SimpleScratch IPC is not available.", NotificationType.Error);
            return;
        }

        await scratchUploadHandler.UploadExistingAsync(simpleScratchIpc);
    }

    public void ShowToast(string message, NotificationType type = NotificationType.Info)
    {
        try
        {
            NotificationManager.AddNotification(new Notification
            {
                Content = message,
                Type = type,
                Minimized = false
            });
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to show notification: {ex.Message}");
        }
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;

        WindowSystem.RemoveAllWindows();
        configWindow.Dispose();

        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        OpenConfigUi();
    }

    private void DrawUi()
    {
        WindowSystem.Draw();
    }

    private void OpenConfigUi()
    {
        configWindow.IsOpen = true;
    }
}
