using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// Sample data is made up. Never paste real guide content (titles) into tests.
public class McuSourceTests
{
    private static readonly DateOnly Today = new(2026, 6, 1);

    private const string Json = """
        [
          {"title":"Movie One","release_date":"2010-05-01","canon":true,"timeline_order":3,"sacred_timeline":2,"type":"x","id":"m1","runtime":1,"multiverse":0},
          {"title":"Ep","show":"Show A","season":1,"episode":1,"release_date":"2012-01-01","canon":true,"timeline_order":1,"sacred_timeline":3,"type":"x","id":"a1","runtime":1,"multiverse":0},
          {"title":"Ep","show":"Show B","season":1,"episode":1,"release_date":"2008-01-01","canon":false,"timeline_order":2,"sacred_timeline":1,"type":"x","id":"b1","runtime":1,"multiverse":0},
          {"title":"Future Movie","release_date":"2099-01-01","canon":true,"timeline_order":0,"sacred_timeline":0,"type":"x","id":"f1","runtime":1,"multiverse":0}
        ]
        """;

    private static IReadOnlyList<string> Ids(WatchOrderList list) =>
        list.Entries.Select(e => e.MovieTitle ?? e.SeriesName!).ToList();

    [Fact]
    public void Release_order_sorts_by_release_date()
    {
        var list = McuSource.Build(McuSource.ParseItems(Json), McuOrder.Release, canonOnly: false, Today);

        Assert.Equal("MCU - Release Order", list.PlaylistName);
        Assert.Equal(["Show B", "Movie One", "Show A"], Ids(list));
    }

    [Fact]
    public void Chronological_order_uses_timeline_order()
    {
        var list = McuSource.Build(McuSource.ParseItems(Json), McuOrder.Chronological, canonOnly: false, Today);

        Assert.Equal(["Show A", "Show B", "Movie One"], Ids(list));
    }

    [Fact]
    public void Sacred_timeline_order_uses_sacred_timeline()
    {
        var list = McuSource.Build(McuSource.ParseItems(Json), McuOrder.SacredTimeline, canonOnly: false, Today);

        Assert.Equal(["Show B", "Movie One", "Show A"], Ids(list));
    }

    [Fact]
    public void Canon_only_drops_non_canon_entries()
    {
        var list = McuSource.Build(McuSource.ParseItems(Json), McuOrder.Release, canonOnly: true, Today);

        Assert.DoesNotContain("Show B", Ids(list));
    }

    [Fact]
    public void Unreleased_items_are_never_included()
    {
        foreach (var order in Enum.GetValues<McuOrder>())
        {
            var list = McuSource.Build(McuSource.ParseItems(Json), order, canonOnly: false, Today);
            Assert.DoesNotContain("Future Movie", Ids(list));
        }
    }

    [Fact]
    public void Episodes_carry_no_title_only_show_season_episode()
    {
        var list = McuSource.Build(McuSource.ParseItems(Json), McuOrder.Release, canonOnly: false, Today);
        var episode = list.Entries.Single(e => e.SeriesName == "Show A");

        Assert.Null(episode.MovieTitle);
        Assert.Equal((1, 1), (episode.Season, episode.Episode));
    }

    [Fact]
    public void Episode_titles_are_dropped_at_parse_time()
    {
        var items = McuSource.ParseItems(Json);

        Assert.All(items.Where(i => i.Show is not null), i => Assert.Null(i.Title));
        Assert.Equal("Movie One", items.Single(i => i.Show is null && i.Canon && i.ReleaseDate!.Value.Year == 2010).Title);
    }

    [Fact]
    public void Empty_feed_throws_so_playlists_are_left_alone()
    {
        Assert.Throws<InvalidDataException>(() => McuSource.ParseItems("[]"));
    }
}
