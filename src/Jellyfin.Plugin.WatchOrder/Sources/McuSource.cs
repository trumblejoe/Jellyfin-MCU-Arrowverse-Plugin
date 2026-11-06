using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.WatchOrder.Sources;

public enum McuOrder
{
    Release,
    Chronological,
    SacredTimeline
}

/// <summary>
/// Reads https://aaronperris.com/mcu/mcu.json — the data file behind the MCU viewing-order page.
/// Anything with a release date in the future is dropped, so unreleased titles never appear.
/// </summary>
public static class McuSource
{
    public const string Url = "https://aaronperris.com/mcu/mcu.json";

    public static string PlaylistName(McuOrder order) => order switch
    {
        McuOrder.Release => "MCU - Release Order",
        McuOrder.Chronological => "MCU - Chronological Order",
        McuOrder.SacredTimeline => "MCU - Sacred Timeline Order",
        _ => throw new ArgumentOutOfRangeException(nameof(order))
    };

    public static IReadOnlyList<McuItem> ParseItems(string json)
    {
        var items = JsonSerializer.Deserialize<List<McuItem>>(json)
            ?? throw new InvalidDataException("mcu.json was empty.");
        if (items.Count == 0)
        {
            throw new InvalidDataException("mcu.json had no items; the data format may have changed.");
        }

        for (var i = 0; i < items.Count; i++)
        {
            items[i].SourceIndex = i;
        }

        return items;
    }

    public static WatchOrderList Build(IReadOnlyList<McuItem> items, McuOrder order, bool canonOnly, DateOnly today)
    {
        var released = items
            .Where(i => !canonOnly || i.Canon)
            .Where(i => i.ReleaseDate is { } d && d <= today);

        IEnumerable<McuItem> sorted = order switch
        {
            McuOrder.Release => released.OrderBy(i => i.ReleaseDate).ThenBy(i => i.SourceIndex),
            McuOrder.Chronological => released.OrderBy(i => i.TimelineOrder),
            McuOrder.SacredTimeline => released.OrderBy(i => i.SacredTimeline),
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };

        var entries = sorted.Select(i => i.Show is not null && i.Season is not null && i.Episode is not null
                ? new WatchOrderEntry(i.Show, i.Season, i.Episode, null, i.ReleaseDate?.Year)
                : new WatchOrderEntry(null, null, null, i.Title, i.ReleaseDate?.Year))
            .ToList();

        return new WatchOrderList(PlaylistName(order), entries);
    }

    public sealed class McuItem
    {
        // Only used to match movies/specials to the library; never logged.
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDateRaw { get; set; }

        [JsonPropertyName("show")]
        public string? Show { get; set; }

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("canon")]
        public bool Canon { get; set; }

        [JsonPropertyName("timeline_order")]
        public int TimelineOrder { get; set; }

        [JsonPropertyName("sacred_timeline")]
        public int SacredTimeline { get; set; }

        [JsonIgnore]
        public int SourceIndex { get; set; }

        [JsonIgnore]
        public DateOnly? ReleaseDate =>
            DateOnly.TryParseExact(ReleaseDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
