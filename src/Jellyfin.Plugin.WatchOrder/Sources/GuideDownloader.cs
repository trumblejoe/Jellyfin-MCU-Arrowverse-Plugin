using System.Text.Json;

namespace Jellyfin.Plugin.WatchOrder.Sources;

/// <summary>
/// Downloads both guides plus their GitHub backup copies, and resolves which data to use
/// (site → newest of local/GitHub copy). Shared by the plugin and the GitHub data workflow.
/// </summary>
public static class GuideDownloader
{
    public const string ArrowverseName = "arrowverse";
    public const string McuName = "mcu";

    public sealed record Result<T>(IReadOnlyList<T>? Items, SnapshotOutcome Outcome, DateTime? SavedUtc, int? FreshCount, string? Error);

    /// <param name="backupUrl">
    /// Folder URL of the GitHub backup copies (blank = never contact GitHub). Only fetched when
    /// actually needed: the site failed, looks broken, or there's no local copy yet.
    /// </param>
    public static async Task<Result<WatchOrderEntry>> ArrowverseAsync(HttpClient http, GuideSnapshotStore store, string? backupUrl, CancellationToken ct)
    {
        IReadOnlyList<WatchOrderEntry>? fresh = null;
        string? error = null;
        try
        {
            fresh = ArrowverseSource.Parse(await http.GetStringAsync(ArrowverseSource.Url, ct).ConfigureAwait(false)).Entries;
        }
        catch (Exception ex) when (IsDownloadProblem(ex, ct))
        {
            error = ex.Message;
        }

        var github = await BackupIfNeededAsync(http, store, backupUrl, ArrowverseName, fresh, ct).ConfigureAwait(false);
        var (items, outcome, savedUtc) = store.Resolve(ArrowverseName, fresh, github);
        return new Result<WatchOrderEntry>(items, outcome, savedUtc, fresh?.Count, error);
    }

    public static async Task<Result<McuSource.McuItem>> McuAsync(HttpClient http, GuideSnapshotStore store, string? backupUrl, CancellationToken ct)
    {
        IReadOnlyList<McuSource.McuItem>? fresh = null;
        string? error = null;
        try
        {
            fresh = McuSource.ParseItems(await http.GetStringAsync(McuSource.Url, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (IsDownloadProblem(ex, ct))
        {
            error = ex.Message;
        }

        var github = await BackupIfNeededAsync(http, store, backupUrl, McuName, fresh, ct).ConfigureAwait(false);
        var (items, outcome, savedUtc) = store.Resolve(McuName, fresh, github);
        if (items is not null)
        {
            McuSource.Index(items);
        }

        return new Result<McuSource.McuItem>(items, outcome, savedUtc, fresh?.Count, error);
    }

    private static async Task<GuideSnapshot<T>?> BackupIfNeededAsync<T>(
        HttpClient http,
        GuideSnapshotStore store,
        string? backupUrl,
        string name,
        IReadOnlyList<T>? fresh,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(backupUrl))
        {
            return null;
        }

        var local = store.Peek<T>(name);
        var needed = fresh is not { Count: > 0 }
            || local is null
            || fresh.Count < local.Items.Count * GuideSnapshotStore.MinimumShareOfSaved;
        if (!needed)
        {
            return null;
        }

        try
        {
            var url = backupUrl.TrimEnd('/') + "/" + GuideSnapshotStore.FileName(name);
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            return GuideSnapshotStore.Parse<T>(json);
        }
        catch (Exception ex) when (IsDownloadProblem(ex, ct))
        {
            return null;
        }
    }

    private static bool IsDownloadProblem(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or InvalidDataException or JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
