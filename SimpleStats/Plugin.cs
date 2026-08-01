using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
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
    [PluginService] internal static IClientState ClientState { get; set; } = null!;
    [PluginService] internal static IFramework Framework { get; set; } = null!;
    [PluginService] internal static IPluginLog Log { get; set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; set; } = null!;

    private const string CommandName = "/simplestats";
#if DEBUG
    private const string UploadDomain = "http://localhost:3000";
#else
    private const string UploadDomain = "https://stats.serahill.net";
#endif
    private const string Endpoint = UploadDomain + "/api/admin/games/import";
    public const string EndpointScratch = UploadDomain + "/api/admin/scratch/import";
    public const string EndpointAviator = UploadDomain + "/api/admin/aviator/import";

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("sbjStats");
    public string StatsEndpoint => Endpoint;

    private readonly ConfigWindow configWindow;
    private readonly BlackjackUploadHandler blackjackUploadHandler;
    private readonly ScratchUploadHandler scratchUploadHandler;
    private readonly AviatorUploadHandler aviatorUploadHandler;

    private SimpleBlackjackIpc? simpleBlackjackIpc;
    private SimpleScratchIpc? simpleScratchIpc;
    private SimpleAviatorIpc? simpleAviatorIpc;

    public Plugin()
    {
        Instance = this;
        ECommonsMain.Init(PluginInterface, this, Module.All);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        blackjackUploadHandler = new BlackjackUploadHandler(this);
        scratchUploadHandler = new ScratchUploadHandler(this);
        aviatorUploadHandler = new AviatorUploadHandler(this);

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

        try
        {
            Log.Information("Initializing IPC for SimpleAviator...");
            simpleAviatorIpc = new SimpleAviatorIpc(aviatorUploadHandler.HandleRoundEnded);
            Log.Information("SimpleAviator IPC initialized.");
        }
        catch (Exception ex)
        {
            Log.Information($"Failed to initialize SimpleAviator IPC: {ex.Message}");
        }
    }

    public async Task UploadExistingStatsSbjAsync()
    {
        try
        {
            if (simpleBlackjackIpc is null)
            {
                ShowToast("SimpleBlackjack IPC is not available.", NotificationType.Error);
                return;
            }

            await blackjackUploadHandler.UploadExistingAsync(simpleBlackjackIpc);
        }
        catch (IpcNotReadyError ex)
        {
            Log.Warning($"SimpleBlackjack IPC is not ready: {ex.Message}");
            ShowToast("SimpleBlackjack IPC is not available. Make sure SimpleBlackjack is loaded, then try again.", NotificationType.Error);
        }
        catch (Exception ex)
        {
            Log.Error($"SimpleBlackjack existing upload failed: {ex}");
            ShowToast("SimpleBlackjack upload failed. Check /xllog for details.", NotificationType.Error);
        }
    }

    public async Task UploadExistingStatsScratchAsync()
    {
        try
        {
            if (simpleScratchIpc is null)
            {
                ShowToast("SimpleScratch IPC is not available.", NotificationType.Error);
                return;
            }

            await scratchUploadHandler.UploadExistingAsync(simpleScratchIpc);
        }
        catch (IpcNotReadyError ex)
        {
            Log.Warning($"SimpleScratch IPC is not ready: {ex.Message}");
            ShowToast("SimpleScratch IPC is not ready yet. Try again after SimpleScratch finishes loading.", NotificationType.Error);
        }
        catch (Exception ex)
        {
            Log.Error($"SimpleScratch existing upload failed: {ex}");
            ShowToast("SimpleScratch upload failed. Check /xllog for details.", NotificationType.Error);
        }
    }

    public async Task UploadExistingStatsAviatorAsync()
    {
        try
        {
            if (simpleAviatorIpc is null)
            {
                ShowToast("SimpleAviator IPC is not available.", NotificationType.Error);
                return;
            }

            await aviatorUploadHandler.UploadExistingAsync(simpleAviatorIpc);
        }
        catch (IpcNotReadyError ex)
        {
            Log.Warning($"SimpleAviator IPC is not ready: {ex.Message}");
            ShowToast("SimpleAviator IPC is not ready yet. Try again after SimpleAviator finishes loading.", NotificationType.Error);
        }
        catch (Exception ex)
        {
            Log.Error($"SimpleAviator existing upload failed: {ex}");
            ShowToast("SimpleAviator upload failed. Check /xllog for details.", NotificationType.Error);
        }
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
