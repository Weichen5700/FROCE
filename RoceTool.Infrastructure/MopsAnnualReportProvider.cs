using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed partial class MopsAnnualReportProvider(HttpClient httpClient) : IFinancialDataProvider
{
    private const string BaseUrl = "https://mopsov.twse.com.tw/server-java/t164sb01";

    public Task<FinancialStatement?> FetchAnnualAsync(string stockNo, int fiscalYear, CancellationToken cancellationToken = default) =>
        FetchAsync(stockNo, fiscalYear, 4, true, cancellationToken);

    public Task<FinancialStatement?> FetchQuarterAsync(string stockNo, int fiscalYear, int quarter, CancellationToken cancellationToken = default)
    {
        if (quarter is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(quarter));
        return FetchAsync(stockNo, fiscalYear, quarter, false, cancellationToken);
    }

    private async Task<FinancialStatement?> FetchAsync(string stockNo, int fiscalYear, int quarter, bool annual, CancellationToken cancellationToken)
    {
        foreach (var reportId in new[] { "C", "A" })
        {
            var url = $"{BaseUrl}?step=1&CO_ID={Uri.EscapeDataString(stockNo)}&SYEAR={fiscalYear}&SSEASON={quarter}&REPORT_ID={reportId}";
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var statement = ParsePeriod(html, stockNo, fiscalYear, quarter, annual, url);
            if (statement is not null) return statement;
        }
        return null;
    }

    public static FinancialStatement? Parse(string html, string stockNo, int fiscalYear, string sourceUrl = "") =>
        ParsePeriod(html, stockNo, fiscalYear, 4, true, sourceUrl);

    private static FinancialStatement? ParsePeriod(string html, string stockNo, int fiscalYear, int quarter, bool annual, string sourceUrl)
    {
        var endMonth = quarter * 3;
        var endDate = new DateTime(fiscalYear, endMonth, DateTime.DaysInMonth(fiscalYear, endMonth));
        var end = endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var duration = $"From{fiscalYear}0101To{end}";
        var instant = $"AsOf{end}";

        var statement = new FinancialStatement
        {
            StockNo = stockNo,
            FiscalYear = fiscalYear,
            Period = annual ? "FY" : $"Q{quarter}YTD",
            IsAnnual = annual,
            OperatingRevenue = Extract(html, duration, true, "ifrs-full:Revenue"),
            OperatingIncome = Extract(html, duration, true,
                "ifrs-full:ProfitLossFromOperatingActivities", "tifrs-bsci-ci:OperatingIncomeLoss"),
            InterestIncome = Extract(html, duration, true,
                "ifrs-full:RevenueFromInterest", "ifrs-full:InterestIncome",
                "ifrs-full:InterestRevenueCalculatedUsingEffectiveInterestMethod", "tifrs-bsci-ci:InterestIncome"),
            NetIncome = Extract(html, duration, true, "ifrs-full:ProfitLoss"),
            CurrentAssets = Extract(html, instant, true, "ifrs-full:CurrentAssets"),
            TotalAssets = Extract(html, instant, true, "ifrs-full:Assets"),
            CurrentLiabilities = Extract(html, instant, true, "ifrs-full:CurrentLiabilities"),
            TotalLiabilities = Extract(html, instant, true, "ifrs-full:Liabilities"),
            TotalEquity = Extract(html, instant, true, "ifrs-full:Equity"),
            Eps = Extract(html, duration, false, "ifrs-full:BasicEarningsLossPerShare"),
            SourceKind = SourceKind.Mops,
            SourceName = "MOPS iXBRL",
            SourceUrl = sourceUrl,
            FetchedAt = DateTime.UtcNow
        };

        return statement.TotalAssets is null && statement.OperatingIncome is null ? null : statement;
    }

    private static decimal? Extract(string html, string context, bool normalizeToThousands, params string[] concepts)
    {
        foreach (Match match in NonFractionRegex().Matches(html))
        {
            var attrs = match.Groups["attrs"].Value;
            var name = Attribute(attrs, "name");
            if (!concepts.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (!string.Equals(Attribute(attrs, "contextRef"), context, StringComparison.OrdinalIgnoreCase)) continue;
            var text = WebUtility.HtmlDecode(TagRegex().Replace(match.Groups["value"].Value, ""))
                .Replace(",", "").Trim();
            if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) continue;
            if (string.Equals(Attribute(attrs, "sign"), "-", StringComparison.Ordinal)) value = -value;
            if (!normalizeToThousands) return value;
            var scale = int.TryParse(Attribute(attrs, "scale"), out var parsedScale) ? parsedScale : 0;
            return value * Pow10(scale) / 1000m;
        }
        return null;
    }

    private static string Attribute(string attrs, string name)
    {
        var match = Regex.Match(attrs, $@"\b{Regex.Escape(name)}\s*=\s*[""'](?<v>[^""']+)[""']",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : "";
    }

    private static decimal Pow10(int scale)
    {
        var result = 1m;
        if (scale >= 0) for (var i = 0; i < scale; i++) result *= 10m;
        else for (var i = 0; i > scale; i--) result /= 10m;
        return result;
    }

    [GeneratedRegex(@"<ix:nonFraction(?<attrs>[^>]*)>(?<value>.*?)</ix:nonFraction>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NonFractionRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();
}
