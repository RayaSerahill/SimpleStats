using System;
using Dalamud.Interface.ImGuiNotification;
using ECommons.Logging;

namespace sbjStats;

public abstract class GameUploadHandlerBase
{
    protected readonly Plugin Plugin;

    protected GameUploadHandlerBase(Plugin plugin)
    {
        Plugin = plugin;
    }

    protected string ApiKey => Plugin.Configuration.ApiKey?.Trim() ?? string.Empty;
    protected string Endpoint => Plugin.StatsEndpoint;

    protected bool IsLiveUploadEnabled(string gameName)
    {
        if (Plugin.Configuration.EnableUpload)
            return true;

        PluginLog.Information($"{gameName} upload skipped: live upload disabled.");
        return false;
    }

    protected bool HasUploadConfiguration(string gameName, bool notifyUser)
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            PluginLog.Warning($"{gameName} upload skipped: endpoint is missing.");
            if (notifyUser)
            {
                Plugin.ShowToast($"{gameName}: upload endpoint is missing.", NotificationType.Error);
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            PluginLog.Warning($"{gameName} upload skipped: API key is missing.");
            if (notifyUser)
            {
                Plugin.ShowToast($"{gameName}: API key is missing.", NotificationType.Error);
            }

            return false;
        }

        return true;
    }
}
