namespace Jellyfin.Plugin.WatchOrder.Api;

/// <summary>
/// For the settings page's status line. Worked out on request from what's already there:
/// Jellyfin's record of the sync task's last run, and the dates on the guide and list copies
/// saved on the server. Nothing extra is stored.
/// </summary>
public sealed class StatusInfo
{
    public DateTime? LastSyncUtc { get; init; }

    /// <summary>Completed, Failed, Cancelled or Aborted; null if the sync has never run.</summary>
    public string? LastSyncStatus { get; init; }

    /// <summary>When the MCU / Arrowverse guide data last changed.</summary>
    public DateTime? McuUpdatedUtc { get; init; }

    public DateTime? ArrowverseUpdatedUtc { get; init; }

    /// <summary>When a list update was last received, and how many lists came from updates.</summary>
    public DateTime? ListsUpdatedUtc { get; init; }

    public int ListsDownloaded { get; init; }

    /// <summary>False = no Backup data URL, so lists only change with plugin releases.</summary>
    public bool ListUpdatesOn { get; init; }
}
