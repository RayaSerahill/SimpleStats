using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using ECommons.DalamudServices.Legacy;
using ECommons.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace sbjStats;

public sealed class AviatorUploadHandler : GameUploadHandlerBase
{
    public AviatorUploadHandler(Plugin plugin) : base(plugin)
    {
    }

    public void HandleRoundEnded(string json, long archivedAtUnixSeconds)
    {
        if (!IsLiveUploadEnabled("SimpleAviator") ||
            !HasUploadConfiguration("SimpleAviator", notifyUser: false, Plugin.EndpointAviator))
            return;

        _ = UploadLiveRoundAsync(json, archivedAtUnixSeconds);
    }

    public async Task UploadExistingAsync(SimpleAviatorIpc ipc)
    {
        if (!HasUploadConfiguration("SimpleAviator", notifyUser: true, Plugin.EndpointAviator))
            return;

        var archivedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dealer = await GetCurrentCharacterNameAsync();

        var statsJson = await ipc.GetStatsAsync();
        var archiveJson = await ipc.GetArchiveAsync();

        var stats = TryParseObject(statsJson);
        var archive = TryParseArray(archiveJson) ?? (stats?["games"]?.DeepClone() as JArray);
        if (archive is null || archive.Count == 0)
        {
            Plugin.ShowToast("SimpleAviator: no archived games were returned.", NotificationType.Info);
            return;
        }

        var gameIds = ExtractGameIds(archive).ToList();
        if (gameIds.Count == 0)
        {
            Plugin.ShowToast("SimpleAviator: archive payload did not contain game ids.", NotificationType.Info);
            return;
        }

        Plugin.ShowToast($"Starting upload of existing SimpleAviator stats for {gameIds.Count} games...", NotificationType.Info);

        var gameArchives = await FetchGameArchivesAsync(ipc, gameIds);
        var request = BuildArchiveUploadRequest(stats, archive, gameArchives, archivedAtUnixSeconds, dealer);
        await SendAviatorUploadAsync(request);

        Plugin.ShowToast("SimpleAviator archive uploaded.", NotificationType.Success);
    }

    public async Task UploadLiveRoundAsync(string json, long archivedAtUnixSeconds)
    {
        try
        {
            var dealer = await GetCurrentCharacterNameAsync();
            var request = BuildLiveUploadRequest(json, archivedAtUnixSeconds, dealer);
            if (request is null)
            {
                PluginLog.Warning("SimpleAviator live upload skipped: payload could not be transformed.");
                return;
            }

            await SendAviatorUploadAsync(request);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"SimpleAviator live upload failed: {ex}");
        }
    }

    public AviatorUploadRequest? BuildLiveUploadRequest(string roundJson, long archivedAtUnixSeconds, string? dealer)
    {
        var round = TryParseObject(roundJson);
        if (round is null)
            return null;

        var payload = new JObject
        {
            ["upload_type"] = new JValue("live_round"),
            ["source"] = new JValue("SimpleAviator"),
            ["archived_at"] = new JValue(archivedAtUnixSeconds),
            ["dealer"] = string.IsNullOrWhiteSpace(dealer) ? JValue.CreateNull() : new JValue(dealer),
            ["round"] = round.DeepClone()
        };

        return new AviatorUploadRequest
        {
            UploadType = "live_round",
            RawJson = payload.ToString(Formatting.None),
            GameId = round["game_id"]?.ToString(),
            OccurredAtUnixSeconds = archivedAtUnixSeconds,
            Dealer = dealer
        };
    }

    public AviatorUploadRequest BuildArchiveUploadRequest(
        JObject? stats,
        JArray archive,
        JArray gameArchives,
        long archivedAtUnixSeconds,
        string? dealer)
    {
        var payload = new JObject
        {
            ["upload_type"] = new JValue("archive"),
            ["source"] = new JValue("SimpleAviator"),
            ["archived_at"] = new JValue(archivedAtUnixSeconds),
            ["dealer"] = string.IsNullOrWhiteSpace(dealer) ? JValue.CreateNull() : new JValue(dealer),
            ["stats"] = stats?.DeepClone() ?? new JObject(),
            ["archive"] = archive.DeepClone(),
            ["game_archives"] = gameArchives.DeepClone()
        };

        return new AviatorUploadRequest
        {
            UploadType = "archive",
            RawJson = payload.ToString(Formatting.None),
            GameCount = gameArchives.Count > 0 ? gameArchives.Count : archive.Count,
            OccurredAtUnixSeconds = archivedAtUnixSeconds,
            Dealer = dealer
        };
    }

    public async Task SendAviatorUploadAsync(AviatorUploadRequest request)
    {
        PluginLog.Information(
            $"SimpleAviator upload sending '{request.UploadType}' payload for game '{request.GameId ?? "<multiple>"}'.");
        Log.Information($"Raw JSON payload: {request.RawJson}");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiKey);

        using var content = new StringContent(request.RawJson, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Plugin.EndpointAviator.Trim(), content);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var msg = $"Upload failed: {(int)response.StatusCode} {response.ReasonPhrase} | {responseText}";
            Plugin.Instance?.ShowToast(msg, NotificationType.Error);
        }
        else
        {
            Plugin.Instance?.ShowToast("Stats uploaded successfully \\o/", NotificationType.Success);
        }
    }

    private static Task<string?> GetCurrentCharacterNameAsync()
    {
        return Plugin.Framework.RunOnFrameworkThread(() => Plugin.ClientState.LocalPlayer?.Name.TextValue);
    }

    private async Task<JArray> FetchGameArchivesAsync(SimpleAviatorIpc ipc, IReadOnlyList<string> gameIds)
    {
        var gameArchives = new JArray();

        foreach (var gameId in gameIds)
        {
            var gameJson = await ipc.GetGameArchiveAsync(gameId);
            var game = TryParseObject(gameJson);
            if (game is null || game.Count == 0)
            {
                PluginLog.Warning($"SimpleAviator game archive skipped: '{gameId}' returned an empty payload.");
                continue;
            }

            gameArchives.Add(game);
        }

        return gameArchives;
    }

    private static IEnumerable<string> ExtractGameIds(JArray archive)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in archive.OfType<JObject>())
        {
            var gameId = item["game_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(gameId) || !seen.Add(gameId))
                continue;

            yield return gameId;
        }
    }

    private static JObject? TryParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JObject.Parse(json);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleAviator JSON object: {ex}");
            return null;
        }
    }

    private static JArray? TryParseArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JArray.Parse(json);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleAviator JSON array: {ex}");
            return null;
        }
    }
}
