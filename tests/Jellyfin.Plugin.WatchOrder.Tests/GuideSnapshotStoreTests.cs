using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// Sample data is made up. Never paste real guide content (titles) into tests.
public sealed class GuideSnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "watchorder-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static List<WatchOrderEntry> Episodes(int count) =>
        Enumerable.Range(1, count).Select(n => new WatchOrderEntry("Show A", 1, n, null, 2020)).ToList();

    [Fact]
    public void Fresh_download_is_used_and_saved()
    {
        var store = new GuideSnapshotStore(_dir);

        var (items, outcome, _) = store.Resolve("x", Episodes(10));

        Assert.Equal(SnapshotOutcome.Fresh, outcome);
        Assert.Equal(10, items!.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "x.snapshot.json")));
    }

    [Fact]
    public void Failed_download_falls_back_to_saved_copy_in_same_order()
    {
        new GuideSnapshotStore(_dir).Resolve("x", Episodes(10));

        var (items, outcome, saved) = new GuideSnapshotStore(_dir).Resolve<WatchOrderEntry>("x", null);

        Assert.Equal(SnapshotOutcome.UsedSavedCopy, outcome);
        Assert.NotNull(saved);
        Assert.Equal(Enumerable.Range(1, 10), items!.Select(e => e.Episode!.Value));
        Assert.True(items![0].IsEpisode);
    }

    [Fact]
    public void Much_smaller_download_is_rejected_and_saved_copy_kept()
    {
        var store = new GuideSnapshotStore(_dir);
        store.Resolve("x", Episodes(100));

        var (items, outcome, _) = store.Resolve("x", Episodes(10));

        Assert.Equal(SnapshotOutcome.RejectedShrunkDownload, outcome);
        Assert.Equal(100, items!.Count);

        // And the saved copy wasn't overwritten by the bad download.
        Assert.Equal(100, store.Resolve<WatchOrderEntry>("x", null).Items!.Count);
    }

    [Fact]
    public void Slightly_smaller_download_is_accepted()
    {
        var store = new GuideSnapshotStore(_dir);
        store.Resolve("x", Episodes(100));

        var (items, outcome, _) = store.Resolve("x", Episodes(90));

        Assert.Equal(SnapshotOutcome.Fresh, outcome);
        Assert.Equal(90, items!.Count);
    }

    [Fact]
    public void No_download_and_no_saved_copy_is_unavailable()
    {
        var (items, outcome, _) = new GuideSnapshotStore(_dir).Resolve<WatchOrderEntry>("x", null);

        Assert.Equal(SnapshotOutcome.Unavailable, outcome);
        Assert.Null(items);
    }

    [Fact]
    public void Corrupt_saved_copy_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "x.snapshot.json"), "{ not json");

        var (_, outcome, _) = new GuideSnapshotStore(_dir).Resolve<WatchOrderEntry>("x", null);

        Assert.Equal(SnapshotOutcome.Unavailable, outcome);
    }

    private static GuideSnapshot<WatchOrderEntry> GitHubCopy(int count, DateTime savedUtc) =>
        new() { SavedUtc = savedUtc, Items = Episodes(count) };

    [Fact]
    public void Failed_download_with_no_local_copy_uses_github_copy_and_saves_it_locally()
    {
        var store = new GuideSnapshotStore(_dir);

        var (items, outcome, _) = store.Resolve("x", null, GitHubCopy(50, DateTime.UtcNow));

        Assert.Equal(SnapshotOutcome.UsedGitHubCopy, outcome);
        Assert.Equal(50, items!.Count);
        Assert.Equal(SnapshotOutcome.UsedSavedCopy, store.Resolve<WatchOrderEntry>("x", null).Outcome);
    }

    [Fact]
    public void Failed_download_prefers_whichever_backup_is_newer()
    {
        var store = new GuideSnapshotStore(_dir);
        store.Resolve("x", Episodes(10));

        Assert.Equal(SnapshotOutcome.UsedSavedCopy, store.Resolve("x", null, GitHubCopy(20, DateTime.UtcNow.AddDays(-30))).Outcome);
        Assert.Equal(SnapshotOutcome.UsedGitHubCopy, store.Resolve("x", null, GitHubCopy(20, DateTime.UtcNow.AddDays(1))).Outcome);
    }

    [Fact]
    public void Github_copy_protects_a_new_install_from_a_broken_site()
    {
        var (items, outcome, _) = new GuideSnapshotStore(_dir).Resolve("x", Episodes(5), GitHubCopy(100, DateTime.UtcNow));

        Assert.Equal(SnapshotOutcome.RejectedShrunkDownload, outcome);
        Assert.Equal(100, items!.Count);
    }

    [Fact]
    public void Unchanged_data_is_not_rewritten()
    {
        var store = new GuideSnapshotStore(_dir);
        var first = store.Resolve("x", Episodes(10)).SavedUtc;
        var path = Path.Combine(_dir, "x.snapshot.json");
        var written = File.GetLastWriteTimeUtc(path);

        var second = store.Resolve("x", Episodes(10)).SavedUtc;

        Assert.Equal(first, second);
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Mcu_saved_copy_rebuilds_all_orders_without_episode_titles()
    {
        const string json = """
            [
              {"title":"Movie One","release_date":"2010-05-01","canon":true,"timeline_order":2,"sacred_timeline":2},
              {"title":"Secret Ep Name","show":"Show A","season":1,"episode":1,"release_date":"2012-01-01","canon":true,"timeline_order":1,"sacred_timeline":1}
            ]
            """;
        new GuideSnapshotStore(_dir).Resolve("mcu", McuSource.ParseItems(json));

        var saved = File.ReadAllText(Path.Combine(_dir, "mcu.snapshot.json"));
        Assert.DoesNotContain("Secret Ep Name", saved);

        var (items, _, _) = new GuideSnapshotStore(_dir).Resolve<McuSource.McuItem>("mcu", null);
        McuSource.Index(items!);
        var today = new DateOnly(2026, 1, 1);
        Assert.Equal("Movie One", McuSource.Build(items!, McuOrder.Release, extended: false, today).Entries[0].MovieTitle);
        Assert.Equal("Show A", McuSource.Build(items!, McuOrder.Chronological, extended: false, today).Entries[0].SeriesName);
    }
}
