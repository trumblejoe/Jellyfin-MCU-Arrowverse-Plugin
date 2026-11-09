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
/// Episodes match on show name + season + episode number. Movies match on title + year.
/// </summary>
public sealed partial class LibraryMatcher
{
    private readonly Dictionary<string, List<Series>> _seriesByName = new();
    private readonly Dictionary<(Guid SeriesId, int Season, int Episode), Guid> _episodes = new();
    private readonly Dictionary<string, List<Movie>> _moviesByTitle = new();
    private readonly Dictionary<string, string> _aliases;

    public LibraryMatcher(ILibraryManager libraryManager, string aliasText)
    {
        _aliases = ParseAliases(aliasText);

        foreach (var s in libraryManager.GetItemList(new InternalItemsQuery
                 {
                     IncludeItemTypes = [BaseItemKind.Series],
                     Recursive = true
                 }).OfType<Series>())
        {
            Add(_seriesByName, Normalize(s.Name), s);
            if (!string.IsNullOrWhiteSpace(s.OriginalTitle))
            {
                Add(_seriesByName, Normalize(s.OriginalTitle), s);
            }
        }

        foreach (var e in libraryManager.GetItemList(new InternalItemsQuery
                 {
                     IncludeItemTypes = [BaseItemKind.Episode],
                     Recursive = true,
                     IsVirtualItem = false
                 }).OfType<Episode>())
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
        }

        foreach (var m in libraryManager.GetItemList(new InternalItemsQuery
                 {
                     IncludeItemTypes = [BaseItemKind.Movie],
                     Recursive = true,
                     IsVirtualItem = false
                 }).OfType<Movie>())
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
            var series = FindSeries(entry.SeriesName!, entry.Year);
            if (series is null)
            {
                MissingSeries.Add(entry.SeriesName!);
                return null;
            }

            return _episodes.TryGetValue((series.Id, entry.Season!.Value, entry.Episode!.Value), out var id) ? id : null;
        }

        if (entry.MovieTitle is null || !_moviesByTitle.TryGetValue(Normalize(entry.MovieTitle), out var movies))
        {
            return null;
        }

        if (entry.Year is not { } year)
        {
            return movies[0].Id;
        }

        // Same title can exist across decades (remakes); require the year to be within one.
        return movies
            .Where(m => m.ProductionYear is null || Math.Abs(m.ProductionYear.Value - year) <= 1)
            .OrderBy(m => m.ProductionYear is null ? 99 : Math.Abs(m.ProductionYear.Value - year))
            .Select(m => (Guid?)m.Id)
            .FirstOrDefault();
    }

    /// <summary>Whether the library has this show (by the guide's name, honoring Series Aliases).</summary>
    public bool HasSeries(string siteName, int? year) => FindSeries(siteName, year) is not null;

    private Series? FindSeries(string siteName, int? year)
    {
        var name = _aliases.TryGetValue(Normalize(siteName), out var alias) ? alias : Normalize(siteName);
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

    private static Dictionary<string, string> ParseAliases(string text)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0)
            {
                map[Normalize(parts[0])] = Normalize(parts[1]);
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
