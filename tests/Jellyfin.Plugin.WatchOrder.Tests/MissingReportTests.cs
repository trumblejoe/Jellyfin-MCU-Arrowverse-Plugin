using System.Text.Json;
using Jellyfin.Plugin.WatchOrder.Sources;
using Jellyfin.Plugin.WatchOrder.Sync;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// Sample data is made up. Never paste real guide content (titles) into tests.
public class MissingReportTests
{
    private static WatchOrderEntry Ep(string show, int s, int e) => new(show, s, e, null, 2020);

    private static WatchOrderEntry Movie(string title) => new(null, null, null, title, 2020);

    // Library: has "Owned Show" S01E01 only, and "Owned Movie".
    private static Guid? Match(WatchOrderEntry e) =>
        (e.SeriesName == "Owned Show" && e.Season == 1 && e.Episode == 1) || e.MovieTitle == "Owned Movie" ? Guid.NewGuid() : null;

    private static bool HasSeries(WatchOrderEntry e) => e.SeriesName == "Owned Show";

    private static MissingReport Build(params WatchOrderList[] lists) =>
        MissingReport.Build(lists, Match, HasSeries, DateTime.UtcNow, null);

    [Fact]
    public void Summarizes_each_playlist()
    {
        var report = Build(new WatchOrderList("P1", [Ep("Owned Show", 1, 1), Ep("Owned Show", 2, 3), Movie("Owned Movie"), Movie("Other Movie")]));

        var p = Assert.Single(report.Playlists);
        Assert.Equal(("P1", 4, 2, 1, 1), (p.Name, p.Total, p.InLibrary, p.MissingEpisodes, p.MissingMovies));
    }

    [Fact]
    public void Lists_unowned_shows_first_then_partial_shows_with_seasons()
    {
        var report = Build(new WatchOrderList("P1", [Ep("Owned Show", 1, 1), Ep("Owned Show", 3, 1), Ep("Owned Show", 2, 5), Ep("Zed Show", 1, 1), Ep("Absent Show", 1, 1)]));

        Assert.Equal(["Absent Show", "Zed Show", "Owned Show"], report.Shows.Select(s => s.Name));
        var owned = report.Shows.Single(s => s.Name == "Owned Show");
        Assert.True(owned.InLibrary);
        Assert.Equal(2, owned.MissingEpisodes);
        Assert.Equal([2, 3], owned.Seasons);
    }

    [Fact]
    public void The_same_episode_in_several_playlists_counts_once()
    {
        var shared = new List<WatchOrderEntry> { Ep("Absent Show", 1, 1), Movie("Other Movie") };
        var report = Build(new WatchOrderList("Canon", shared), new WatchOrderList("Extended", shared));

        Assert.Equal(1, report.Shows.Single().MissingEpisodes);
        Assert.Equal(1, report.MissingMovies);
        Assert.Equal(2, report.Playlists.Count);
    }

    [Fact]
    public void Never_exposes_movie_titles()
    {
        var report = Build(new WatchOrderList("P1", [Movie("Secret Movie Name")]));

        Assert.DoesNotContain("Secret Movie Name", JsonSerializer.Serialize(report));
        Assert.Equal(1, report.MissingMovies);
    }

    [Fact]
    public void No_saved_guides_means_no_data()
    {
        Assert.False(MissingReport.Build([], Match, HasSeries, null, null).HasData);
    }
}
