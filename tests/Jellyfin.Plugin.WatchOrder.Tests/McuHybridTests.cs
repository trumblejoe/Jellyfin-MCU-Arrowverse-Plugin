using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// Sample data is made up. Never paste real guide content (titles) into tests.
public class McuHybridTests
{
    private static readonly DateOnly Today = new(2026, 6, 1);

    private static McuSource.McuItem Film(string title, string date, int universe = 0, string type = "Marvel Studios") =>
        new() { Title = title, ReleaseDateRaw = date, Canon = true, Multiverse = universe, Type = type };

    private static McuSource.McuItem Ep(string show, int e, string date, string type) =>
        new() { Show = show, Season = 1, Episode = e, ReleaseDateRaw = date, Canon = true, Type = type };

    private static List<string> Order(params McuSource.McuItem[] items) =>
        McuSource.Build(McuSource.Index(items.ToList()), McuOrder.Hybrid, extended: false, Today).Entries
            .Select(e => e.MovieTitle ?? $"{e.SeriesName} E{e.Episode}").ToList();

    [Fact]
    public void Older_other_universe_films_move_from_the_start_to_just_before_the_multiverse_saga()
    {
        var order = Order(
            Film("Old Other Film", "2000-07-14", universe: 11, type: "Fox"),
            Film("First Main Film", "2008-05-02"),
            Film("Mid Other Film", "2011-06-03", universe: 11, type: "Fox"),
            Film("Saga End Film", "2019-07-02"),
            Film("Saga Start Film", "2021-03-05"),
            Film("Later Other Film", "2022-04-01", universe: 13, type: "Sony"));

        Assert.Equal(
            ["First Main Film", "Saga End Film", "Old Other Film", "Mid Other Film", "Saga Start Film", "Later Other Film"],
            order);
    }

    [Fact]
    public void A_streaming_season_plays_straight_through()
    {
        var order = Order(
            Ep("Stream Show", 1, "2021-01-15", "Disney+"),
            Film("Film Released Mid Season", "2021-01-22"),
            Ep("Stream Show", 2, "2021-01-29", "Disney+"),
            Ep("Stream Show", 3, "2021-02-05", "Disney+"));

        Assert.Equal(["Stream Show E1", "Stream Show E2", "Stream Show E3", "Film Released Mid Season"], order);
    }

    [Fact]
    public void A_broadcast_show_keeps_air_date_order_around_films()
    {
        var order = Order(
            Ep("Network Show", 1, "2013-11-12", "ABC"),
            Film("Tie In Film", "2013-11-08"),
            Ep("Network Show", 2, "2013-11-19", "ABC"));

        Assert.Equal(["Tie In Film", "Network Show E1", "Network Show E2"], order);
    }

    [Fact]
    public void Hybrid_keeps_the_same_items_as_release_order()
    {
        var items = McuSource.Index(new List<McuSource.McuItem>
        {
            Film("A", "2000-01-01", universe: 11, type: "Fox"),
            Film("B", "2008-01-01"),
            Ep("S", 1, "2021-01-15", "Disney+"),
            Film("C", "2021-01-20")
        });

        var hybrid = McuSource.Build(items, McuOrder.Hybrid, false, Today).Entries.Select(e => e.MovieTitle ?? e.SeriesName).Order();
        var release = McuSource.Build(items, McuOrder.Release, false, Today).Entries.Select(e => e.MovieTitle ?? e.SeriesName).Order();
        Assert.Equal(release, hybrid);
        Assert.Equal("MCU Hybrid", McuSource.Build(items, McuOrder.Hybrid, false, Today).PlaylistName);
    }
}
