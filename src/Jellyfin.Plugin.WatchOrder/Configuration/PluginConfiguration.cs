using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchOrder.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Users who get their own private copy of each playlist. Empty = first administrator
    /// (or the pre-1.1 single owner, if one was set).
    /// </summary>
    public string[] UserIds { get; set; } = [];

    /// <summary>Pre-1.1 single playlist owner. Only read as a fallback when <see cref="UserIds"/> is empty.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>
    /// "Start at my next unwatched item": each user's playlists drop watched items from the front
    /// (up to the first unwatched one), so Play continues where they left off. Items watched out
    /// of order further down stay in place. (Property name kept from 1.1 for existing settings.)
    /// </summary>
    public bool RemoveWatched { get; set; } = true;

    /// <summary>
    /// Delete this plugin's playlists that are turned off: a list unticked, a user unticked, or a
    /// playlist a user was opted out of. Only playlists this plugin made are ever touched.
    /// </summary>
    public bool DeleteTurnedOff { get; set; } = true;

    /// <summary>
    /// Per-user opt-outs, as "userId|Playlist Name". Every ticked user gets every ticked playlist
    /// except these.
    /// </summary>
    public string[] ExcludedUserPlaylists { get; set; } = [];

    public bool EnableArrowverse { get; set; } = true;

    // MCU playlists: each order has a canon playlist and an Extended (canon + non-canon) playlist.
    public bool EnableMcuRelease { get; set; } = true;

    public bool EnableMcuReleaseExtended { get; set; }

    public bool EnableMcuChronological { get; set; }

    public bool EnableMcuChronologicalExtended { get; set; }

    public bool EnableMcuSacredTimeline { get; set; }

    public bool EnableMcuSacredTimelineExtended { get; set; }

    /// <summary>MCU Hybrid: the first-time-viewer order (see McuOrder.Hybrid).</summary>
    public bool EnableMcuHybrid { get; set; }

    public bool EnableMcuHybridExtended { get; set; }

    /// <summary>Playlist names of the built-in lists (Lists/*.txt) that are turned on.</summary>
    public string[] EnabledLists { get; set; } = [];

    // Pre-1.2 per-order "canon only" switches. Only read by MigrateLegacyCanonSettings.
    public bool McuReleaseCanonOnly { get; set; } = true;

    public bool McuChronologicalCanonOnly { get; set; } = true;

    public bool McuSacredTimelineCanonOnly { get; set; } = true;

    /// <summary>
    /// Pre-1.2, an order with "canon only" unticked meant the playlist included non-canon entries.
    /// That's now the order's Extended playlist. Safe to run every load: once converted, the old
    /// switches are reset to their default (true), which converts nothing.
    /// </summary>
    /// <returns>True if anything changed and the configuration should be saved.</returns>
    public bool MigrateLegacyCanonSettings()
    {
        var changed = false;
        if (!McuReleaseCanonOnly)
        {
            EnableMcuReleaseExtended |= EnableMcuRelease;
            EnableMcuRelease = false;
            McuReleaseCanonOnly = true;
            changed = true;
        }

        if (!McuChronologicalCanonOnly)
        {
            EnableMcuChronologicalExtended |= EnableMcuChronological;
            EnableMcuChronological = false;
            McuChronologicalCanonOnly = true;
            changed = true;
        }

        if (!McuSacredTimelineCanonOnly)
        {
            EnableMcuSacredTimelineExtended |= EnableMcuSacredTimeline;
            EnableMcuSacredTimeline = false;
            McuSacredTimelineCanonOnly = true;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Optional folder URL of a guide backup (e.g. a repo's data branch on raw.githubusercontent.com).
    /// Blank by default, so installs never contact anyone's GitHub unless the server admin opts in.
    /// Only used when a guide site fails, looks broken, or there's no local copy yet.
    /// </summary>
    public string BackupDataUrl { get; set; } = string.Empty;

    /// <summary>
    /// One mapping per line: "Name on guide site = Name in your library".
    /// Only needed when automatic name matching misses a show.
    /// </summary>
    public string SeriesAliases { get; set; } = string.Empty;
}
