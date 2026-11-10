using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.WatchOrder.Sources;

/// <summary>
/// Watch orders shipped inside the plugin (the <c>Lists/*.txt</c> files), for franchises whose
/// guides have no data feed. If the server admin set a Backup data URL, newer copies are
/// downloaded daily (see <see cref="ListDownloader"/>) so list updates need no plugin release.
///
/// File format (one item per line; blank lines and <c># comments</c> ignored):
/// <code>
/// playlist: Batman TAS Fan Order        the playlist name (required, once)
/// description: A fan-curated order ...  shown on the playlist in Jellyfin; spoiler-free (lines join)
/// updates: finished                     'finished' (never checked), 'auto' (release-order list: newly
///                                       aired episodes are appended each week) or 'ongoing' (hand-
///                                       ordered: new episodes go right after that show's last one)
/// upcoming: movie: Some Movie (2027-05-28) | alt: Other Title | after: Earlier Movie
///                                       added as a movie entry once its date passes (not played before)
/// upcoming: New series (2027-01-01)     a reminder only, reported when its date passes
/// @ Batman: The Animated Series         the show for the codes that follow
/// @ Doctor Who (2023)                   optional premiere year: only a series that premiered then matches
/// S01E13                                one episode
/// S01E02-E05                            a range within one season (also S01E02-S01E05)
/// movie: Some Movie (1993)              a movie, by title and year (Movies library, or a show's specials)
/// movie: Main Title (2001) | Other Title  alternate titles after "|"
/// </code>
/// Episode codes must match the library's numbering (TheTVDB/TMDB aired order).
/// Lists never contain episode titles or notes, to stay spoiler-free.
/// </summary>
public static partial class BuiltInLists
{
    /// <summary>
    /// Every list in effect, sorted by playlist name: the ones shipped with the plugin, updated by
    /// any newer copies downloaded to the server (read fresh each time, so updates apply without
    /// a restart).
    /// </summary>
    public static IReadOnlyList<WatchOrderList> All => LoadEmbedded();

    [GeneratedRegex(@"^S(\d+)E(\d+)(?:-(?:S(\d+))?E(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"^(.*?)\s*\((\d{4})\)$")]
    private static partial Regex NameYearRegex();

    [GeneratedRegex(@"^.+\(\d{4}(-\d{2}-\d{2})?\)(\s*\|.*)?$")]
    private static partial Regex UpcomingRegex();

    public static WatchOrderList Parse(string text)
    {
        string? playlist = null;
        string? description = null;
        bool? ongoing = null;
        string? show = null;
        int? showYear = null;
        var entries = new List<WatchOrderEntry>();
        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("playlist:", StringComparison.OrdinalIgnoreCase))
            {
                playlist = line["playlist:".Length..].Trim();
            }
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                // Several description lines join into one paragraph.
                var part = line["description:".Length..].Trim();
                description = description is null ? part : description + " " + part;
            }
            else if (line.StartsWith("updates:", StringComparison.OrdinalIgnoreCase))
            {
                // For the repo's list-update checks only; the plugin doesn't use it.
                ongoing = line["updates:".Length..].Trim().ToLowerInvariant() switch
                {
                    "auto" or "ongoing" => true,
                    "finished" => false,
                    _ => throw new InvalidDataException($"Line {lineNumber}: updates must be 'auto', 'ongoing' or 'finished'.")
                };
            }
            else if (line.StartsWith("upcoming:", StringComparison.OrdinalIgnoreCase))
            {
                // Announced titles, for the repo's update checks only; not a playlist item.
                if (!UpcomingRegex().IsMatch(line["upcoming:".Length..].Trim()))
                {
                    throw new InvalidDataException($"Line {lineNumber}: upcoming needs a date, e.g. 'upcoming: Title (2027-05-28)'.");
                }
            }
            else if (line.StartsWith('@'))
            {
                (show, showYear) = SplitYear(line[1..].Trim());
            }
            else if (line.StartsWith("movie:", StringComparison.OrdinalIgnoreCase))
            {
                // movie: Main Title (Year) | Alternate Title | Another Title
                var parts = line["movie:".Length..].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    throw new InvalidDataException($"Line {lineNumber}: movie needs a title.");
                }

                var (title, year) = SplitYear(parts[0]);
                entries.Add(new WatchOrderEntry(null, null, null, title, year, parts.Length > 1 ? parts[1..] : null));
            }
            else if (CodeRegex().Match(line) is { Success: true } m)
            {
                if (show is null)
                {
                    throw new InvalidDataException($"Line {lineNumber}: episode code before any '@ Show' line.");
                }

                var season = Int(m.Groups[1]);
                var first = Int(m.Groups[2]);
                var last = m.Groups[4].Success ? Int(m.Groups[4]) : first;
                if (m.Groups[3].Success && Int(m.Groups[3]) != season)
                {
                    throw new InvalidDataException($"Line {lineNumber}: a range must stay within one season.");
                }

                if (last < first)
                {
                    throw new InvalidDataException($"Line {lineNumber}: range ends before it starts.");
                }

                for (var e = first; e <= last; e++)
                {
                    entries.Add(new WatchOrderEntry(show, season, e, null, showYear, SeriesYear: showYear));
                }
            }
            else
            {
                throw new InvalidDataException($"Line {lineNumber}: not understood.");
            }
        }

        if (string.IsNullOrWhiteSpace(playlist))
        {
            throw new InvalidDataException("Missing 'playlist:' line.");
        }

        return new WatchOrderList(playlist, entries) { Ongoing = ongoing, Description = description };
    }

    private static int Int(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

    private static (string Name, int? Year) SplitYear(string value) =>
        NameYearRegex().Match(value) is { Success: true } m
            ? (m.Groups[1].Value, Int(m.Groups[2]))
            : (value, null);

    /// <summary>
    /// The lists shipped inside the plugin, by file key (e.g. "batman-tas-fan-order").
    /// </summary>
    public static IReadOnlyDictionary<string, WatchOrderList> Embedded => EmbeddedByKey.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, WatchOrderList>> EmbeddedByKey = new(() =>
    {
        const string Marker = ".Lists.";
        var assembly = typeof(BuiltInLists).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(Marker, StringComparison.Ordinal) && n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                n => n[(n.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..^".txt".Length],
                n => Parse(Read(assembly, n)),
                StringComparer.OrdinalIgnoreCase);
    });

    /// <summary>
    /// The lists in effect: the embedded ones, each replaced by a downloaded copy in
    /// <paramref name="downloadFolder"/> when one exists and parses (see <see cref="ListDownloader"/>),
    /// plus any list that only exists as a download. Sorted by playlist name.
    /// </summary>
    public static IReadOnlyList<WatchOrderList> Load(string? downloadFolder) =>
        LoadByKey(downloadFolder).Values.OrderBy(l => l.PlaylistName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The lists in effect, by file key (embedded, replaced by valid downloaded copies).</summary>
    public static Dictionary<string, WatchOrderList> LoadByKey(string? downloadFolder)
    {
        var lists = new Dictionary<string, WatchOrderList>(Embedded, StringComparer.OrdinalIgnoreCase);
        if (downloadFolder is not null && Directory.Exists(downloadFolder))
        {
            foreach (var file in Directory.EnumerateFiles(downloadFolder, "*.txt"))
            {
                try
                {
                    lists[Path.GetFileNameWithoutExtension(file)] = Parse(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    // A bad download never replaces a good list.
                }
            }
        }

        return lists;
    }

    private static IReadOnlyList<WatchOrderList> LoadEmbedded() => Load(DownloadFolder);

    /// <summary>Where downloaded list updates live: the plugin's data folder on the server.</summary>
    public static string? DownloadFolder =>
        Plugin.Instance?.DataFolderPath is { } data ? Path.Combine(data, "lists") : null;

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
