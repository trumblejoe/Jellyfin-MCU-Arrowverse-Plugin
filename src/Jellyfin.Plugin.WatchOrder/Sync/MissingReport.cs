using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// What the enabled playlists include that isn't in the library, for the settings page.
/// Derived on demand from the saved guide copies and the current library (nothing is stored).
///
/// Spoiler policy: show names, counts, and season numbers only. Movie titles are never
/// included, only counts.
/// </summary>
public sealed class MissingReport
{
    /// <summary>False if no guide has been saved yet (no sync has run).</summary>
    public bool HasData { get; init; }

    public DateTime? ArrowverseSavedUtc { get; init; }

    public DateTime? McuSavedUtc { get; init; }

    public List<PlaylistSummary> Playlists { get; init; } = [];

    /// <summary>Shows with missing episodes, entirely-missing shows first, then by name.</summary>
    public List<ShowSummary> Shows { get; init; } = [];

    /// <summary>Distinct movies/specials across the enabled playlists that aren't in the library.</summary>
    public int MissingMovies { get; init; }

    public sealed record PlaylistSummary(string Name, int Total, int InLibrary, int MissingEpisodes, int MissingMovies);

    /// <param name="Name">Show name as the guide spells it (the name to use in a Series Alias).</param>
    /// <param name="InLibrary">False = the show isn't in the library at all (or under a different name).</param>
    /// <param name="MissingEpisodes">Distinct episodes missing across all enabled playlists.</param>
    /// <param name="Seasons">Season numbers with missing episodes.</param>
    public sealed record ShowSummary(string Name, bool InLibrary, int MissingEpisodes, List<int> Seasons);

    /// <param name="lists">The enabled playlists, built from the saved guide copies.</param>
    /// <param name="match">Library item for an entry, or null if missing.</param>
    /// <param name="hasSeries">Whether the library has the show (by guide name and air year).</param>
    public static MissingReport Build(
        IReadOnlyList<WatchOrderList> lists,
        Func<WatchOrderEntry, Guid?> match,
        Func<string, int?, bool> hasSeries,
        DateTime? arrowverseSavedUtc,
        DateTime? mcuSavedUtc)
    {
        var playlists = new List<PlaylistSummary>();
        var missingEpisodes = new HashSet<(string Show, int Season, int Episode)>();
        var showYears = new Dictionary<string, int?>();
        var missingMovies = new HashSet<(string? Title, int? Year)>();

        foreach (var list in lists)
        {
            int inLibrary = 0, eps = 0, movies = 0;
            foreach (var entry in list.Entries)
            {
                if (match(entry) is not null)
                {
                    inLibrary++;
                }
                else if (entry.IsEpisode)
                {
                    eps++;
                    missingEpisodes.Add((entry.SeriesName!, entry.Season!.Value, entry.Episode!.Value));
                    showYears.TryAdd(entry.SeriesName!, entry.Year);
                }
                else
                {
                    movies++;
                    missingMovies.Add((entry.MovieTitle, entry.Year));
                }
            }

            playlists.Add(new PlaylistSummary(list.PlaylistName, list.Entries.Count, inLibrary, eps, movies));
        }

        var shows = missingEpisodes
            .GroupBy(e => e.Show)
            .Select(g => new ShowSummary(
                g.Key,
                hasSeries(g.Key, showYears[g.Key]),
                g.Count(),
                g.Select(e => e.Season).Distinct().Order().ToList()))
            .OrderBy(s => s.InLibrary)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MissingReport
        {
            HasData = arrowverseSavedUtc is not null || mcuSavedUtc is not null,
            ArrowverseSavedUtc = arrowverseSavedUtc,
            McuSavedUtc = mcuSavedUtc,
            Playlists = playlists,
            Shows = shows,
            MissingMovies = missingMovies.Count
        };
    }
}
