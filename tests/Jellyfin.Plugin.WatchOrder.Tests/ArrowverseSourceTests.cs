using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// Sample data is made up. Never paste real guide content (titles) into tests.
public class ArrowverseSourceTests
{
    private static string Row(int order, string series, string code, string date) =>
        $"""
         <tr class="episode x"> <td>{order}</td> <td>{series}</td> <td>{code}</td> <td>Placeholder</td> <td>{date}</td> <td> <a href=./x>link</a> </td> </tr>
         """;

    private static string Page(params string[] rows) =>
        $"<table id=episode-list><thead><tr><th>#</th></tr></thead><tbody>{string.Join("\n", rows)}</tbody></table>";

    [Fact]
    public void Parses_rows_in_site_order()
    {
        var list = ArrowverseSource.Parse(Page(
            Row(2, "Show B", "S01E01", "October 17, 2012"),
            Row(1, "Show A", "S02E05", "October 10, 2012")));

        Assert.Equal("Arrowverse", list.PlaylistName);
        Assert.Collection(
            list.Entries,
            e => Assert.Equal(("Show A", 2, 5, 2012), (e.SeriesName, e.Season, e.Episode, e.Year)),
            e => Assert.Equal(("Show B", 1, 1, 2012), (e.SeriesName, e.Season, e.Episode, e.Year)));
    }

    [Fact]
    public void Decodes_html_entities_in_show_names()
    {
        var list = ArrowverseSource.Parse(Page(Row(1, "Hero &amp; Friend", "S01E01", "January 01, 2020")));

        Assert.Equal("Hero & Friend", list.Entries[0].SeriesName);
    }

    [Fact]
    public void Never_reads_the_name_column()
    {
        var list = ArrowverseSource.Parse(Page(Row(1, "Show A", "S01E01", "January 01, 2020")));

        Assert.Null(list.Entries[0].MovieTitle);
        Assert.True(list.Entries[0].IsEpisode);
    }

    [Fact]
    public void Skips_rows_with_malformed_episode_codes()
    {
        var list = ArrowverseSource.Parse(Page(
            Row(1, "Show A", "S01E01", "January 01, 2020"),
            Row(2, "Show A", "S05E0?", "January 08, 2020")));

        Assert.Single(list.Entries);
    }

    [Fact]
    public void Throws_when_layout_changes_so_playlist_is_left_alone()
    {
        Assert.Throws<InvalidDataException>(() => ArrowverseSource.Parse("<html><body>redesigned</body></html>"));
    }
}
