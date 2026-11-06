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
    /// Leave out anything the user has watched, so the first item is always their next one.
    /// Watched items drop off as soon as they're finished; un-marking puts them back.
    /// </summary>
    public bool RemoveWatched { get; set; } = true;

    public bool EnableArrowverse { get; set; } = true;

    public bool EnableMcuRelease { get; set; } = true;

    public bool EnableMcuChronological { get; set; }

    public bool EnableMcuSacredTimeline { get; set; }

    // Per-order: only include entries the MCU guide marks as canon.
    public bool McuReleaseCanonOnly { get; set; } = true;

    public bool McuChronologicalCanonOnly { get; set; } = true;

    public bool McuSacredTimelineCanonOnly { get; set; } = true;

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
