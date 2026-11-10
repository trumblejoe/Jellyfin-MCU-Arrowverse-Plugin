using Jellyfin.Plugin.WatchOrder.Sources;
using Jellyfin.Plugin.WatchOrder.Sync;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// A fake library. Sample data is made up apart from show names and years.
public class LibraryMatcherTests
{
    private static Series Show(string name, int? premiered, int? production = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        PremiereDate = premiered is { } y ? new DateTime(y, 1, 14, 0, 0, 0, DateTimeKind.Utc) : null,
        ProductionYear = production ?? premiered
    };

    private static Episode Ep(Series s, int season, int episode) => new()
    {
        Id = Guid.NewGuid(),
        SeriesId = s.Id,
        ParentIndexNumber = season,
        IndexNumber = episode,
        Name = "x"
    };

    private static (LibraryMatcher Matcher, Dictionary<Series, Episode> Episodes) Library(string aliases, params Series[] shows)
    {
        var eps = shows.ToDictionary(s => s, s => Ep(s, 1, 1));
        return (new LibraryMatcher(shows, eps.Values, Array.Empty<Movie>(), aliases), eps);
    }

    // What a built-in list line like "@ Doctor Who (2023)" + "S01E01" produces.
    private static WatchOrderEntry ListEntry(string show, int year) => new(show, 1, 1, null, year, SeriesYear: year);

    [Fact]
    public void Battlestar_matches_by_the_year_in_its_name_despite_a_2005_premiere_date()
    {
        // Exactly how the real library has it: named "(2003)", premiere date = US premiere.
        var bsg = Show("Battlestar Galactica (2003)", 2005);
        var (matcher, eps) = Library("", bsg);

        Assert.Equal(eps[bsg].Id, matcher.Match(ListEntry("Battlestar Galactica", 2003)));
    }

    [Fact]
    public void A_premiere_two_years_off_still_matches()
    {
        var bsg = Show("Battlestar Galactica", 2005);
        var (matcher, eps) = Library("", bsg);

        Assert.Equal(eps[bsg].Id, matcher.Match(ListEntry("Battlestar Galactica", 2003)));
    }

    [Fact]
    public void A_same_named_show_from_another_era_never_matches()
    {
        var (matcher, _) = Library("", Show("Doctor Who", 2005));

        Assert.Null(matcher.Match(ListEntry("Doctor Who", 2023)));
    }

    [Fact]
    public void Same_named_shows_are_told_apart_by_year()
    {
        var dw2005 = Show("Doctor Who", 2005);
        var dw2023 = Show("Doctor Who (2023)", 2023);
        var (matcher, eps) = Library("", dw2005, dw2023);

        Assert.Equal(eps[dw2023].Id, matcher.Match(ListEntry("Doctor Who", 2023)));
        Assert.Equal(eps[dw2005].Id, matcher.Match(ListEntry("Doctor Who", 2005)));
    }

    [Fact]
    public void An_alias_with_a_year_picks_that_series()
    {
        var dw2005 = Show("Doctor Who", 2005);
        var (matcher, eps) = Library("Doctor Who (2023) = Doctor Who (2005)", dw2005, Show("Doctor Who (2023)", 2023));

        Assert.Equal(eps[dw2005].Id, matcher.Match(ListEntry("Doctor Who", 2023)));
    }

    [Fact]
    public void An_alias_without_a_year_overrides_the_list_year()
    {
        var bsg = Show("BSG Reimagined", 2009);
        var (matcher, eps) = Library("Battlestar Galactica (2003) = BSG Reimagined", bsg);

        Assert.Equal(eps[bsg].Id, matcher.Match(ListEntry("Battlestar Galactica", 2003)));
        Assert.True(matcher.HasSeries(ListEntry("Battlestar Galactica", 2003)));
    }

    [Fact]
    public void An_alias_maps_a_guide_name_to_a_differently_named_library_show()
    {
        var show = Show("Falcon & Winter Soldier", 2021);
        var (matcher, eps) = Library("The Falcon And The Winter Soldier = Falcon & Winter Soldier\r\n", show);

        // A guide entry (MCU/Arrowverse style: air year, not a strict list year).
        Assert.Equal(eps[show].Id, matcher.Match(new WatchOrderEntry("The Falcon And The Winter Soldier", 1, 1, null, 2021)));
    }

    [Fact]
    public void A_show_with_no_year_at_all_is_not_matched_to_a_strict_list_year()
    {
        var (matcher, _) = Library("", Show("Doctor Who", null));

        Assert.Null(matcher.Match(ListEntry("Doctor Who", 2023)));
    }
}
