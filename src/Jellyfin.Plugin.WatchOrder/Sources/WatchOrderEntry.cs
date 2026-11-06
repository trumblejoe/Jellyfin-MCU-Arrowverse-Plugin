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
public sealed record WatchOrderEntry(string? SeriesName, int? Season, int? Episode, string? MovieTitle, int? Year)
{
    public bool IsEpisode => SeriesName is not null && Season is not null && Episode is not null;
}

/// <summary>A playlist to build: its name and its ordered entries.</summary>
public sealed record WatchOrderList(string PlaylistName, IReadOnlyList<WatchOrderEntry> Entries);
