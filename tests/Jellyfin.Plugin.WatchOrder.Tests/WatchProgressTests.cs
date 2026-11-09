using Jellyfin.Plugin.WatchOrder.Sync;

namespace Jellyfin.Plugin.WatchOrder.Tests;

public class WatchProgressTests
{
    private static readonly Guid[] Items = Enumerable.Range(1, 6).Select(_ => Guid.NewGuid()).ToArray();

    private static Func<Guid, bool> Watched(params int[] positions) =>
        id => positions.Contains(Array.IndexOf(Items, id) + 1);

    [Fact]
    public void Starts_at_the_first_unwatched_item()
    {
        Assert.Equal(Items[2..], WatchProgress.FromNextUnwatched(Items, Watched(1, 2)));
    }

    [Fact]
    public void Something_watched_out_of_order_stays_in_its_place()
    {
        // Watched 1-2 in order, and 5 on its own: the playlist starts at 3 and still has 5.
        var result = WatchProgress.FromNextUnwatched(Items, Watched(1, 2, 5));

        Assert.Equal(Items[2..], result);
        Assert.Contains(Items[4], result);
    }

    [Fact]
    public void Finishing_the_next_item_also_drops_already_watched_items_right_after_it()
    {
        // 2 was watched out of order earlier; finishing 1 removes both from the front.
        Assert.Equal([Items[0], Items[1]], WatchProgress.WatchedAtFront(Items, Watched(1, 2, 4)));
    }

    [Fact]
    public void Watching_something_further_down_removes_nothing()
    {
        Assert.Empty(WatchProgress.WatchedAtFront(Items, Watched(4)));
    }

    [Fact]
    public void Nothing_watched_keeps_everything_and_all_watched_empties_the_list()
    {
        Assert.Equal(Items, WatchProgress.FromNextUnwatched(Items, Watched()));
        Assert.Empty(WatchProgress.FromNextUnwatched(Items, Watched(1, 2, 3, 4, 5, 6)));
    }
}
