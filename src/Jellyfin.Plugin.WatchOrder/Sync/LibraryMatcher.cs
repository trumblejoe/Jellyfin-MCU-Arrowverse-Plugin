using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchOrder.Sources;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.WatchOrder.Sync;

/// <summary>
/// Maps watch-order entries to items that actually exist (not virtual/missing) in the library.
/// Episodes match on show name + season + episode number. Movies match on title + year, in the
/// Movies library first, then among shows' specials (a movie filed as Season 0 of a series).
/// </summary>
public sealed partial class LibraryMatcher
{
    private readonly Dictionary<string, List<Series>> _seriesByName = new();
    private readonly Dictionary<(Guid SeriesId, int Season, int Episode), Guid> _episodes = new();
    private readonly Dictionary<string, List<Movie>> _moviesByTitle = new();
    private readonly Dictionary<string, List<Special>> _specialsByTitle = new();
    private readonly Dictionary<string, (string Name, int? Year)> _aliases;

    /// <summary>
    /// How far a show's year may be from the year a list gives ("@ Show (2003)"). Premiere dates
    /// vary by country (Battlestar Galactica: 2003 miniseries, 2004 UK, 2005 US), while
    /// same-named shows from different eras (Doctor Who 2005 vs 2023) stay far apart.
    /// </summary>
    private const int ShowYearTolerance = 2;

    public LibraryMatcher(ILibraryManager libraryManager, string aliasText)
        : this(
            libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Series], Recursive = true }).OfType<Series>(),
            libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Episode], Recursive = true, IsVirtualItem = false }).OfType<Episode>(),
            libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Movie], Recursive = true, IsVirtualItem = false }).OfType<Movie>(),
            aliasText)
    {
    }

    /// <summary>Builds the matcher from given items (the library, or test data).</summary>
    internal LibraryMatcher(IEnumerable<Series> allSeries, IEnumerable<Episode> allEpisodes, IEnumerable<Movie> allMovies, string aliasText)
    {
        _aliases = ParseAliases(aliasText);

        foreach (var s in allSeries)
        {
            Add(_seriesByName, Normalize(s.Name), s);
            if (!string.IsNullOrWhiteSpace(s.OriginalTitle))
            {
                Add(_seriesByName, Normalize(s.OriginalTitle), s);
            }
        }

        foreach (var e in allEpisodes)
        {
            if (e.ParentIndexNumber is not { } season || e.IndexNumber is not { } first)
            {
                continue;
            }

            // A single file can cover several episodes (e.g. S01E01-E02).
            var last = e.IndexNumberEnd is { } end && end >= first ? end : first;
            for (var n = first; n <= last; n++)
            {
                _episodes.TryAdd((e.SeriesId, season, n), e.Id);
            }

            // Specials can be movies filed under a show (e.g. a series' feature film).
            if (season == 0)
            {
                Add(_specialsByTitle, Normalize(e.Name), new Special(e.Id, e.PremiereDate?.Year ?? e.ProductionYear));
            }
        }

        foreach (var m in allMovies)
        {
            Add(_moviesByTitle, Normalize(m.Name), m);
            if (!string.IsNullOrWhiteSpace(m.OriginalTitle))
            {
                Add(_moviesByTitle, Normalize(m.OriginalTitle), m);
            }
        }
    }

    /// <summary>Show names from the guide that have no matching series in the library.</summary>
    public HashSet<string> MissingSeries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Guid? Match(WatchOrderEntry entry)
    {
        if (entry.IsEpisode)
        {
            var series = entry.SeriesYear is { } premiered
                ? FindSeriesPremiered(entry.SeriesName!, premiered)
                : FindSeries(entry.SeriesName!, entry.Year);
            if (series is null)
            {
                MissingSeries.Add(entry.SeriesName!);
                return null;
            }

            return _episodes.TryGetValue((series.Id, entry.Season!.Value, entry.Episode!.Value), out var id) ? id : null;
        }

        if (entry.MovieTitle is null)
        {
            return null;
        }

        var titles = new[] { entry.MovieTitle }.Concat(entry.MovieAltTitles ?? []).Select(Normalize).Distinct().ToList();

        // Movies library first, then movies filed as a show's specials (Season 0).
        return titles.Select(t => Pick(_moviesByTitle, t, entry.Year, m => m.ProductionYear, m => m.Id)).FirstOrDefault(id => id is not null)
            ?? titles.Select(t => Pick(_specialsByTitle, t, entry.Year, s => s.Year, s => s.Id)).FirstOrDefault(id => id is not null);
    }

    /// <summary>
    /// The item with this title, if any. With a year, the item's year must be within one
    /// (the same title can exist across decades, e.g. remakes); unknown years are allowed.
    /// </summary>
    private static Guid? Pick<T>(Dictionary<string, List<T>> byTitle, string title, int? year, Func<T, int?> yearOf, Func<T, Guid> idOf)
    {
        if (!byTitle.TryGetValue(title, out var candidates))
        {
            return null;
        }

        if (year is null)
        {
            return idOf(candidates[0]);
        }

        return candidates
            .Where(c => yearOf(c) is null || Math.Abs(yearOf(c)!.Value - year.Value) <= 1)
            .OrderBy(c => yearOf(c) is null ? 99 : Math.Abs(yearOf(c)!.Value - year.Value))
            .Select(c => (Guid?)idOf(c))
            .FirstOrDefault();
    }

    private sealed record Special(Guid Id, int? Year);

    /// <summary>Whether the library has this entry's show (strict premiere year if the list gives one).</summary>
    public bool HasSeries(WatchOrderEntry entry) =>
        entry.SeriesYear is { } premiered
            ? FindSeriesPremiered(entry.SeriesName!, premiered) is not null
            : FindSeries(entry.SeriesName!, entry.Year) is not null;

    /// <summary>Whether the library has this show (by the guide's name, honoring Series Aliases).</summary>
    public bool HasSeries(string siteName, int? year) => FindSeries(siteName, year) is not null;

    /// <summary>
    /// Strict lookup for a built-in list's "@ Show (2023)": only a series whose year is within
    /// <see cref="ShowYearTolerance"/> of that matches, never a same-named series from another era.
    /// A Series Alias wins over the list's year: "Show (2003) = Other Name (2005)" uses 2005, and an
    /// alias without a year accepts the named series whatever its year.
    /// </summary>
    private Series? FindSeriesPremiered(string siteName, int premiered)
    {
        if (_aliases.TryGetValue(Normalize(siteName), out var alias))
        {
            if (alias.Year is null)
            {
                return _seriesByName.TryGetValue(alias.Name, out var named) ? named[0] : null;
            }

            premiered = alias.Year.Value;
        }

        var name = alias.Name ?? Normalize(siteName);
        return _seriesByName.TryGetValue(name, out var candidates)
            ? candidates
                .Select(s => (Series: s, Distance: YearsOf(s).Select(y => Math.Abs(y - premiered)).DefaultIfEmpty(int.MaxValue).Min()))
                .Where(c => c.Distance <= ShowYearTolerance)
                .OrderBy(c => c.Distance)
                .Select(c => c.Series)
                .FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Every year a series is known by: a year in its name ("Battlestar Galactica (2003)"), its
    /// premiere date, and its production year.
    /// </summary>
    private static IEnumerable<int> YearsOf(Series s)
    {
        foreach (var title in new[] { s.Name, s.OriginalTitle })
        {
            if (title is not null && NameYear().Match(title) is { Success: true } m)
            {
                yield return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (s.PremiereDate is { } premiere)
        {
            yield return premiere.Year;
        }

        if (s.ProductionYear is { } production)
        {
            yield return production;
        }
    }

    [GeneratedRegex(@"\((\d{4})\)\s*$")]
    private static partial Regex NameYear();

    private Series? FindSeries(string siteName, int? year)
    {
        if (_aliases.TryGetValue(Normalize(siteName), out var alias) && alias.Year is { } aliasYear)
        {
            // The alias names a specific year's series; use it as-is.
            return FindSeriesPremiered(siteName, aliasYear);
        }

        var name = alias.Name ?? Normalize(siteName);
        if (!_seriesByName.TryGetValue(name, out var candidates))
        {
            return null;
        }

        if (candidates.Count == 1 || year is null)
        {
            return candidates[0];
        }

        // Several shows share the name (e.g. two series called "The Flash"):
        // pick the newest one that had started by the time this episode aired.
        return candidates
                   .Where(s => s.ProductionYear is not null && s.ProductionYear <= year)
                   .OrderByDescending(s => s.ProductionYear)
                   .FirstOrDefault()
               ?? candidates[0];
    }

    /// <summary>
    /// "Name on guide = Name in Jellyfin" lines. A year on the right ("Doctor Who (2005)") is kept,
    /// so an alias can pick one of several same-named shows; a year on the left is ignored.
    /// </summary>
    private static Dictionary<string, (string Name, int? Year)> ParseAliases(string text)
    {
        var map = new Dictionary<string, (string Name, int? Year)>();
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && Normalize(parts[0]).Length > 0 && Normalize(parts[1]).Length > 0)
            {
                var target = parts[1].Trim();
                int? year = NameYear().Match(target) is { Success: true } m
                    ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : null;
                map[Normalize(parts[0])] = (Normalize(target), year);
            }
        }

        return map;
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T item)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        if (!list.Contains(item))
        {
            list.Add(item);
        }
    }

    [GeneratedRegex(@"\(\d{4}\)\s*$")]
    private static partial Regex TrailingYear();

    /// <summary>
    /// "Marvel's Agents of S.H.I.E.L.D." and "Agents of SHIELD" → "agentsofshield";
    /// "The Flash (2014)" → "theflash"; "Superman &amp; Lois" → "supermanandlois".
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = System.Net.WebUtility.HtmlDecode(value).Trim().ToLowerInvariant();
        s = TrailingYear().Replace(s, string.Empty).Trim();
        s = s.Replace("&", " and ", StringComparison.Ordinal)
             .Replace('’', '\'');
        foreach (var prefix in new[] { "marvel's ", "marvel studios' ", "dc's " })
        {
            if (s.StartsWith(prefix, StringComparison.Ordinal))
            {
                s = s[prefix.Length..];
            }
        }

        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
