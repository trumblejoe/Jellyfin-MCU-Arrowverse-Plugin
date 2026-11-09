using Jellyfin.Plugin.WatchOrder.Sources;
using Jellyfin.Plugin.WatchOrder.Sync;

namespace Jellyfin.Plugin.WatchOrder.Tests;

public class BuiltInListsTests
{
    [Fact]
    public void Parses_shows_codes_ranges_movies_and_years()
    {
        var list = BuiltInLists.Parse("""
            # comment
            playlist: Test List

            @ Show A
            S01E03
            S01E01-E02
            @ Show B (2014)
            S02E05-S02E06
            movie: Some Movie (1993)
            """);

        Assert.Equal("Test List", list.PlaylistName);
        Assert.Equal(
            ["Show A 1x3", "Show A 1x1", "Show A 1x2", "Show B 2x5 2014", "Show B 2x6 2014", "movie 1993"],
            list.Entries.Select(e => e.IsEpisode ? $"{e.SeriesName} {e.Season}x{e.Episode}{(e.Year is null ? "" : " " + e.Year)}" : $"movie {e.Year}"));
        Assert.Equal("Some Movie", list.Entries[^1].MovieTitle);
    }

    [Theory]
    [InlineData("@ Show\nS01E01")]                               // no playlist line
    [InlineData("playlist: X\nS01E01")]                          // code before any show
    [InlineData("playlist: X\n@ Show\nS01E05-E02")]              // backwards range
    [InlineData("playlist: X\n@ Show\nS01E01-S02E03")]           // range across seasons
    [InlineData("playlist: X\n@ Show\nsomething else")]          // unknown line
    public void Rejects_malformed_lists(string text)
    {
        Assert.Throws<InvalidDataException>(() => BuiltInLists.Parse(text));
    }

    [Fact]
    public void Every_shipped_list_loads_with_a_unique_name_and_no_duplicate_episodes()
    {
        Assert.NotEmpty(BuiltInLists.All);
        Assert.Equal(BuiltInLists.All.Count, BuiltInLists.All.Select(l => l.PlaylistName.ToLowerInvariant()).Distinct().Count());
        foreach (var list in BuiltInLists.All)
        {
            var episodes = list.Entries.Where(e => e.IsEpisode).Select(e => (e.SeriesName, e.SeriesYear, e.Season, e.Episode)).ToList();
            Assert.Equal(episodes.Count, episodes.Distinct().Count());
        }
    }

    [Fact]
    public void Batman_list_has_every_episode_of_both_shows_plus_the_two_movie_specials()
    {
        var list = BuiltInLists.All.Single(l => l.PlaylistName == "Batman TAS Fan Order");
        var btas = list.Entries.Where(e => e.SeriesName == "Batman: The Animated Series").ToList();
        var tnba = list.Entries.Where(e => e.SeriesName == "The New Batman Adventures").ToList();

        Assert.Equal(111, list.Entries.Count);
        Assert.Equal(87, btas.Count);                                     // 85 episodes + 2 specials
        Assert.Equal(2, btas.Count(e => e.Season == 0));
        Assert.Equal(new[] { 60, 10, 10, 5 }, new[] { 1, 2, 3, 4 }.Select(s => btas.Count(e => e.Season == s)));
        Assert.Equal(new[] { 13, 11 }, new[] { 1, 2 }.Select(s => tnba.Count(e => e.Season == s)));
        Assert.All(list.Entries.Skip(87), e => Assert.Equal("The New Batman Adventures", e.SeriesName)); // TNBA comes last
    }

    [Fact]
    public void Movie_alternate_titles_are_parsed()
    {
        var list = BuiltInLists.Parse("playlist: X\nmovie: Main Title (2001) | Other Title | Third");
        var movie = Assert.Single(list.Entries);

        Assert.Equal(("Main Title", 2001), (movie.MovieTitle, movie.Year));
        Assert.Equal(["Other Title", "Third"], movie.MovieAltTitles!);
    }

    private static WatchOrderList List(string name) => BuiltInLists.All.Single(l => l.PlaylistName == name);

    private static int Count(WatchOrderList list, string show) => list.Entries.Count(e => e.SeriesName == show);

    [Fact]
    public void Ships_every_requested_list()
    {
        Assert.Equal(
            [
                "Batman TAS Fan Order", "Battlestar Galactica Release", "Breaking Bad Release", "DCAU Release", "DCU Release",
                "Doctor Who Release", "Fast & Furious Chronological", "Fast & Furious Release", "Harry Potter Release",
                "Middle-earth Chronological", "Middle-earth Release", "One Chicago Release", "Saved by the Bell Chronological",
                "Star Wars Chronological", "Star Wars Release", "Stargate Optimized", "Wizarding World Chronological",
                "Wizarding World Release"
            ],
            BuiltInLists.All.Select(l => l.PlaylistName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Show_line_year_becomes_a_strict_premiere_year()
    {
        var list = BuiltInLists.Parse("playlist: X\n@ Doctor Who (2023)\nS01E01\n@ Torchwood\nS01E01");

        Assert.Equal(("Doctor Who", 2023), (list.Entries[0].SeriesName, list.Entries[0].SeriesYear));
        Assert.Null(list.Entries[1].SeriesYear);
    }

    [Fact]
    public void Newer_lists_have_the_expected_size()
    {
        Assert.Equal(303, List("Doctor Who Release").Entries.Count);
        Assert.Equal(21, List("Doctor Who Release").Entries.Count(e => e.SeriesYear == 2023)); // 16 episodes + 5 specials
        Assert.Equal(810, List("One Chicago Release").Entries.Count);
        Assert.Equal(4, Count(List("One Chicago Release"), "Law & Order: Special Victims Unit"));
        Assert.Equal(126, List("Battlestar Galactica Release").Entries.Count);
        Assert.Equal(126, List("Breaking Bad Release").Entries.Count);
        Assert.Equal(23, List("Middle-earth Release").Entries.Count);
        Assert.Equal(23, List("Middle-earth Chronological").Entries.Count);
        Assert.Equal(11, List("Wizarding World Release").Entries.Count);
        Assert.Equal(11, List("Fast & Furious Chronological").Entries.Count);
    }

    [Fact]
    public void Chronological_and_release_versions_contain_the_same_items()
    {
        foreach (var franchise in new[] { "Middle-earth", "Wizarding World", "Fast & Furious" })
        {
            var release = List(franchise + " Release").Entries.Select(e => e.IsEpisode ? $"{e.SeriesName}|{e.Season}|{e.Episode}" : e.MovieTitle!).Order();
            var chrono = List(franchise + " Chronological").Entries.Select(e => e.IsEpisode ? $"{e.SeriesName}|{e.Season}|{e.Episode}" : e.MovieTitle!).Order();
            Assert.Equal(release, chrono);
        }
    }

    [Fact]
    public void Star_wars_release_and_chronological_cover_the_same_episodes_and_movies()
    {
        static HashSet<string> Keys(WatchOrderList l) =>
            l.Entries.Select(e => e.IsEpisode ? $"{e.SeriesName}|{e.Season}|{e.Episode}" : $"movie|{e.MovieTitle}|{e.Year}").ToHashSet();

        var release = List("Star Wars Release");
        var chrono = List("Star Wars Chronological");
        Assert.Equal(414, release.Entries.Count);
        Assert.Equal(414, chrono.Entries.Count);
        Assert.True(Keys(release).SetEquals(Keys(chrono)));
        Assert.Equal(133, Count(chrono, "Star Wars: The Clone Wars"));
    }

    [Fact]
    public void Dcau_has_every_episode_of_every_series_plus_four_movies()
    {
        var dcau = List("DCAU Release");
        Assert.Equal(85, Count(dcau, "Batman: The Animated Series"));
        Assert.Equal(54, Count(dcau, "Superman: The Animated Series"));
        Assert.Equal(24, Count(dcau, "The New Batman Adventures"));
        Assert.Equal(52, Count(dcau, "Batman Beyond"));
        Assert.Equal(52, Count(dcau, "Static Shock"));
        Assert.Equal(26, Count(dcau, "The Zeta Project"));
        Assert.Equal(91, Count(dcau, "Justice League")); // includes Unlimited (TVDB seasons 3-5)
        Assert.Equal(4, dcau.Entries.Count(e => !e.IsEpisode));
    }

    [Fact]
    public void Smaller_lists_have_the_expected_size()
    {
        Assert.Equal(8, List("Harry Potter Release").Entries.Count);
        Assert.Equal(35, List("DCU Release").Entries.Count);
        Assert.Equal(87, List("Saved by the Bell Chronological").Entries.Count);
        Assert.Equal(86, Count(List("Saved by the Bell Chronological"), "Saved by the Bell"));

        var stargate = List("Stargate Optimized");
        Assert.Equal(392, stargate.Entries.Count);
        Assert.Equal(214, Count(stargate, "Stargate SG-1"));
        Assert.Equal(100, Count(stargate, "Stargate Atlantis"));
        Assert.Equal(74, Count(stargate, "Stargate Universe")); // 40 episodes + 34 kino specials
    }

    [Fact]
    public void Built_in_playlists_are_managed()
    {
        Assert.Contains("Batman TAS Fan Order", ManagedPlaylists.Names);
    }
}
