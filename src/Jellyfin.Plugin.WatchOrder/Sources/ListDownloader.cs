using System.Text.Json;

namespace Jellyfin.Plugin.WatchOrder.Sources;

/// <summary>
/// Downloads updated built-in lists from the Backup data URL (the repo's data branch, where the
/// weekly "Update lists" workflow publishes them as <c>lists/index.json</c> + <c>lists/*.txt</c>),
/// so list updates reach the server without a plugin release or restart.
///
/// Safety: only when a Backup data URL is set (blank = never contacted); a file must pass the
/// list format check; a download with far fewer items than the list in effect (under 80%) is
/// treated as broken; anything that fails keeps the last good copy.
/// </summary>
public static class ListDownloader
{
    public sealed record Result(int Updated, int Unchanged, int Rejected, string? Error);

    public static async Task<Result> RefreshAsync(HttpClient http, string? backupUrl, string folder, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(backupUrl))
        {
            return new Result(0, 0, 0, null);
        }

        var baseUrl = backupUrl.TrimEnd('/') + "/lists/";
        List<string> keys;
        try
        {
            keys = JsonSerializer.Deserialize<List<string>>(await http.GetStringAsync(baseUrl + "index.json", ct).ConfigureAwait(false)) ?? [];
        }
        catch (Exception ex) when (IsDownloadProblem(ex, ct))
        {
            return new Result(0, 0, 0, ex.Message);
        }

        var current = BuiltInLists.LoadByKey(folder);

        int updated = 0, unchanged = 0, rejected = 0;
        Directory.CreateDirectory(folder);
        foreach (var key in keys.Where(IsSafeKey))
        {
            string text;
            WatchOrderList fresh;
            try
            {
                text = await http.GetStringAsync(baseUrl + key + ".txt", ct).ConfigureAwait(false);
                fresh = BuiltInLists.Parse(text);
            }
            catch (Exception ex) when (IsDownloadProblem(ex, ct))
            {
                rejected++;
                continue;
            }

            if (current.TryGetValue(key, out var existing)
                && fresh.Entries.Count < existing.Entries.Count * GuideSnapshotStore.MinimumShareOfSaved)
            {
                rejected++;
                continue;
            }

            var path = Path.Combine(folder, key + ".txt");
            if (File.Exists(path) && File.ReadAllText(path) == text)
            {
                unchanged++;
                continue;
            }

            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, text, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            updated++;
        }

        return new Result(updated, unchanged, rejected, null);
    }

    /// <summary>File keys look like "star-wars-release-order": nothing that could escape the folder.</summary>
    private static bool IsSafeKey(string key) =>
        key.Length is > 0 and <= 100 && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    private static bool IsDownloadProblem(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or InvalidDataException or JsonException or IOException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
