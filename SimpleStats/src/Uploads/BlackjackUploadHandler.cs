using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using ECommons.Logging;

namespace sbjStats;

public sealed class BlackjackUploadHandler : GameUploadHandlerBase
{
    public BlackjackUploadHandler(Plugin plugin) : base(plugin)
    {
    }

    public void HandleCompletedRound(StatsRecording stat)
    {
        if (!IsLiveUploadEnabled("SimpleBlackjack") || !HasUploadConfiguration("SimpleBlackjack", notifyUser: true))
            return;

        PluginLog.Information("Processing completed SimpleBlackjack round, uploading stats.");
        CsvUploader.SendStatAsCsv(stat, Endpoint, ApiKey);
    }

    public async Task UploadExistingAsync(SimpleBlackjackIpc ipc)
    {
        if (!HasUploadConfiguration("SimpleBlackjack", notifyUser: true))
            return;

        var archives = ipc.GetArchives();
        if (archives.Count == 0)
        {
            Plugin.ShowToast("SimpleBlackjack: no archives were returned.", NotificationType.Info);
            return;
        }

        Plugin.ShowToast($"Starting upload of existing SimpleBlackjack stats for {archives.Count} archives...", NotificationType.Info);

        var allStats = new List<StatsRecording>();
        foreach (var archive in archives)
        {
            var stats = ipc.GetStats(archive.Key);
            if (stats.Count == 0)
                continue;

            allStats.AddRange(stats);
        }

        if (allStats.Count == 0)
        {
            Plugin.ShowToast("SimpleBlackjack: no stats were found in the available archives.", NotificationType.Info);
            return;
        }

        var orderedStats = allStats
            .OrderBy(stat => stat.Time)
            .ToList();

        await CsvUploader.SendMassStatsAsCsvAsync(orderedStats, Endpoint, ApiKey);
    }
}
