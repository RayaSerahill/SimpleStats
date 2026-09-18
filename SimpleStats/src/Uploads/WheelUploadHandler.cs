using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using ECommons.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace sbjStats;

/// <summary>
/// Collects SimpleWheel games and ships them to the stats server.
/// Live games arrive one at a time from GameEndedIPC and are posted as a single JSON object.
/// Archive snapshots come from GetArchive(Limit)IPC and are posted as a JSON array.
/// Both use the same record schema so the server only has to understand one shape; see example.md.
/// </summary>
public sealed class WheelUploadHandler : GameUploadHandlerBase
{
    private const string GameName = "SimpleWheel";

    public WheelUploadHandler(Plugin plugin) : base(plugin)
    {
    }

    public void HandleGameEnded(string json, long archivedAtUnixSeconds)
    {
        if (!IsLiveUploadEnabled(GameName) || !HasUploadConfiguration(GameName, notifyUser: false))
            return;

        _ = UploadLiveGameAsync(json, archivedAtUnixSeconds);
    }

    public async Task UploadExistingAsync(SimpleWheelIpc ipc, int? archiveLimit = null)
    {
        if (!HasUploadConfiguration(GameName, notifyUser: true))
            return;

        var archiveJson = await ipc.GetArchiveAsync(archiveLimit);
        if (string.IsNullOrWhiteSpace(archiveJson) || archiveJson.Trim() == "[]")
        {
            Plugin.ShowToast("SimpleWheel: archive payload was empty.", NotificationType.Info);
            return;
        }

        var uploaded = await UploadArchiveSnapshotAsync(archiveJson);
        if (uploaded)
            Plugin.ShowToast("SimpleWheel archive uploaded.", NotificationType.Success);
    }

    public async Task UploadLiveGameAsync(string json, long archivedAtUnixSeconds)
    {
        try
        {
            var dealer = await GetCurrentCharacterNameAsync();
            var request = BuildLiveUploadRequest(json, archivedAtUnixSeconds, dealer);
            if (request is null)
            {
                PluginLog.Warning("SimpleWheel live upload skipped: payload could not be transformed.");
                return;
            }

            await SendWheelUploadAsync(request);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"SimpleWheel live upload failed: {ex}");
        }
    }

    public async Task<bool> UploadArchiveSnapshotAsync(string archiveJson)
    {
        var request = BuildArchiveUploadRequest(archiveJson);
        if (request is null)
        {
            PluginLog.Warning("SimpleWheel archive upload skipped: payload could not be transformed.");
            Plugin.ShowToast("SimpleWheel: archive payload could not be read.", NotificationType.Error);
            return false;
        }

        await SendWheelUploadAsync(request);
        PluginLog.Information($"SimpleWheel archive snapshot of {request.GameCount} games transformed and uploaded.");
        return true;
    }

    public static WheelUploadRequest? BuildLiveUploadRequest(string json, long archivedAtUnixSeconds, string? dealer)
    {
        var payload = TryParseObject(json);
        if (payload is null)
            return null;

        payload["archived_at"] = archivedAtUnixSeconds;
        if (!string.IsNullOrWhiteSpace(dealer))
            payload["dealer"] = dealer;

        NormalizePrizes(payload, "prizes_won");

        return new WheelUploadRequest
        {
            UploadType = "live",
            RawJson = payload.ToString(Formatting.None),
            PlayerName = payload["player_name"]?.ToString(),
            GameId = payload["game_uuid"]?.ToString(),
            OccurredAtUnixSeconds = archivedAtUnixSeconds,
            Dealer = dealer,
            GameCount = 1
        };
    }

    public static WheelUploadRequest? BuildArchiveUploadRequest(string archiveJson)
    {
        if (string.IsNullOrWhiteSpace(archiveJson))
            return null;

        JArray archive;
        try
        {
            archive = JArray.Parse(archiveJson);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleWheel archive payload: {ex}");
            return null;
        }

        var games = new JArray();
        foreach (var item in archive.OfType<JObject>())
        {
            games.Add(NormalizeArchiveGame(item));
        }

        return new WheelUploadRequest
        {
            UploadType = "archive",
            RawJson = games.ToString(Formatting.None),
            GameCount = games.Count
        };
    }

    /// <summary>
    /// Renames the archive row fields to the same names GameEndedIPC uses, so live and archive records look alike.
    /// Unknown fields are carried over untouched because SimpleWheel promises its payloads are additive.
    /// </summary>
    private static JObject NormalizeArchiveGame(JObject item)
    {
        var game = (JObject)item.DeepClone();

        RenameField(game, "id", "game_uuid");
        RenameField(game, "player_nickname", "player_name");
        RenameField(game, "spins", "max_spins");
        RenameField(game, "prizes", "prizes_won");

        // The archive does not carry these, but the server should see a stable set of keys.
        game["player_homeworld"] ??= JValue.CreateNull();
        game["spins_used"] ??= JValue.CreateNull();

        var createdAt = game["created_at"];
        game.Remove("created_at");
        game["archived_at"] = ConvertArchiveTimestamp(createdAt);

        NormalizePrizes(game, "prizes_won");
        return game;
    }

    private static void RenameField(JObject obj, string from, string to)
    {
        var value = obj[from];
        if (value is null)
            return;

        obj.Remove(from);
        obj[to] ??= value;
    }

    private static JToken ConvertArchiveTimestamp(JToken? createdAt)
    {
        switch (createdAt?.Type)
        {
            case JTokenType.Integer:
                return createdAt;
            case JTokenType.Date:
                return createdAt.Value<DateTime>().ToUniversalTime().Subtract(DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerSecond;
            case JTokenType.String:
            {
                var text = createdAt.Value<string>() ?? string.Empty;
                if (TryParseArchiveTimestamp(text, out var unixSeconds))
                    return unixSeconds;

                PluginLog.Warning($"SimpleWheel archive item had an invalid created_at value: {text}");
                return JValue.CreateNull();
            }
            default:
                return JValue.CreateNull();
        }
    }

    /// <summary>
    /// SimpleWheel might hand prizes over as a real JSON array or as a JSON-encoded string of one.
    /// Either way the server gets a plain array of prize labels.
    /// </summary>
    private static void NormalizePrizes(JObject game, string field)
    {
        var prizes = game[field];
        switch (prizes?.Type)
        {
            case JTokenType.Array:
                return;
            case JTokenType.String:
            {
                var text = prizes.Value<string>() ?? string.Empty;
                if (text.TrimStart().StartsWith('['))
                {
                    try
                    {
                        game[field] = JArray.Parse(text);
                        return;
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning($"SimpleWheel prizes looked like JSON but did not parse: {ex.Message}");
                    }
                }

                game[field] = string.IsNullOrWhiteSpace(text) ? new JArray() : new JArray(text);
                return;
            }
            default:
                game[field] = new JArray();
                return;
        }
    }

    private static Task<string?> GetCurrentCharacterNameAsync()
    {
        return Plugin.Framework.RunOnFrameworkThread(() => Plugin.ClientState.LocalPlayer?.Name.TextValue);
    }

    public async Task SendWheelUploadAsync(WheelUploadRequest request)
    {
        PluginLog.Information(
            $"SimpleWheel upload sending '{request.UploadType}' payload with {request.GameCount} game(s), player '{request.PlayerName ?? "<n/a>"}', game '{request.GameId ?? "<n/a>"}'.");
        PluginLog.Debug($"SimpleWheel raw JSON payload: {request.RawJson}");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using var content = new StringContent(request.RawJson, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(Plugin.EndpointWheel, content);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var msg = $"SimpleWheel upload failed: {(int)response.StatusCode} {response.ReasonPhrase} | {responseText}";
            PluginLog.Warning(msg);
            Plugin.ShowToast(msg, NotificationType.Error);
        }
        else
        {
            Plugin.ShowToast("Wheel stats uploaded successfully \\o/", NotificationType.Success);
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
            PluginLog.Error($"Failed to parse SimpleWheel JSON object: {ex}");
            return null;
        }
    }

    private static bool TryParseArchiveTimestamp(string value, out long unixSeconds)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out unixSeconds))
            return true;

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            unixSeconds = parsed.ToUnixTimeSeconds();
            return true;
        }

        unixSeconds = 0;
        return false;
    }
}
