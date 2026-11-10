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

    /// <summary>
    /// The playlists ticked in settings (from the settings alone, never from what downloaded,
    /// so a guide being offline can't make a playlist look "turned off").
    /// </summary>
    public static IReadOnlySet<string> EnabledNames(PluginConfiguration config)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.EnableArrowverse)
        {
            names.Add(ArrowverseSource.PlaylistName);
        }

        foreach (var (order, extended) in SyncPlaylistsTask.EnabledMcuPlaylists(config))
        {
            names.Add(McuSource.PlaylistName(order, extended));
        }

        foreach (var list in SyncPlaylistsTask.EnabledBuiltInLists(config))
        {
            names.Add(list.PlaylistName);
        }

        return names;
    }

    /// <summary>The playlists this user gets: the ticked ones minus the user's opt-outs.</summary>
    public static IReadOnlySet<string> NamesFor(PluginConfiguration config, Guid userId)
    {
        var excluded = (config.ExcludedUserPlaylists ?? [])
            .Select(e => e.Split('|', 2))
            .Where(p => p.Length == 2 && Guid.TryParse(p[0], out var id) && id == userId)
            .Select(p => p[1].Trim());
        var names = new HashSet<string>(EnabledNames(config), StringComparer.OrdinalIgnoreCase);
        names.ExceptWith(excluded);
        return names;
    }
}
