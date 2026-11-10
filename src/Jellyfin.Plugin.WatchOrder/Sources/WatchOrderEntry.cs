namespace Jellyfin.Plugin.WatchOrder.Sources;

/// <summary>
/// One step in a watch order. Deliberately carries no episode titles or synopses:
/// episodes are identified by show + season + episode number only.
/// </summary>
/// <param name="SeriesName">Show name, or null for a movie/special.</param>
/// <param name="Season">Season number for episodes.</param>
/// <param name="Episode">Episode number for episodes.</param>
/// <param name="MovieTitle">Movie title, only set for non-episode entries.</param>
/// <param name="Year">Release/air year, used to tell apart same-named shows or movies.</param>
/// <param name="MovieAltTitles">Other titles the movie is known by (e.g. regional titles).</param>
public sealed record WatchOrderEntry(
    string? SeriesName,
    int? Season,
    int? Episode,
    string? MovieTitle,
    int? Year,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? MovieAltTitles = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? SeriesYear = null)
{
    // SeriesYear: the show's premiere year, from a built-in list's "@ Show (2023)" line. Unlike
    // Year (a hint), it's strict: only a series that premiered that year matches, so e.g.
    // "Doctor Who (2023)" can never match "Doctor Who (2005)" and play the wrong episodes.

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEpisode => SeriesName is not null && Season is not null && Episode is not null;
}

/// <summary>A playlist to build: its name and its ordered entries.</summary>
public sealed record WatchOrderList(string PlaylistName, IReadOnlyList<WatchOrderEntry> Entries)
{
    /// <summary>Built-in lists only: true if the franchise has more coming, false if finished.</summary>
    public bool? Ongoing { get; init; }

    /// <summary>
    /// Spoiler-free description shown on the playlist in Jellyfin (its overview) and on the settings
    /// page: what the order is and who it's best for.
    /// </summary>
    public string? Description { get; init; }
}
