using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.WatchOrder.Sources;

/// <summary>
/// Watch orders shipped inside the plugin (the <c>Lists/*.txt</c> files), for franchises whose
/// guides have no data feed. Nothing is downloaded; lists change only with plugin releases.
///
/// File format (one item per line; blank lines and <c># comments</c> ignored):
/// <code>
/// playlist: Batman TAS Fan Order        the playlist name (required, once)
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
    private static readonly Lazy<IReadOnlyList<WatchOrderList>> Loaded = new(LoadEmbedded);

    /// <summary>Every list shipped with the plugin, sorted by playlist name.</summary>
    public static IReadOnlyList<WatchOrderList> All => Loaded.Value;

    [GeneratedRegex(@"^S(\d+)E(\d+)(?:-(?:S(\d+))?E(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"^(.*?)\s*\((\d{4})\)$")]
    private static partial Regex NameYearRegex();

    public static WatchOrderList Parse(string text)
    {
        string? playlist = null;
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

        return new WatchOrderList(playlist, entries);
    }

    private static int Int(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

    private static (string Name, int? Year) SplitYear(string value) =>
        NameYearRegex().Match(value) is { Success: true } m
            ? (m.Groups[1].Value, Int(m.Groups[2]))
            : (value, null);

    private static IReadOnlyList<WatchOrderList> LoadEmbedded()
    {
        var assembly = typeof(BuiltInLists).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Lists.", StringComparison.Ordinal) && n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .Select(n => Parse(Read(assembly, n)))
            .OrderBy(l => l.PlaylistName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
