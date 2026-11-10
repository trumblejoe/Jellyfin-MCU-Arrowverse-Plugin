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
                "Alien & Predator Chronological", "Alien & Predator Release", "Avatar Release",
                "Batman TAS Fan Order", "Battlestar Galactica Release", "Breaking Bad Release", "DCAU Release", "DCU Release",
                "Doctor Who Release", "Fast & Furious Chronological", "Fast & Furious Release", "Harry Potter Release",
                "Hunger Games Chronological", "Hunger Games Release",
                "Middle-earth Chronological", "Middle-earth Release", "One Chicago Release", "Saved by the Bell Chronological",
                "Star Wars Chronological", "Star Wars Release", "Stargate Optimized", "The Wire Release",
                "Transformers Chronological", "Transformers Release",
                "Wizarding World Chronological", "Wizarding World Release"
            ],
            BuiltInLists.All.Select(l => l.PlaylistName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_list_says_whether_it_is_ongoing_or_finished()
    {
        Assert.All(BuiltInLists.All, l => Assert.NotNull(l.Ongoing));
        Assert.True(List("One Chicago Release").Ongoing);
        Assert.False(List("The Wire Release").Ongoing);
        Assert.Throws<InvalidDataException>(() => BuiltInLists.Parse("playlist: X\nupdates: maybe"));
        Assert.True(BuiltInLists.Parse("playlist: X\nupdates: auto").Ongoing);
    }

    [Fact]
    public void Upcoming_lines_are_not_playlist_items_but_need_a_date()
    {
        Assert.Empty(BuiltInLists.Parse(
            "playlist: X\nupcoming: Some Movie (2027-05-28)\nupcoming: Other (2027)\n" +
            "upcoming: movie: Third (2027-03-17) | alt: 3rd | after: Earlier Movie").Entries);
        Assert.Throws<InvalidDataException>(() => BuiltInLists.Parse("playlist: X\nupcoming: No Date"));
    }

    [Fact]
    public void The_wire_list_runs_the_corner_then_the_wire_then_we_own_this_city()
    {
        var wire = List("The Wire Release");
        Assert.Equal(72, wire.Entries.Count);
        Assert.All(wire.Entries.Take(6), e => Assert.Equal(("The Corner", 2000), (e.SeriesName!, e.SeriesYear!.Value)));
        Assert.All(wire.Entries.Skip(6).Take(60), e => Assert.Equal("The Wire", e.SeriesName));
        Assert.All(wire.Entries.Skip(66), e => Assert.Equal("We Own This City", e.SeriesName));
    }

    [Fact]
    public void New_franchise_lists_have_the_expected_size()
    {
        Assert.True(List("Alien & Predator Release").Entries.Count >= 24); // 16 films + Alien: Earth
        Assert.Equal(24, List("Alien & Predator Chronological").Entries.Count);
        Assert.True(List("Avatar Release").Entries.Count >= 3);
        Assert.Equal(8, List("Transformers Chronological").Entries.Count);
        Assert.Equal("Transformers One", List("Transformers Chronological").Entries[0].MovieTitle);
        Assert.Equal("The Hunger Games: The Ballad of Songbirds & Snakes", List("Hunger Games Chronological").Entries[0].MovieTitle);
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
        Assert.True(List("Doctor Who Release").Entries.Count >= 303); // auto-updated lists only grow
        Assert.True(List("Doctor Who Release").Entries.Count(e => e.SeriesYear == 2023) >= 21); // 16 episodes + 5 specials
        Assert.True(List("One Chicago Release").Entries.Count >= 810);
        Assert.Equal(4, Count(List("One Chicago Release"), "Law & Order: Special Victims Unit"));
        Assert.Equal(126, List("Battlestar Galactica Release").Entries.Count);
        Assert.Equal(126, List("Breaking Bad Release").Entries.Count);
        Assert.True(List("Middle-earth Release").Entries.Count >= 23);
        Assert.True(List("Middle-earth Chronological").Entries.Count >= 23);
        Assert.True(List("Wizarding World Release").Entries.Count >= 11);
        Assert.True(List("Fast & Furious Chronological").Entries.Count >= 11);
    }

    [Fact]
    public void Release_versions_cover_everything_in_chronological_versions()
    {
        foreach (var franchise in new[] { "Middle-earth", "Wizarding World", "Fast & Furious", "Alien & Predator", "Transformers", "Hunger Games" })
        {
            var release = List(franchise + " Release").Entries.Select(e => e.IsEpisode ? $"{e.SeriesName}|{e.Season}|{e.Episode}" : e.MovieTitle!).Order();
            var chrono = List(franchise + " Chronological").Entries.Select(e => e.IsEpisode ? $"{e.SeriesName}|{e.Season}|{e.Episode}" : e.MovieTitle!).Order();
            // The release list is auto-updated and can run ahead until new items are placed in chronological.
            Assert.Subset(release.ToHashSet(), chrono.ToHashSet());
        }
    }

    [Fact]
    public void Star_wars_release_covers_everything_in_chronological()
    {
        static HashSet<string> Keys(WatchOrderList l) =>
            l.Entries.Select(e => e.IsEpisode ? $"{e.SeriesName}|{e.Season}|{e.Episode}" : $"movie|{e.MovieTitle}|{e.Year}").ToHashSet();

        var release = List("Star Wars Release");
        var chrono = List("Star Wars Chronological");
        Assert.True(release.Entries.Count >= 414);
        Assert.True(chrono.Entries.Count >= 414);
        // Release is auto-updated and may be ahead until new items are placed in chronological.
        Assert.True(Keys(release).IsSupersetOf(Keys(chrono)));
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
        Assert.True(List("DCU Release").Entries.Count >= 35);
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
