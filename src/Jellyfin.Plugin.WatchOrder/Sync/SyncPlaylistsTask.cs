using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.WatchOrder.Configuration;
using Jellyfin.Plugin.WatchOrder.Sources;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// Scheduled task (Dashboard → Scheduled Tasks → Watch Order Playlists) that downloads the
/// guides, matches them to the library, and creates/reorders the playlists.
///
/// Spoiler policy: logs only counts, show names, and season/episode codes — never
/// episode titles, movie titles, or synopses.
/// </summary>
public class SyncPlaylistsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncPlaylistsTask> _logger;

    public SyncPlaylistsTask(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        IHttpClientFactory httpClientFactory,
        ILogger<SyncPlaylistsTask> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
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
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var ownerId = ResolveOwner(config);
        if (ownerId is null)
        {
            _logger.LogError("Watch Order: no playlist owner found. Pick a user on the plugin settings page.");
            return;
        }

        var lists = await DownloadListsAsync(config, cancellationToken).ConfigureAwait(false);
        progress.Report(30);

        var matcher = new LibraryMatcher(_libraryManager, config.SeriesAliases);
        progress.Report(50);

        for (var i = 0; i < lists.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SyncOneAsync(lists[i], matcher, ownerId.Value, config.MakePublic).ConfigureAwait(false);
            progress.Report(50 + (50.0 * (i + 1) / lists.Count));
        }

        if (matcher.MissingSeries.Count > 0)
        {
            _logger.LogInformation(
                "Watch Order: these shows from the guides aren't in your library (add a Series Alias if one is there under a different name): {Shows}",
                string.Join(", ", matcher.MissingSeries.Order()));
        }
    }

    private async Task<List<WatchOrderList>> DownloadListsAsync(PluginConfiguration config, CancellationToken ct)
    {
        var lists = new List<WatchOrderList>();
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-WatchOrderPlaylists/1.0");

        if (config.EnableArrowverse)
        {
            try
            {
                var html = await http.GetStringAsync(ArrowverseSource.Url, ct).ConfigureAwait(false);
                lists.Add(ArrowverseSource.Parse(html));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                // Leave the existing playlist untouched rather than emptying it.
                _logger.LogWarning(ex, "Watch Order: couldn't read arrowverse.info; Arrowverse playlist left as-is.");
            }
        }

        // Each MCU order has its own on/off switch and its own canon-only switch.
        var mcuOrders = new List<(McuOrder Order, bool CanonOnly)>();
        if (config.EnableMcuRelease)
        {
            mcuOrders.Add((McuOrder.Release, config.McuReleaseCanonOnly));
        }

        if (config.EnableMcuChronological)
        {
            mcuOrders.Add((McuOrder.Chronological, config.McuChronologicalCanonOnly));
        }

        if (config.EnableMcuSacredTimeline)
        {
            mcuOrders.Add((McuOrder.SacredTimeline, config.McuSacredTimelineCanonOnly));
        }

        if (mcuOrders.Count > 0)
        {
            try
            {
                var json = await http.GetStringAsync(McuSource.Url, ct).ConfigureAwait(false);
                var items = McuSource.ParseItems(json);
                var today = DateOnly.FromDateTime(DateTime.Now);
                lists.AddRange(mcuOrders.Select(o => McuSource.Build(items, o.Order, o.CanonOnly, today)));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(ex, "Watch Order: couldn't read the MCU guide; MCU playlists left as-is.");
            }
        }

        return lists;
    }

    private async Task SyncOneAsync(WatchOrderList list, LibraryMatcher matcher, Guid ownerId, bool makePublic)
    {
        var desired = new List<Guid>();
        var seen = new HashSet<Guid>();
        var missingEpisodes = 0;
        var missingMovies = 0;
        foreach (var entry in list.Entries)
        {
            if (matcher.Match(entry) is { } id)
            {
                if (seen.Add(id))
                {
                    desired.Add(id);
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
            desired.Count,
            list.Entries.Count,
            missingEpisodes,
            missingMovies);

        var existing = _playlistManager.GetPlaylists(ownerId)
            .FirstOrDefault(p => string.Equals(p.Name, list.PlaylistName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (desired.Count == 0)
            {
                return;
            }

            await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = list.PlaylistName,
                ItemIdList = desired,
                MediaType = MediaType.Video,
                UserId = ownerId,
                Public = makePublic
            }).ConfigureAwait(false);
            _logger.LogInformation("Watch Order: created playlist {Playlist}.", list.PlaylistName);
            return;
        }

        var current = existing.LinkedChildren.Select(c => c.ItemId ?? Guid.Empty).ToList();
        var orderChanged = !current.SequenceEqual(desired);
        var sharingChanged = existing.OpenAccess != makePublic;
        if (!orderChanged && !sharingChanged)
        {
            return;
        }

        await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
        {
            Id = existing.Id,
            UserId = ownerId,
            Ids = orderChanged ? desired : null,
            Public = makePublic
        }).ConfigureAwait(false);
        _logger.LogInformation("Watch Order: updated playlist {Playlist}.", list.PlaylistName);
    }

    private Guid? ResolveOwner(PluginConfiguration config)
    {
        if (Guid.TryParse(config.OwnerUserId, out var id) && _userManager.GetUserById(id) is not null)
        {
            return id;
        }

        return _userManager.GetUsers()
            .FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator))
            ?.Id;
    }
}
