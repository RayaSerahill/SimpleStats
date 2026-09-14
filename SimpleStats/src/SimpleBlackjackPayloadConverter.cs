using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ECommons.Logging;
using Newtonsoft.Json.Linq;

namespace sbjStats;

/// <summary>
/// Converts whatever SimpleBlackjack sends over IPC into our StatsRecording.
/// Dalamud hands us the sender's payload as JSON when we subscribe with <c>object</c>.
/// Hand members are matched by name when the expected names are present, and by
/// position and type otherwise, because newer SimpleBlackjack builds ship with
/// obfuscated hand member names that change between releases.
/// </summary>
public static class SimpleBlackjackPayloadConverter
{
    private static readonly string[] ExpectedHandNames =
        [nameof(HandStat.PlayerName), nameof(HandStat.Cards), nameof(HandStat.SplitNum), nameof(HandStat.Bet),
         nameof(HandStat.Payout), nameof(HandStat.IsDoubleDown), nameof(HandStat.Result), nameof(HandStat.Dealer)];

    public static List<StatsRecording> ToRecordings(object? payload, string source)
    {
        switch (payload)
        {
            case null:
                return [];
            case StatsRecording single:
                return [single];
            case IEnumerable<StatsRecording> typed:
                return typed.ToList();
        }

        var token = payload as JToken ?? JToken.FromObject(payload);
        var records = token switch
        {
            JArray array => array.OfType<JObject>().ToList(),
            JObject obj => [obj],
            _ => [],
        };

        var fallbackHands = 0;
        var result = new List<StatsRecording>(records.Count);
        foreach (var record in records)
            result.Add(ToRecording(record, ref fallbackHands));

        if (fallbackHands > 0)
            PluginLog.Warning($"SimpleBlackjack {source}: hand member names did not match HandStat, mapped {fallbackHands} hand(s) by position.");

        return result;
    }

    public static StatsRecording ToRecording(object? payload, string source)
    {
        return ToRecordings(payload, source).FirstOrDefault() ?? new StatsRecording();
    }

    private static StatsRecording ToRecording(JObject record, ref int fallbackHands)
    {
        var handsToken = record[nameof(StatsRecording.Hands)];
        record.Remove(nameof(StatsRecording.Hands));
        var recording = record.ToObject<StatsRecording>() ?? new StatsRecording();
        recording.Hands = [];

        if (handsToken is not JArray hands)
            return recording;

        foreach (var hand in hands.OfType<JObject>())
        {
            if (ExpectedHandNames.Any(name => hand[name] is not null))
            {
                recording.Hands.Add(hand.ToObject<HandStat>() ?? new HandStat());
                continue;
            }

            fallbackHands++;
            recording.Hands.Add(HandFromPosition(hand));
        }

        return recording;
    }

    /// <summary>
    /// Known layout: PlayerName, Cards, SplitNum, Bet, Payout, IsDoubleDown, Result, Dealer, then any newer fields.
    /// </summary>
    private static HandStat HandFromPosition(JObject hand)
    {
        var values = hand.Properties().Select(p => p.Value).ToList();
        var stat = new HandStat();

        stat.PlayerName = At(values, 0)?.Type == JTokenType.String ? At(values, 0)!.Value<string>() ?? string.Empty : string.Empty;
        stat.Cards = At(values, 1) is JArray cards
            ? cards.Where(c => c.Type == JTokenType.Integer).Select(c => (Card)c.Value<int>()).ToList()
            : [];
        stat.SplitNum = Int(values, 2);
        stat.Bet = Int(values, 3);
        stat.Payout = Int(values, 4);
        stat.IsDoubleDown = Bool(values, 5);
        stat.Result = (Result)Int(values, 6);
        stat.Dealer = Bool(values, 7);
        return stat;
    }

    private static JToken? At(List<JToken> values, int index) => index < values.Count ? values[index] : null;

    private static int Int(List<JToken> values, int index) =>
        At(values, index) is { Type: JTokenType.Integer } t ? t.Value<int>() : 0;

    private static bool Bool(List<JToken> values, int index) =>
        At(values, index) is { Type: JTokenType.Boolean } t && t.Value<bool>();
}
