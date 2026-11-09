using System.Net;
using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Tests;

// Sample data is made up. Never paste real guide content (titles) into tests.
public sealed class ListDownloaderTests : IDisposable
{
    private const string Url = "https://backup.example/data/";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "watchorder-lists-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static string List(string name, int episodes) =>
        $"playlist: {name}\nupdates: auto\n@ Show A\nS01E01-E{episodes:00}\n";

    private sealed class FakeWeb(Dictionary<string, string> files) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var name = request.RequestUri!.AbsolutePath.Split("/lists/").Last();
            return Task.FromResult(files.TryGetValue(name, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private async Task<ListDownloader.Result> Refresh(Dictionary<string, string> files, string? url = Url)
    {
        using var http = new HttpClient(new FakeWeb(files));
        return await ListDownloader.RefreshAsync(http, url, _dir, CancellationToken.None);
    }

    [Fact]
    public async Task Blank_url_never_contacts_anything()
    {
        var web = new FakeWeb([]);
        using var http = new HttpClient(web);

        await ListDownloader.RefreshAsync(http, "", _dir, CancellationToken.None);

        Assert.Equal(0, web.Requests);
    }

    [Fact]
    public async Task A_new_list_downloads_and_is_used_without_a_restart()
    {
        var result = await Refresh(new() { ["index.json"] = "[\"test-list\"]", ["test-list.txt"] = List("Test List", 10) });

        Assert.Equal(1, result.Updated);
        var loaded = BuiltInLists.Load(_dir).Single(l => l.PlaylistName == "Test List");
        Assert.Equal(10, loaded.Entries.Count);
    }

    [Fact]
    public async Task A_downloaded_update_replaces_the_shipped_list()
    {
        var key = BuiltInLists.Embedded.Keys.First();
        var shipped = BuiltInLists.Embedded[key];
        var longer = string.Join("\n", $"playlist: {shipped.PlaylistName}", "updates: auto", "@ Show A", $"S01E01-E{shipped.Entries.Count + 5:00}") + "\n";

        await Refresh(new() { ["index.json"] = $"[\"{key}\"]", [key + ".txt"] = longer });

        Assert.Equal(shipped.Entries.Count + 5, BuiltInLists.Load(_dir).Single(l => l.PlaylistName == shipped.PlaylistName).Entries.Count);
    }

    [Fact]
    public async Task A_much_shorter_download_is_rejected_and_the_old_copy_kept()
    {
        await Refresh(new() { ["index.json"] = "[\"test-list\"]", ["test-list.txt"] = List("Test List", 50) });

        var result = await Refresh(new() { ["index.json"] = "[\"test-list\"]", ["test-list.txt"] = List("Test List", 5) });

        Assert.Equal(1, result.Rejected);
        Assert.Equal(50, BuiltInLists.Load(_dir).Single(l => l.PlaylistName == "Test List").Entries.Count);
    }

    [Fact]
    public async Task A_malformed_download_is_rejected()
    {
        var result = await Refresh(new() { ["index.json"] = "[\"test-list\"]", ["test-list.txt"] = "this is not a list" });

        Assert.Equal(1, result.Rejected);
        Assert.DoesNotContain(BuiltInLists.Load(_dir), l => l.PlaylistName == "Test List");
    }

    [Fact]
    public async Task Unsafe_file_names_in_the_index_are_ignored()
    {
        var result = await Refresh(new() { ["index.json"] = "[\"../../evil\", \"UPPER\", \"ok-list\"]", ["ok-list.txt"] = List("Ok List", 3) });

        Assert.Equal(1, result.Updated);
        Assert.Single(Directory.GetFiles(_dir, "*.txt"));
    }

    [Fact]
    public async Task Site_down_keeps_everything_and_reports_the_error()
    {
        var result = await Refresh([]);

        Assert.NotNull(result.Error);
        Assert.Equal(BuiltInLists.Embedded.Count, BuiltInLists.Load(_dir).Count);
    }
}
