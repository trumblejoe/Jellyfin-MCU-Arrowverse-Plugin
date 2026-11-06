using Jellyfin.Plugin.WatchOrder.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// With "Remove watched items" on, keeps the playlists current between nightly syncs:
/// as soon as a selected user finishes (or marks watched) an item, it's removed from that
/// user's own copies of our playlists,
/// so the first item is always the next unwatched one. Marking something unwatched queues a
/// full sync to put it back in the right place.
/// </summary>
public sealed class WatchedListener : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<WatchedListener> _logger;

    public WatchedListener(
        IUserDataManager userDataManager,
        IUserManager userManager,
        IPlaylistManager playlistManager,
        ITaskManager taskManager,
        ILogger<WatchedListener> logger)
    {
        _userDataManager = userDataManager;
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

            var itemId = e.Item.Id;
            // Only this user's own copies; other users' playlists follow their own watched status.
            foreach (var playlist in ManagedPlaylists.OwnedBy(_playlistManager, e.UserId)
                         .Where(p => p.LinkedChildren.Any(c => c.ItemId == itemId))
                         .ToList())
            {
                await _playlistManager.RemoveItemFromPlaylistAsync(
                        playlist.Id.ToString("N"),
                        [itemId.ToString("N")])
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Never let a playlist hiccup break playback reporting; the nightly sync will catch up.
            _logger.LogWarning(ex, "Watch Order: couldn't remove a watched item from the playlists.");
        }
    }
}
