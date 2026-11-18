using System;
using System.IO;
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

namespace sbjStats;

/// <summary>
/// Collects SimpleMarbles games and ships them to the stats server.
/// The archive comes from GetArchiveIPC as one summary row per game. Those rows only hold totals,
/// so each of the dealer's own finished games also gets its full detail from GetGameArchiveIPC,
/// attached to its row as "detail". That is where races and per-player results live.
/// A live race arrives from the RaceEnded event and is posted as a single JSON object.
/// Records are passed through as SimpleMarbles wrote them.
/// </summary>
public sealed class MarblesUploadHandler : GameUploadHandlerBase
{
    private const string GameName = "SimpleMarbles";
    private const int MaxToastBodyLength = 200;

    // Rows with detail attached are large, and the server caps request bodies.
    private const int GamesPerRequest = 25;

    private bool uploadRunning;

    /// <summary>Progress and outcome of the last archive upload, for the config window.</summary>
    public string Status { get; private set; } = "Nothing uploaded yet.";

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

        if (uploadRunning)
        {
            Plugin.ShowToast("SimpleMarbles: an upload is already running.", NotificationType.Info);
            return;
        }

        uploadRunning = true;
        try
        {
            await UploadArchiveAsync(ipc);
        }
        catch (Exception)
        {
            SetStatus("upload failed, check /xllog.");
            throw;
        }
        finally
        {
            uploadRunning = false;
        }
    }

    private async Task UploadArchiveAsync(SimpleMarblesIpc ipc)
    {
        SetStatus("fetching archive...");
        var archiveJson = await ipc.GetArchiveAsync();
        if (string.IsNullOrWhiteSpace(archiveJson))
        {
            SetStatus("archive payload was empty.");
            Plugin.ShowToast("SimpleMarbles: archive payload was empty.", NotificationType.Info);
            return;
        }

        var games = TryParseArray(archiveJson);
        if (games is null)
        {
            PluginLog.Warning("SimpleMarbles archive upload skipped: payload was not a JSON array.");
            SetStatus("archive payload could not be read.");
            Plugin.ShowToast("SimpleMarbles: archive payload could not be read.", NotificationType.Error);
            return;
        }

        if (games.Count == 0)
        {
            SetStatus("archive contained no games.");
            Plugin.ShowToast("SimpleMarbles: archive contained no games, nothing to upload.", NotificationType.Info);
            return;
        }

        var withDetail = await AttachDetailsAsync(ipc, games);

        PluginLog.Information($"SimpleMarbles upload sending 'archive' payload with {games.Count} game(s), {withDetail} with detail.");

        for (var start = 0; start < games.Count; start += GamesPerRequest)
        {
            var chunk = new JArray(games.Skip(start).Take(GamesPerRequest));
            SetStatus($"uploading games {start + 1}-{start + chunk.Count} of {games.Count}...");

            var body = chunk.ToString(Formatting.None);
            PluginLog.Debug($"SimpleMarbles raw JSON payload: {body}");

            if (!await PostJsonAsync(Plugin.EndpointMarbles, body, "SimpleMarbles archive upload"))
            {
                SetStatus($"upload failed at game {start + 1} of {games.Count}, check /xllog.");
                return;
            }
        }

        SetStatus($"{games.Count} game(s) uploaded, {withDetail} with race results.");
        Plugin.ShowToast($"SimpleMarbles: {games.Count} archived game(s) uploaded \\o/", NotificationType.Success);
    }

    /// <summary>
    /// Fetches the full detail of every game worth having it for and hangs it on the game's row.
    /// A game whose detail cannot be fetched is still uploaded as a plain summary.
    /// Returns how many rows got detail.
    /// </summary>
    private async Task<int> AttachDetailsAsync(SimpleMarblesIpc ipc, JArray games)
    {
        var wanted = games.OfType<JObject>().Where(WantsDetail).ToList();
        var attached = 0;
        var shapeLogged = false;

        for (var i = 0; i < wanted.Count; i++)
        {
            var game = wanted[i];
            var gameId = GameIdOf(game)!;
            SetStatus($"fetching race results {i + 1} of {wanted.Count}...");

            JToken? detail;
            try
            {
                detail = ParseDetail(await ipc.GetGameArchiveAsync(gameId));
            }
            catch (Exception ex)
            {
                // A gate that is missing or has another signature fails the same way for every game.
                PluginLog.Warning($"SimpleMarbles GetGameArchiveIPC failed for game {gameId}, uploading summaries only: {ex}");
                break;
            }

            if (detail is null)
            {
                PluginLog.Warning($"SimpleMarbles returned no usable detail for game {gameId}.");
                continue;
            }

            if (!shapeLogged)
            {
                // Logged once per upload so the detail's shape can be read straight out of /xllog.
                PluginLog.Information($"[SimpleMarbles GetGameArchive] Raw detail of game {gameId}: {detail.ToString(Formatting.None)}");
                shapeLogged = true;
            }

            game["detail"] = detail;
            attached++;
        }

        return attached;
    }

    /// <summary>
    /// Detail is fetched for the dealer's own games that have finished. Other dealers' games do not
    /// count towards this dealer's stats, and a running game has no results yet.
    /// </summary>
    public static bool WantsDetail(JObject game)
    {
        if (GameIdOf(game) is null)
            return false;

        if (game["mine"] is not { Type: JTokenType.Boolean } mine || !mine.Value<bool>())
            return false;

        var status = game["final_status"] is { Type: JTokenType.String } s ? s.Value<string>() : null;
        return !string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GameIdOf(JObject game)
    {
        var id = game["game_id"];
        if (id is null || id.Type is not (JTokenType.String or JTokenType.Integer))
            return null;

        var text = id.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>A game's detail as JSON, or null when SimpleMarbles had nothing to say ("", "{}", "[]", "null").</summary>
    public static JToken? ParseDetail(string? json)
    {
        var token = TryParseToken(json);
        return token is JContainer { HasValues: true } ? token : null;
    }

    private void SetStatus(string message)
    {
        Status = $"{DateTime.Now:HH:mm:ss} {message}";
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

    private static JObject? TryParseObject(string json) => TryParseToken(json) as JObject;

    private static JArray? TryParseArray(string json) => TryParseToken(json) as JArray;

    private static JToken? TryParseToken(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            // Read dates as plain strings. Json.NET would otherwise parse them and write them back reformatted.
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JToken.Load(reader);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleMarbles JSON: {ex}");
            return null;
        }
    }
}
