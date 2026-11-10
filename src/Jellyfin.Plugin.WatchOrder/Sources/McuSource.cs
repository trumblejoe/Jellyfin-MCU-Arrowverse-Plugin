using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.WatchOrder.Sources;

public enum McuOrder
{
    Release,
    Chronological,
    SacredTimeline,

    /// <summary>
    /// For first-time viewers: release order, with films from other universes (Fox, Sony, New Line)
    /// released before the Multiverse Saga moved into one block just before it, and each
    /// Disney+/Netflix/Hulu season kept together. See <see cref="McuSource.BuildHybrid"/>.
    /// </summary>
    Hybrid
}

/// <summary>
/// Reads https://aaronperris.com/mcu/mcu.json — the data file behind the MCU viewing-order page.
/// Anything with a release date in the future is dropped, so unreleased titles never appear.
/// </summary>
public static class McuSource
{
    public const string Url = "https://aaronperris.com/mcu/mcu.json";

    /// <summary>
    /// Names sort in pairs on Jellyfin's playlist page ("MCU Release", "MCU Release Extended", ...)
    /// and stay short because some clients scroll long names.
    /// </summary>
    public static string PlaylistName(McuOrder order, bool extended)
    {
        var name = order switch
        {
            McuOrder.Release => "MCU Release",
            McuOrder.Chronological => "MCU Chronological",
            McuOrder.SacredTimeline => "MCU Sacred Timeline",
            McuOrder.Hybrid => "MCU Hybrid",
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
        return extended ? name + " Extended" : name;
    }

    /// <summary>Spoiler-free description shown on the playlist: what the order is and who it suits.</summary>
    public static string Description(McuOrder order, bool extended)
    {
        var text = order switch
        {
            McuOrder.Hybrid =>
                "Best first watch. Release order, so nothing plays before what it assumes you've seen, starting with Iron Man. " +
                "The older Fox, Sony and Blade films play together just before the Multiverse Saga, and each Disney+ and Netflix season plays straight through.",
            McuOrder.Release =>
                "The MCU in the order it came out, including the Netflix and ABC shows, the older Fox and Sony films and the web shorts. " +
                "Spoiler-safe for a first watch, but it opens with the older films before Iron Man, and some films land in the middle of a series.",
            McuOrder.SacredTimeline =>
                "Best for a rewatch. The main MCU in story order through Endgame, then the stories from other universes. " +
                "Not for a first watch: prequels made later play early and can give things away.",
            McuOrder.Chronological =>
                "For a rewatch. Everything in story order, with the stories from other universes woven into the timeline. " +
                "Not for a first watch: prequels made later play early and can give things away.",
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
        if (extended)
        {
            text += " Extended: also includes the non-canon entries the guide lists, such as the older Fox series and the 90s animated shows.";
        }

        return text + " Order from aaronperris.com/mcu; updated automatically.";
    }

    /// <summary>Names used before 1.2 (renamed once); null for orders added later.</summary>
    public static string? LegacyPlaylistName(McuOrder order) => order switch
    {
        McuOrder.Release => "MCU - Release Order",
        McuOrder.Chronological => "MCU - Chronological Order",
        McuOrder.SacredTimeline => "MCU - Sacred Timeline Order",
        _ => null
    };

    /// <summary>The Multiverse Saga's first release (WandaVision, Phase 4).</summary>
    public static readonly DateOnly MultiverseSagaStart = new(2021, 1, 15);

    /// <summary>Released one week at a time or all at once on a streaming service: kept together.</summary>
    private static readonly HashSet<string> StreamingTypes = new(StringComparer.OrdinalIgnoreCase) { "Disney+", "Netflix", "Hulu" };

    /// <summary>
    /// MCU Hybrid, for first-time viewers:
    /// 1. Release order (spoiler-safe: nothing plays before what it assumes you've seen).
    /// 2. Items from other universes (multiverse != 0: Fox X-Men, Raimi's Spider-Man, Blade, ...)
    ///    released before the Multiverse Saga form one block, in their own release order, just
    ///    before it starts, so newcomers start with Iron Man and the Infinity Saga runs straight
    ///    through. Later ones stay at their release dates.
    /// 3. Each Disney+/Netflix/Hulu season plays straight through, at its premiere; broadcast shows
    ///    (ABC etc.) keep air-date order so tie-in episodes follow the films they react to.
    /// </summary>
    public static IEnumerable<McuItem> BuildHybrid(IEnumerable<McuItem> released)
    {
        var blockDate = MultiverseSagaStart.AddDays(-1);
        var ordered = released
            .OrderBy(i => i.Multiverse != 0 && i.ReleaseDate < MultiverseSagaStart ? blockDate : i.ReleaseDate)
            .ThenBy(i => i.ReleaseDate)
            .ThenBy(i => i.SourceIndex)
            .ToList();

        // Keep each streaming season together, at the position of its first episode.
        var seasons = ordered
            .Where(i => i.Show is not null && i.Season is not null && StreamingTypes.Contains(i.Type ?? string.Empty))
            .GroupBy(i => (i.Show, i.Season))
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Episode).ToList());
        var placed = new HashSet<(string?, int?)>();
        foreach (var item in ordered)
        {
            if (item.Show is not null && seasons.TryGetValue((item.Show, item.Season), out var season))
            {
                if (placed.Add((item.Show, item.Season)))
                {
                    foreach (var episode in season)
                    {
                        yield return episode;
                    }
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    public static IReadOnlyList<McuItem> ParseItems(string json)
    {
        var items = JsonSerializer.Deserialize<List<McuItem>>(json)
            ?? throw new InvalidDataException("mcu.json was empty.");
        if (items.Count == 0)
        {
            throw new InvalidDataException("mcu.json had no items; the data format may have changed.");
        }

        // Episodes are matched by show/season/episode, so their titles are dropped immediately
        // and never kept in memory, logs, or the saved copy.
        foreach (var item in items.Where(i => i.Show is not null))
        {
            item.Title = null;
        }

        return Index(items);
    }

    /// <summary>Records each item's position in the feed (tie-break for same-day releases).</summary>
    public static IReadOnlyList<McuItem> Index(IReadOnlyList<McuItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SourceIndex = i;
        }

        return items;
    }

    /// <param name="extended">false = canon only; true = canon plus the guide's non-canon entries.</param>
    public static WatchOrderList Build(IReadOnlyList<McuItem> items, McuOrder order, bool extended, DateOnly today)
    {
        var released = items
            .Where(i => extended || i.Canon)
            .Where(i => i.ReleaseDate is { } d && d <= today);

        IEnumerable<McuItem> sorted = order switch
        {
            McuOrder.Release => released.OrderBy(i => i.ReleaseDate).ThenBy(i => i.SourceIndex),
            McuOrder.Chronological => released.OrderBy(i => i.TimelineOrder),
            McuOrder.SacredTimeline => released.OrderBy(i => i.SacredTimeline),
            McuOrder.Hybrid => BuildHybrid(released),
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };

        var entries = sorted.Select(i => i.Show is not null && i.Season is not null && i.Episode is not null
                ? new WatchOrderEntry(i.Show, i.Season, i.Episode, null, i.ReleaseDate?.Year)
                : new WatchOrderEntry(null, null, null, i.Title, i.ReleaseDate?.Year))
            .ToList();

        return new WatchOrderList(PlaylistName(order, extended), entries) { Description = Description(order, extended) };
    }

    public sealed class McuItem
    {
        // Movies/specials only (cleared for episodes). Used to match the library; never logged.
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

        /// <summary>The guide's universe number: 0 = the main MCU, anything else = another universe.</summary>
        [JsonPropertyName("multiverse")]
        public int Multiverse { get; set; }

        /// <summary>Where it was released, e.g. "Marvel Studios", "Disney+", "Netflix", "ABC", "Fox".</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonIgnore]
        public int SourceIndex { get; set; }

        [JsonIgnore]
        public DateOnly? ReleaseDate =>
            DateOnly.TryParseExact(ReleaseDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
