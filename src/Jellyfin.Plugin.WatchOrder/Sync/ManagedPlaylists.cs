using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.WatchOrder.Configuration;
using Jellyfin.Plugin.WatchOrder.Sources;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>Shared helpers: which users get playlists and which playlists this plugin manages.</summary>
public static class ManagedPlaylists
{
    /// <summary>
    /// Every playlist name the plugin manages, including pre-1.2 names awaiting rename. Worked out
    /// each time, since downloaded list updates can add playlists without a restart.
    /// </summary>
    public static IReadOnlySet<string> Names => new HashSet<string>(
        Enum.GetValues<McuOrder>()
            .SelectMany(o => new[]
            {
                McuSource.PlaylistName(o, extended: false),
                McuSource.PlaylistName(o, extended: true),
                McuSource.LegacyPlaylistName(o)
            })
            .Append(ArrowverseSource.PlaylistName)
            .Concat(BuiltInLists.All.Select(l => l.PlaylistName)),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The ticked users that still exist. If none are ticked: the pre-1.1 single owner,
    /// else the first administrator.
    /// </summary>
    public static IReadOnlyList<Guid> ResolveUsers(IUserManager userManager, PluginConfiguration config)
    {
        var users = (config.UserIds ?? [])
            .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty && userManager.GetUserById(id) is not null)
            .Distinct()
            .ToList();
        if (users.Count > 0)
        {
            return users;
        }

        if (Guid.TryParse(config.OwnerUserId, out var owner) && userManager.GetUserById(owner) is not null)
        {
            return [owner];
        }

        var admin = userManager.GetUsers().FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator));
        return admin is null ? [] : [admin.Id];
    }

    /// <summary>The user's own copy of a managed playlist (not someone else's shared one).</summary>
    public static Playlist? Find(IPlaylistManager playlistManager, Guid userId, string name) =>
        playlistManager.GetPlaylists(userId)
            .FirstOrDefault(p => p.OwnerUserId == userId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>All of the user's own managed playlists.</summary>
    public static IEnumerable<Playlist> OwnedBy(IPlaylistManager playlistManager, Guid userId) =>
        playlistManager.GetPlaylists(userId)
            .Where(p => p.OwnerUserId == userId && Names.Contains(p.Name));
}
