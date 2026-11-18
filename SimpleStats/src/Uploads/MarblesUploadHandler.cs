using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using ECommons.DalamudServices.Legacy;
using ECommons.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace sbjStats;

/// <summary>
/// Collects SimpleMarbles races and ships them to the stats server.
/// A live race arrives from the RaceEnded event and is posted as a single JSON object.
/// The archive comes from GetArchiveIPC and is posted as the JSON array SimpleMarbles returned.
/// Records are passed through as SimpleMarbles wrote them; a live race only gains archived_at and dealer.
/// </summary>
public sealed class MarblesUploadHandler : GameUploadHandlerBase
{
    private const string GameName = "SimpleMarbles";
    private const int MaxToastBodyLength = 200;

    public MarblesUploadHandler(Plugin plugin) : base(plugin)
    {
    }

    public void HandleRaceEnded(string json, long archivedAtUnixSeconds)
    {
        if (!IsLiveUploadEnabled(GameName) || !HasUploadConfiguration(GameName, notifyUser: false))
            return;

        _ = UploadLiveRaceAsync(json, archivedAtUnixSeconds);
    }

    public async Task UploadExistingAsync(SimpleMarblesIpc ipc)
    {
        if (!HasUploadConfiguration(GameName, notifyUser: true))
            return;

        var archiveJson = await ipc.GetArchiveAsync();
        if (string.IsNullOrWhiteSpace(archiveJson))
        {
            Plugin.ShowToast("SimpleMarbles: archive payload was empty.", NotificationType.Info);
            return;
        }

        var gameCount = CountArchiveGames(archiveJson);
        if (gameCount is null)
        {
            PluginLog.Warning("SimpleMarbles archive upload skipped: payload was not a JSON array.");
            Plugin.ShowToast("SimpleMarbles: archive payload could not be read.", NotificationType.Error);
            return;
        }

        if (gameCount == 0)
        {
            Plugin.ShowToast("SimpleMarbles: archive contained no games, nothing to upload.", NotificationType.Info);
            return;
        }

        PluginLog.Information($"SimpleMarbles upload sending 'archive' payload with {gameCount} game(s).");
        PluginLog.Debug($"SimpleMarbles raw JSON payload: {archiveJson}");

        // Nothing in the archive needs changing, so the server gets SimpleMarbles' own text.
        var ok = await PostJsonAsync(Plugin.EndpointMarbles, archiveJson, "SimpleMarbles archive upload");
        if (ok)
            Plugin.ShowToast($"SimpleMarbles: {gameCount} archived game(s) uploaded \\o/", NotificationType.Success);
    }

    public async Task UploadLiveRaceAsync(string json, long archivedAtUnixSeconds)
    {
        try
        {
            var dealer = await GetCurrentCharacterNameAsync();
            var payload = BuildLivePayload(json, archivedAtUnixSeconds, dealer);
            if (payload is null)
            {
                PluginLog.Warning("SimpleMarbles live upload skipped: payload could not be transformed.");
                return;
            }

            var body = payload.ToString(Formatting.None);
            PluginLog.Information("SimpleMarbles upload sending 'live' payload with 1 race.");
            PluginLog.Debug($"SimpleMarbles raw JSON payload: {body}");

            var ok = await PostJsonAsync(Plugin.EndpointMarbles, body, "SimpleMarbles upload");
            if (ok)
                Plugin.ShowToast("Marble stats uploaded successfully \\o/", NotificationType.Success);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"SimpleMarbles live upload failed: {ex}");
        }
    }

    /// <summary>
    /// Adds the two fields every live upload carries: when the race ended and which character hosted it.
    /// Everything SimpleMarbles sent is kept as it is, including a timestamp of its own if it has one.
    /// </summary>
    public static JObject? BuildLivePayload(string json, long archivedAtUnixSeconds, string? dealer)
    {
        var payload = TryParseObject(json);
        if (payload is null)
            return null;

        if (payload["archived_at"] is null or { Type: JTokenType.Null })
            payload["archived_at"] = archivedAtUnixSeconds;

        if (!string.IsNullOrWhiteSpace(dealer))
            payload["dealer"] = dealer;

        return payload;
    }

    /// <summary>Number of games in the archive, or null when it is not a JSON array.</summary>
    private static int? CountArchiveGames(string archiveJson)
    {
        try
        {
            return JArray.Parse(archiveJson).Count;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleMarbles archive payload: {ex}");
            return null;
        }
    }

    private static Task<string?> GetCurrentCharacterNameAsync()
    {
        return Plugin.Framework.RunOnFrameworkThread(() => Plugin.ClientState.LocalPlayer?.Name.TextValue);
    }

    /// <summary>Posts a JSON body with the bearer key. Failures are logged and toasted; returns success.</summary>
    private async Task<bool> PostJsonAsync(string endpoint, string json, string what)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(endpoint, content);
        var responseText = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
            return true;

        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}";
        PluginLog.Warning($"{what} failed: {status} | {responseText}");

        // The log keeps the whole body. The toast gets a short one, because a server without the
        // marbles route yet answers with an entire HTML error page.
        var shortBody = responseText.Length > MaxToastBodyLength
            ? responseText[..MaxToastBodyLength] + "..."
            : responseText;
        Plugin.ShowToast($"{what} failed: {status} | {shortBody}", NotificationType.Error);
        return false;
    }

    private static JObject? TryParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            // Read dates as plain strings. Json.NET would otherwise parse them and write them back reformatted.
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleMarbles JSON object: {ex}");
            return null;
        }
    }
}
