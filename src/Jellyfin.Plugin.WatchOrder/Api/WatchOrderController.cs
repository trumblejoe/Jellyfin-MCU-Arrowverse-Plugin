using System.Net.Mime;
using Jellyfin.Plugin.WatchOrder.Configuration;
using Jellyfin.Plugin.WatchOrder.Sources;
using Jellyfin.Plugin.WatchOrder.Sync;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchOrder.Api;

/// <summary>Admin-only endpoints used by the plugin's settings page.</summary>
[ApiController]
[Route("WatchOrder")]
[Authorize(Policy = Policies.RequiresElevation)]
public class WatchOrderController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    public WatchOrderController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// What the enabled playlists include that isn't in the library. Worked out on request from
    /// the guide copies saved by the last sync and the current library — no downloads, nothing stored.
    /// </summary>
    [HttpGet("Missing")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<MissingReport> GetMissing()
    {
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();
        if (plugin is null)
        {
            return new MissingReport();
        }

        var store = new GuideSnapshotStore(plugin.DataFolderPath);
        var arrowverse = store.Peek<WatchOrderEntry>(GuideDownloader.ArrowverseName);
        var mcu = store.Peek<McuSource.McuItem>(GuideDownloader.McuName);

        var lists = new List<WatchOrderList>();
        if (config.EnableArrowverse && arrowverse is not null)
        {
            lists.Add(new WatchOrderList(ArrowverseSource.PlaylistName, arrowverse.Items));
        }

        if (mcu is not null)
        {
            McuSource.Index(mcu.Items);
            var today = DateOnly.FromDateTime(DateTime.Now);
            lists.AddRange(SyncPlaylistsTask.EnabledMcuPlaylists(config)
                .Select(p => McuSource.Build(mcu.Items, p.Order, p.Extended, today)));
        }

        var matcher = new LibraryMatcher(_libraryManager, config.SeriesAliases);
        return MissingReport.Build(lists, matcher.Match, matcher.HasSeries, arrowverse?.SavedUtc, mcu?.SavedUtc);
    }
}
