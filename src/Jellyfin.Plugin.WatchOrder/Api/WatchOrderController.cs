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
    private readonly MediaBrowser.Model.Tasks.ITaskManager _taskManager;

    public WatchOrderController(ILibraryManager libraryManager, MediaBrowser.Model.Tasks.ITaskManager taskManager)
    {
        _libraryManager = libraryManager;
        _taskManager = taskManager;
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

        var builtIn = SyncPlaylistsTask.EnabledBuiltInLists(config).ToList();
        lists.AddRange(builtIn);

        var matcher = new LibraryMatcher(_libraryManager, config.SeriesAliases);
        var report = MissingReport.Build(lists, matcher.Match, matcher.HasSeries, arrowverse?.SavedUtc, mcu?.SavedUtc);

        // Built-in lists need no sync to be checked, so they count as data too.
        return builtIn.Count > 0 && !report.HasData
            ? new MissingReport { HasData = true, Playlists = report.Playlists, Shows = report.Shows, MissingMovies = report.MissingMovies }
            : report;
    }

    /// <summary>When things last ran and changed, for the settings page's status line.</summary>
    [HttpGet("Status")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<StatusInfo> GetStatus()
    {
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();
        var sync = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask is SyncPlaylistsTask)?.LastExecutionResult;

        DateTime? listsUpdated = null;
        var listsDownloaded = 0;
        if (BuiltInLists.DownloadFolder is { } folder && Directory.Exists(folder))
        {
            var files = Directory.GetFiles(folder, "*.txt");
            listsDownloaded = files.Length;
            listsUpdated = files.Length > 0 ? files.Max(System.IO.File.GetLastWriteTimeUtc) : null;
        }

        var store = plugin is null ? null : new GuideSnapshotStore(plugin.DataFolderPath);
        return new StatusInfo
        {
            LastSyncUtc = sync?.EndTimeUtc,
            LastSyncStatus = sync?.Status.ToString(),
            McuUpdatedUtc = store?.Peek<McuSource.McuItem>(GuideDownloader.McuName)?.SavedUtc,
            ArrowverseUpdatedUtc = store?.Peek<WatchOrderEntry>(GuideDownloader.ArrowverseName)?.SavedUtc,
            ListsUpdatedUtc = listsUpdated,
            ListsDownloaded = listsDownloaded,
            ListUpdatesOn = !string.IsNullOrWhiteSpace(config.BackupDataUrl)
        };
    }

    /// <summary>Your library's show names, as suggestions for the settings page's alias boxes.</summary>
    [HttpGet("LibraryShows")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<IEnumerable<string>> GetLibraryShows() =>
        _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = [Jellyfin.Data.Enums.BaseItemKind.Series],
                Recursive = true
            })
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The built-in lists shipped with the plugin, for the settings page's checkboxes.</summary>
    [HttpGet("Lists")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<IEnumerable<BuiltInListInfo>> GetLists() =>
        BuiltInLists.All.Select(l => new BuiltInListInfo(l.PlaylistName, l.Entries.Count)).ToList();

    public sealed record BuiltInListInfo(string Name, int Items);
}
