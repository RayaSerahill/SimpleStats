using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using ECommons.Logging;
using Newtonsoft.Json;
using Serilog;

namespace sbjStats;

public sealed class ScratchUploadHandler : GameUploadHandlerBase
{
    public ScratchUploadHandler(Plugin plugin) : base(plugin)
    {
    }

    public void HandleGameEnded(string json)
    {
        if (!IsLiveUploadEnabled("SimpleScratch") || !HasUploadConfiguration("SimpleScratch", notifyUser: false))
            return;

        _ = UploadLiveRoundAsync(json);
    }

    public async Task UploadExistingAsync(SimpleScratchIpc ipc)
    {
        if (!HasUploadConfiguration("SimpleScratch", notifyUser: true))
            return;

        var archiveJson = await ipc.GetArchiveAsync();
        if (string.IsNullOrWhiteSpace(archiveJson))
        {
            Plugin.ShowToast("SimpleScratch: archive payload was empty.", Dalamud.Interface.ImGuiNotification.NotificationType.Info);
            return;
        }

        await UploadArchiveSnapshotAsync(archiveJson);
        Plugin.ShowToast("SimpleScratch archive upload skeleton ran. Wire the transport in ScratchUploadHandler when the payload format is finalized.", Dalamud.Interface.ImGuiNotification.NotificationType.Info);
    }

    public ScratchGameEndedPayload? ParseGameEndedPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<ScratchGameEndedPayload>(json);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleScratch game-ended payload: {ex}");
            return null;
        }
    }

    public string? ParseArchiveSnapshot(string json)
    {
        Log.Information("Parsing SimpleScratch archive snapshot.");
        Log.Information($"Raw archive JSON: {json}");
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return json;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to parse SimpleScratch archive payload: {ex}");
            return null;
        }
    }

    public async Task UploadLiveRoundAsync(string json)
    {
        try
        {
            var payload = ParseGameEndedPayload(json);
            if (payload is null)
            {
                PluginLog.Warning("SimpleScratch live upload skipped: payload could not be parsed.");
                return;
            }

            var request = BuildLiveUploadRequest(payload, json);
            if (request is null)
            {
                PluginLog.Warning("SimpleScratch live upload skipped: request could not be built.");
                return;
            }

            await SendScratchUploadAsync(request);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"SimpleScratch live upload failed: {ex}");
        }
    }

    public ScratchUploadRequest? BuildLiveUploadRequest(ScratchGameEndedPayload payload, string rawJson)
    {
        return new ScratchUploadRequest
        {
            RawJson = rawJson,
        };
    }

    public ScratchUploadRequest? BuildArchiveUploadRequest(string snapshot, string rawJson)
    {
        return new ScratchUploadRequest
        {
            RawJson = snapshot,
        };
    }

    public async Task SendScratchUploadAsync(ScratchUploadRequest request)
    {
        PluginLog.Information(
            $"SimpleScratch upload skeleton reached for '{request.UploadType}' with player '{request.PlayerName ?? "<unknown>"}' and game '{request.GameId ?? "<unknown>"}'.");
        Log.Information($"Raw JSON payload: {request.RawJson}");
        
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Plugin.Configuration.ApiKey);
        
        using var content = new StringContent(request.RawJson, Encoding.UTF8, "application/json");
        
        var response = await client.PostAsync(Plugin.EndpointScratch.Trim(), content);
        var responseText = await response.Content.ReadAsStringAsync();
        
        if (!response.IsSuccessStatusCode)
        {
            var msg = $"Upload failed: {(int)response.StatusCode} {response.ReasonPhrase} | {responseText}";
            Plugin.Instance?.ShowToast(msg, Dalamud.Interface.ImGuiNotification.NotificationType.Error);
        }
        else
        {
            var msg = $"Stats uploaded successfully \\o/";
            Plugin.Instance?.ShowToast(msg, Dalamud.Interface.ImGuiNotification.NotificationType.Success);
        }
        
        await Task.CompletedTask;
    }

    public async Task UploadArchiveSnapshotAsync(string archiveJson)
    {
        var snapshot = ParseArchiveSnapshot(archiveJson);
        if (snapshot is null)
        {
            PluginLog.Warning("SimpleScratch archive upload skipped: payload could not be parsed.");
            return;
        }

        var request = BuildArchiveUploadRequest(snapshot, archiveJson);
        if (request is null)
        {
            PluginLog.Warning("SimpleScratch archive upload skipped: request could not be built.");
            return;
        }

        await SendScratchUploadAsync(request);
        PluginLog.Information($"SimpleScratch archive snapshot parsed.");
    }
}
