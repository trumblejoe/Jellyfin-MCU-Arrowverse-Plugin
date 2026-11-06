using System.Text.Json;

namespace Jellyfin.Plugin.WatchOrder.Sources;

public enum SnapshotOutcome
{
    /// <summary>Fresh download used and saved as the new last-good copy.</summary>
    Fresh,

    /// <summary>Download failed; the local saved copy was used.</summary>
    UsedSavedCopy,

    /// <summary>Download failed; the GitHub backup copy was used (and saved locally).</summary>
    UsedGitHubCopy,

    /// <summary>Download looked incomplete (far fewer entries than the backup); the backup was used.</summary>
    RejectedShrunkDownload,

    /// <summary>Download failed and there's no backup anywhere.</summary>
    Unavailable
}

/// <summary>Saved copy of one guide: when it last changed, and its entries in order.</summary>
public sealed class GuideSnapshot<T>
{
    public DateTime SavedUtc { get; set; }

    public List<T> Items { get; set; } = [];
}

/// <summary>
/// Keeps a "last good copy" of each guide so playlists can still be rebuilt if a guide site
/// goes offline or breaks. Used both by the plugin (in its data folder on the Jellyfin server)
/// and by the GitHub workflow that maintains the backup copy on the repo's <c>data</c> branch.
///
/// Holds only parsed ordering data (show names, season/episode numbers, dates, order fields,
/// and movie titles for matching) — never episode titles or synopses.
/// </summary>
public sealed class GuideSnapshotStore
{
    /// <summary>A download with fewer than this share of the backup's entries is treated as broken.</summary>
    public const double MinimumShareOfSaved = 0.8;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;

    public GuideSnapshotStore(string folder)
    {
        _folder = folder;
    }

    public static string FileName(string name) => $"{name}.snapshot.json";

    public static GuideSnapshot<T>? Parse<T>(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<GuideSnapshot<T>>(json, JsonOptions);
            return snapshot is { Items.Count: > 0 } ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decide which data to use: the fresh download (null if it failed), else the newest of
    /// the local saved copy and the GitHub copy (null if unavailable).
    /// Saves whatever is used locally so the next run has it.
    /// </summary>
    public (IReadOnlyList<T>? Items, SnapshotOutcome Outcome, DateTime? SavedUtc) Resolve<T>(
        string name,
        IReadOnlyList<T>? fresh,
        GuideSnapshot<T>? github = null)
    {
        var local = Load<T>(name);
        var backup = local;
        var backupIsGitHub = false;
        if (github is not null && (local is null || github.SavedUtc > local.SavedUtc))
        {
            backup = github;
            backupIsGitHub = true;
        }

        if (fresh is { Count: > 0 })
        {
            if (backup is null || fresh.Count >= backup.Items.Count * MinimumShareOfSaved)
            {
                var savedUtc = Save(name, fresh, DateTime.UtcNow);
                return (fresh, SnapshotOutcome.Fresh, savedUtc);
            }

            if (backupIsGitHub)
            {
                Save(name, backup.Items, backup.SavedUtc);
            }

            return (backup.Items, SnapshotOutcome.RejectedShrunkDownload, backup.SavedUtc);
        }

        if (backup is null)
        {
            return (null, SnapshotOutcome.Unavailable, null);
        }

        if (backupIsGitHub)
        {
            Save(name, backup.Items, backup.SavedUtc);
            return (backup.Items, SnapshotOutcome.UsedGitHubCopy, backup.SavedUtc);
        }

        return (backup.Items, SnapshotOutcome.UsedSavedCopy, backup.SavedUtc);
    }

    /// <summary>The local saved copy, if any.</summary>
    public GuideSnapshot<T>? Peek<T>(string name) => Load<T>(name);

    private string PathFor(string name) => Path.Combine(_folder, FileName(name));

    private GuideSnapshot<T>? Load<T>(string name)
    {
        try
        {
            var path = PathFor(name);
            return File.Exists(path) ? Parse<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the copy, unless the entries are identical to what's already saved
    /// (so SavedUtc means "last changed" and the GitHub branch only gets real changes).
    /// Returns the effective SavedUtc.
    /// </summary>
    private DateTime Save<T>(string name, IReadOnlyList<T> items, DateTime savedUtc)
    {
        var existing = Load<T>(name);
        if (existing is not null
            && JsonSerializer.Serialize(existing.Items, JsonOptions) == JsonSerializer.Serialize(items, JsonOptions))
        {
            return existing.SavedUtc;
        }

        Directory.CreateDirectory(_folder);
        var path = PathFor(name);
        var temp = path + ".tmp";

        // Write to a temp file first so a crash mid-write can't corrupt the last good copy.
        File.WriteAllText(temp, JsonSerializer.Serialize(new GuideSnapshot<T> { SavedUtc = savedUtc, Items = items.ToList() }, JsonOptions));
        File.Move(temp, path, overwrite: true);
        return savedUtc;
    }
}
