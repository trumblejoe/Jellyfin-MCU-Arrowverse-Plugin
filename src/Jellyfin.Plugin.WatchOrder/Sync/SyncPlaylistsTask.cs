using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchOrder.Configuration;
using Jellyfin.Plugin.WatchOrder.Sources;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// Scheduled task (Dashboard → Scheduled Tasks → Watch Order Playlists) that downloads the
/// guides, matches them to the library, and creates/reorders each selected user's private playlists.
///
/// If a guide can't be downloaded (or looks broken), the newest backup is used instead: the
/// copy saved on this server, or the copy the GitHub workflow keeps on the repo's data branch.
///
/// Spoiler policy: logs only counts, show names, and season/episode codes — never
/// episode titles, movie titles, or synopses.
/// </summary>
public class SyncPlaylistsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncPlaylistsTask> _logger;

    public SyncPlaylistsTask(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IHttpClientFactory httpClientFactory,
        ILogger<SyncPlaylistsTask> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "Sync watch-order playlists";

    public string Key => "WatchOrderPlaylistsSync";

    public string Description => "Updates the Arrowverse and MCU playlists from arrowverse.info and aaronperris.com/mcu.";

    public string Category => "Watch Order Playlists";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks },
        new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger }
    ];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();
        var users = ManagedPlaylists.ResolveUsers(_userManager, config);
        if (users.Count == 0)
        {
            _logger.LogError("Watch Order: no users selected. Tick at least one user on the plugin settings page.");
            return;
        }

        var store = new GuideSnapshotStore(plugin?.DataFolderPath ?? Path.GetTempPath());
        var lists = await GetListsAsync(config, store, cancellationToken).ConfigureAwait(false);
        progress.Report(30);

        var matcher = new LibraryMatcher(_libraryManager, config.SeriesAliases);
        var matched = lists.Select(l => (l.PlaylistName, Ids: MatchAndLog(l, matcher))).ToList();
        progress.Report(50);

        var steps = users.Count * matched.Count;
        var done = 0;
        foreach (var userId in users)
        {
            await RenameLegacyPlaylistsAsync(config, userId).ConfigureAwait(false);
            foreach (var (name, ids) in matched)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var desired = config.RemoveWatched ? FromNextUnwatched(ids, userId) : ids;
                await SyncOneAsync(name, desired, userId).ConfigureAwait(false);
                progress.Report(50 + (50.0 * ++done / steps));
            }
        }

        if (matcher.MissingSeries.Count > 0)
        {
            _logger.LogInformation(
                "Watch Order: these shows from the guides aren't in your library (add a Series Alias if one is there under a different name): {Shows}",
                string.Join(", ", matcher.MissingSeries.Order()));
        }
    }

    private async Task<List<WatchOrderList>> GetListsAsync(PluginConfiguration config, GuideSnapshotStore store, CancellationToken ct)
    {
        var lists = new List<WatchOrderList>();
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-WatchOrderPlaylists/1.1");

        if (config.EnableArrowverse)
        {
            var result = await GuideDownloader.ArrowverseAsync(http, store, config.BackupDataUrl, ct).ConfigureAwait(false);
            LogOutcome("arrowverse.info", result.Outcome, result.SavedUtc, result.FreshCount, result.Error);
            if (result.Items is not null)
            {
                lists.Add(new WatchOrderList(ArrowverseSource.PlaylistName, result.Items));
            }
        }

        lists.AddRange(EnabledBuiltInLists(config));

        var mcuPlaylists = EnabledMcuPlaylists(config);
        if (mcuPlaylists.Count > 0)
        {
            var result = await GuideDownloader.McuAsync(http, store, config.BackupDataUrl, ct).ConfigureAwait(false);
            LogOutcome("the MCU guide", result.Outcome, result.SavedUtc, result.FreshCount, result.Error);
            if (result.Items is not null)
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                lists.AddRange(mcuPlaylists.Select(p => McuSource.Build(result.Items, p.Order, p.Extended, today)));
            }
        }

        return lists;
    }

    /// <summary>The built-in lists ticked in settings.</summary>
    internal static IEnumerable<WatchOrderList> EnabledBuiltInLists(PluginConfiguration config) =>
        BuiltInLists.All.Where(l => (config.EnabledLists ?? []).Contains(l.PlaylistName, StringComparer.OrdinalIgnoreCase));

    /// <summary>The MCU playlists ticked in settings, as (order, extended) pairs.</summary>
    internal static List<(McuOrder Order, bool Extended)> EnabledMcuPlaylists(PluginConfiguration config)
    {
        var all = new (McuOrder Order, bool Extended, bool Enabled)[]
        {
            (McuOrder.Release, false, config.EnableMcuRelease),
            (McuOrder.Release, true, config.EnableMcuReleaseExtended),
            (McuOrder.Chronological, false, config.EnableMcuChronological),
            (McuOrder.Chronological, true, config.EnableMcuChronologicalExtended),
            (McuOrder.SacredTimeline, false, config.EnableMcuSacredTimeline),
            (McuOrder.SacredTimeline, true, config.EnableMcuSacredTimelineExtended),
        };
        return all.Where(p => p.Enabled).Select(p => (p.Order, p.Extended)).ToList();
    }

    /// <summary>
    /// Pre-1.2 playlists ("MCU - Release Order", ...) are renamed to their new name, so the
    /// user keeps the same playlist instead of getting a duplicate. If only the Extended
    /// playlist is ticked for that order, it becomes the Extended one; otherwise the canon one.
    /// </summary>
    private async Task RenameLegacyPlaylistsAsync(PluginConfiguration config, Guid userId)
    {
        var enabled = EnabledMcuPlaylists(config);
        foreach (var order in Enum.GetValues<McuOrder>())
        {
            var legacy = ManagedPlaylists.Find(_playlistManager, userId, McuSource.LegacyPlaylistName(order));
            if (legacy is null)
            {
                continue;
            }

            var onlyExtended = enabled.Contains((order, true)) && !enabled.Contains((order, false));
            var newName = McuSource.PlaylistName(order, extended: onlyExtended);
            if (ManagedPlaylists.Find(_playlistManager, userId, newName) is not null)
            {
                _logger.LogWarning("Watch Order: left old playlist {Old} alone because {New} already exists; delete it if you don't need it.", legacy.Name, newName);
                continue;
            }

            await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
            {
                Id = legacy.Id,
                UserId = userId,
                Name = newName
            }).ConfigureAwait(false);
            _logger.LogInformation("Watch Order: renamed playlist {Old} to {New} for user {UserId}.", legacy.Name, newName, userId);
        }
    }

    private void LogOutcome(string source, SnapshotOutcome outcome, DateTime? savedUtc, int? freshCount, string? error)
    {
        switch (outcome)
        {
            case SnapshotOutcome.UsedSavedCopy:
                _logger.LogWarning("Watch Order: couldn't read {Source} ({Error}); using the saved copy from {Saved:u}.", source, error, savedUtc);
                break;
            case SnapshotOutcome.UsedGitHubCopy:
                _logger.LogWarning("Watch Order: couldn't read {Source} ({Error}); using the GitHub backup copy from {Saved:u}.", source, error, savedUtc);
                break;
            case SnapshotOutcome.RejectedShrunkDownload:
                _logger.LogWarning(
                    "Watch Order: {Source} returned only {Count} entries, far fewer than the backup; it may be broken. Using the backup from {Saved:u}.",
                    source,
                    freshCount,
                    savedUtc);
                break;
            case SnapshotOutcome.Unavailable:
                _logger.LogWarning("Watch Order: couldn't read {Source} ({Error}) and there's no backup copy; its playlists were left as they are.", source, error);
                break;
        }
    }

    private List<Guid> MatchAndLog(WatchOrderList list, LibraryMatcher matcher)
    {
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        var missingEpisodes = 0;
        var missingMovies = 0;
        foreach (var entry in list.Entries)
        {
            if (matcher.Match(entry) is { } id)
            {
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }
            else if (entry.IsEpisode)
            {
                missingEpisodes++;
            }
            else
            {
                missingMovies++;
            }
        }

        _logger.LogInformation(
            "Watch Order: {Playlist}: {Found} of {Total} in library ({MissingEp} episodes, {MissingMovies} movies/specials not found).",
            list.PlaylistName,
            ids.Count,
            list.Entries.Count,
            missingEpisodes,
            missingMovies);
        return ids;
    }

    private async Task SyncOneAsync(string playlistName, List<Guid> desired, Guid userId)
    {
        var existing = ManagedPlaylists.Find(_playlistManager, userId, playlistName);
        if (existing is null)
        {
            if (desired.Count == 0)
            {
                return;
            }

            await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = playlistName,
                ItemIdList = desired,
                MediaType = MediaType.Video,
                UserId = userId,
                Public = false
            }).ConfigureAwait(false);
            _logger.LogInformation("Watch Order: created {Playlist} for user {UserId}.", playlistName, userId);
            return;
        }

        var current = existing.LinkedChildren.Select(c => c.ItemId ?? Guid.Empty).ToList();
        var orderChanged = !current.SequenceEqual(desired);

        // Each user has their own copy now, so a shared (pre-1.1) playlist is made private.
        var wasShared = existing.OpenAccess;
        if (!orderChanged && !wasShared)
        {
            return;
        }

        await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
        {
            Id = existing.Id,
            UserId = userId,
            Ids = orderChanged ? desired : null,
            Public = false
        }).ConfigureAwait(false);
        _logger.LogInformation("Watch Order: updated {Playlist} for user {UserId} ({Count} items).", playlistName, userId, desired.Count);
    }

    /// <summary>The playlist from the user's next unwatched item onward (see <see cref="WatchProgress"/>).</summary>
    private List<Guid> FromNextUnwatched(List<Guid> ids, Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null || ids.Count == 0)
        {
            return ids;
        }

        var items = ids.Select(_libraryManager.GetItemById).OfType<BaseItem>().ToList();
        var userData = _userDataManager.GetUserDataBatch(items, user);
        var watched = items
            .Where(i => userData.TryGetValue(i.Id, out var d) && d.Played)
            .Select(i => i.Id)
            .ToHashSet();
        return WatchProgress.FromNextUnwatched(ids, watched.Contains);
    }
}
