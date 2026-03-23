using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace sbjStats;

public sealed class ScratchGameEndedPayload
{
    [JsonProperty("player_id")]
    public string? PlayerId { get; set; }

    [JsonProperty("player_name")]
    public string? PlayerName { get; set; }

    [JsonProperty("wins")]
    public string? Wins { get; set; }

    [JsonProperty("total_cards")]
    public string? TotalCards { get; set; }

    [JsonProperty("PrizesWon")]
    public bool? ThemeName { get; set; }
}

public sealed class ScratchArchiveSnapshot
{
    [JsonProperty("players")]
    public List<ScratchArchivePlayer> Players { get; set; } = [];

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraFields { get; set; }
}

public sealed class ScratchArchivePlayer
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("ticketCost")]
    public int? TicketCost { get; set; }

    [JsonProperty("payout")]
    public int? Payout { get; set; }

    [JsonProperty("completedAt")]
    public long? CompletedAtUnixMilliseconds { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraFields { get; set; }
}

public sealed class ScratchUploadRequest
{
    public string UploadType { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public string? GameId { get; set; }
    public string? PlayerName { get; set; }
    public long? OccurredAtUnixMilliseconds { get; set; }
}
