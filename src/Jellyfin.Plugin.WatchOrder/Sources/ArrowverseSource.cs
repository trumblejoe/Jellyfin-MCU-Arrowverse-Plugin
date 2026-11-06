using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.WatchOrder.Sources;

/// <summary>
/// Reads https://arrowverse.info/ — a single HTML table already in viewing order.
/// Columns: #, Series, Episode (SxxEyy), Name, Original Air Date, Source.
/// The Name column is never read.
/// </summary>
public static partial class ArrowverseSource
{
    public const string Url = "https://arrowverse.info/";

    public const string PlaylistName = "Arrowverse";

    [GeneratedRegex("<tr class=\"episode [^\"]*\">(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    [GeneratedRegex("<td>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"^S(\d+)E(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeRegex();

    public static WatchOrderList Parse(string html)
    {
        var entries = new List<(int Order, WatchOrderEntry Entry)>();
        foreach (Match row in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(row.Groups[1].Value);
            if (cells.Count < 5)
            {
                continue;
            }

            var code = CodeRegex().Match(cells[2].Groups[1].Value.Trim());
            if (!code.Success || !int.TryParse(cells[0].Groups[1].Value.Trim(), CultureInfo.InvariantCulture, out var order))
            {
                continue;
            }

            int? year = DateTime.TryParse(cells[4].Groups[1].Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var aired)
                ? aired.Year
                : null;

            entries.Add((order, new WatchOrderEntry(
                SeriesName: WebUtility.HtmlDecode(cells[1].Groups[1].Value.Trim()),
                Season: int.Parse(code.Groups[1].Value, CultureInfo.InvariantCulture),
                Episode: int.Parse(code.Groups[2].Value, CultureInfo.InvariantCulture),
                MovieTitle: null,
                Year: year)));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("arrowverse.info returned no episode rows; the page layout may have changed.");
        }

        return new WatchOrderList(PlaylistName, entries.OrderBy(e => e.Order).Select(e => e.Entry).ToList());
    }
}
