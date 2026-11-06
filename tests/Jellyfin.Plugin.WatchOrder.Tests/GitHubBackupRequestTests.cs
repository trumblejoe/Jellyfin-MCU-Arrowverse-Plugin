using System.Net;
using Jellyfin.Plugin.WatchOrder.Sources;

namespace Jellyfin.Plugin.WatchOrder.Tests;

/// <summary>
/// The plugin must only contact the GitHub backup when a server admin opted in AND a backup is
/// actually needed. Sample data is made up.
/// </summary>
public sealed class GitHubBackupRequestTests : IDisposable
{
    private const string BackupUrl = "https://backup.example/data/";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "watchorder-gh-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static string Page(int rows) =>
        "<table><tbody>" + string.Concat(Enumerable.Range(1, rows).Select(n =>
            $"<tr class=\"episode x\"> <td>{n}</td> <td>Show A</td> <td>S01E{n:00}</td> <td>x</td> <td>January 01, 2020</td> <td></td> </tr>")) + "</tbody></table>";

    private sealed class FakeWeb(bool siteUp, int rows = 10) : HttpMessageHandler
    {
        public int BackupRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "backup.example")
            {
                BackupRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(siteUp
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page(rows)) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private async Task<int> RunAsync(FakeWeb web, string? backupUrl)
    {
        using var http = new HttpClient(web);
        await GuideDownloader.ArrowverseAsync(http, new GuideSnapshotStore(_dir), backupUrl, CancellationToken.None);
        return web.BackupRequests;
    }

    [Fact]
    public async Task Blank_url_never_contacts_github_even_when_site_is_down()
    {
        Assert.Equal(0, await RunAsync(new FakeWeb(siteUp: false), backupUrl: ""));
        Assert.Equal(0, await RunAsync(new FakeWeb(siteUp: false), backupUrl: null));
    }

    [Fact]
    public async Task Healthy_site_with_local_copy_does_not_contact_github()
    {
        await RunAsync(new FakeWeb(siteUp: true), BackupUrl); // first run creates the local copy

        Assert.Equal(0, await RunAsync(new FakeWeb(siteUp: true), BackupUrl));
    }

    [Fact]
    public async Task Github_is_contacted_only_when_a_backup_is_needed()
    {
        Assert.Equal(1, await RunAsync(new FakeWeb(siteUp: true), BackupUrl));          // no local copy yet
        Assert.Equal(1, await RunAsync(new FakeWeb(siteUp: false), BackupUrl));         // site down
        Assert.Equal(1, await RunAsync(new FakeWeb(siteUp: true, rows: 2), BackupUrl)); // site looks broken
    }
}
