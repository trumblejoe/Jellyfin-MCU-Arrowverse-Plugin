using Jellyfin.Plugin.WatchOrder.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// With "Start at my next unwatched item" on, keeps the playlists current between nightly syncs:
/// when a selected user finishes (or marks watched) an item, the watched items at the FRONT of
/// that user's own playlists are removed, so the first item is always the next unwatched one.
/// Items watched out of order further down stay put. Marking something unwatched queues a full
/// sync, which moves the starting point back if needed.
/// </summary>
public sealed class WatchedListener : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<WatchedListener> _logger;

    public WatchedListener(
        IUserDataManager userDataManager,
        IUserManager userManager,
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        ILogger<WatchedListener> logger)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _playlistManager = playlistManager;
        _taskManager = taskManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private async void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!config.RemoveWatched || e.Item is null || e.UserData is null)
            {
                return;
            }

            if (!ManagedPlaylists.ResolveUsers(_userManager, config).Contains(e.UserId))
            {
                return;
            }

            if (!e.UserData.Played)
            {
                // Progress updates fire constantly; only an explicit "mark unwatched" matters.
                if (e.SaveReason == UserDataSaveReason.TogglePlayed)
                {
                    _taskManager.QueueIfNotRunning<SyncPlaylistsTask>();
                }

                return;
            }

            var user = _userManager.GetUserById(e.UserId);
            if (user is null)
            {
                return;
            }

            var itemId = e.Item.Id;

            // Only this user's own copies; other users' playlists follow their own watched status.
            // Only the watched run at the FRONT is removed: something watched out of order further
            // down stays in its place until the user gets there.
            foreach (var playlist in ManagedPlaylists.OwnedBy(_playlistManager, e.UserId)
                         .Where(p => p.LinkedChildren.Any(c => c.ItemId == itemId))
                         .ToList())
            {
                var ids = playlist.LinkedChildren.Select(c => c.ItemId ?? Guid.Empty).ToList();
                var front = WatchProgress.WatchedAtFront(ids, id => IsWatched(user, id, itemId));
                if (front.Count > 0)
                {
                    await _playlistManager.RemoveItemFromPlaylistAsync(
                            playlist.Id.ToString("N"),
                            front.Select(id => id.ToString("N")))
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            // Never let a playlist hiccup break playback reporting; the nightly sync will catch up.
            _logger.LogWarning(ex, "Watch Order: couldn't remove a watched item from the playlists.");
        }
    }

    /// <summary>
    /// Watched status for one playlist item. The item that was just finished counts as watched
    /// even if its saved user data isn't visible yet.
    /// </summary>
    private bool IsWatched(Jellyfin.Database.Implementations.Entities.User user, Guid id, Guid justWatched)
    {
        if (id == justWatched)
        {
            return true;
        }

        var item = _libraryManager.GetItemById(id);
        return item is not null && _userDataManager.GetUserData(user, item)?.Played == true;
    }
}
