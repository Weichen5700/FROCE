using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed partial class MopsDividendProvider(HttpClient httpClient)
{
    public static string QueryUrl(string stockNo, int firstYear, int lastYear) =>
        "https://mopsov.twse.com.tw/mops/web/ajax_t05st09_2?encodeURIComponent=1&step=1&firstin=true&off=1&TYPEK=all" +
        $"&co_id={Uri.EscapeDataString(stockNo)}&date1={firstYear - 1911}&date2={lastYear - 1911}&qryType=2&isnew=false";

    public async Task<IReadOnlyList<AnnualDividend>> FetchAsync(string stockNo, int firstYear, int lastYear,
        CancellationToken cancellationToken = default)
    {
        var url = QueryUrl(stockNo, firstYear, lastYear);
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(html, stockNo, firstYear, lastYear, url);
    }

    public static IReadOnlyList<AnnualDividend> Parse(string html, string stockNo, int firstYear, int lastYear, string sourceUrl)
    {
        // Ordinary shares are the first hasBorder table; preferred shares have a different schema.
        var table = OrdinaryTableRegex().Match(html);
        if (!table.Success) return [];
        var periods = new List<DividendPeriod>();
        foreach (Match row in RowRegex().Matches(table.Groups["body"].Value))
        {
            var cells = CellRegex().Matches(row.Groups["body"].Value)
                .Select(x => Clean(x.Groups["body"].Value)).ToArray();
            if (cells.Length != 21) continue;
            var yearMatch = Regex.Match(cells[1], @"^(?<year>\d{3,4})年");
            if (!yearMatch.Success || !int.TryParse(yearMatch.Groups["year"].Value, out var year)) continue;
            if (year < 1911) year += 1911;
            if (year < firstYear || year > lastYear) continue;
            periods.Add(new(year, cells[2], cells[3], cells[0], cells[4],
                Number(cells[10]), AddKnown(Number(cells[11]), Number(cells[12])),
                Number(cells[14]), AddKnown(Number(cells[15]), Number(cells[16]))));
        }
        return periods.GroupBy(x => new { x.Year, x.Period, x.Sequence })
            .Select(group => group.OrderByDescending(x => x.Status.Contains("確認", StringComparison.Ordinal))
                .ThenByDescending(x => x.DecisionDate, StringComparer.Ordinal).First())
            .GroupBy(x => x.Year)
            .Select(group => new AnnualDividend
            {
                StockNo = stockNo, FiscalYear = group.Key,
                CashEarnings = SumKnown(group.Select(x => x.CashEarnings)),
                CashReserves = SumKnown(group.Select(x => x.CashReserves)),
                StockEarnings = SumKnown(group.Select(x => x.StockEarnings)),
                StockReserves = SumKnown(group.Select(x => x.StockReserves)),
                DecisionStatus = string.Join("；", group.Select(x => x.Status).Distinct()),
                DecisionDate = string.Join("；", group.Select(x => x.DecisionDate).Distinct()),
                SourceUrl = sourceUrl, FetchedAt = DateTime.UtcNow
            }).OrderByDescending(x => x.FiscalYear).ToList();
    }

    private static string Clean(string html) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();
    private static decimal? Number(string text) => decimal.TryParse(text.Replace(",", ""),
        NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static decimal? AddKnown(decimal? left, decimal? right) => left + right;
    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var list = values.ToArray();
        return list.Any(x => x is null) ? null : list.Sum(x => x!.Value);
    }
    private sealed record DividendPeriod(int Year, string Period, string Sequence, string Status, string DecisionDate,
        decimal? CashEarnings, decimal? CashReserves, decimal? StockEarnings, decimal? StockReserves);

    [GeneratedRegex(@"<table\b[^>]*class\s*=\s*['"" ]hasBorder['"" ][^>]*>(?<body>.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OrdinaryTableRegex();
    [GeneratedRegex(@"<tr\b[^>]*>(?<body>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRegex();
    [GeneratedRegex(@"<td\b[^>]*>(?<body>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRegex();
}
