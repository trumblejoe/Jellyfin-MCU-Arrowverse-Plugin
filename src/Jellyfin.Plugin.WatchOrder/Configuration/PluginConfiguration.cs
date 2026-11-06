using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchOrder.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>User who owns the playlists. Empty = first administrator.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>Let every user on the server see the playlists.</summary>
    public bool MakePublic { get; set; } = true;

    public bool EnableArrowverse { get; set; } = true;

    public bool EnableMcuRelease { get; set; } = true;

    public bool EnableMcuChronological { get; set; }

    public bool EnableMcuSacredTimeline { get; set; }

    // Per-order: only include entries the MCU guide marks as canon.
    public bool McuReleaseCanonOnly { get; set; } = true;

    public bool McuChronologicalCanonOnly { get; set; } = true;

    public bool McuSacredTimelineCanonOnly { get; set; } = true;

    /// <summary>
    /// One mapping per line: "Name on guide site = Name in your library".
    /// Only needed when automatic name matching misses a show.
    /// </summary>
    public string SeriesAliases { get; set; } = string.Empty;
}
