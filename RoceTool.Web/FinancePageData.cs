using Microsoft.EntityFrameworkCore;
using System.Globalization;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Web.Pages;

public static class FinancePageData
{
    public const string IndustryCode = "17";
    public const decimal TargetYieldPercent = 5m;
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    public static IQueryable<Stock> Companies(RoceDbContext db) => db.Stocks.AsNoTracking()
        .Where(x => x.StatementKind == StatementKind.Financial && x.IsSupported && x.IndustryCode == IndustryCode);

    public static async Task<int> ReferenceYearAsync(RoceDbContext db, CancellationToken cancellationToken)
    {
        var latestYear = await (
            from statement in db.FinancialStatements.AsNoTracking()
            join stock in Companies(db) on statement.StockNo equals stock.StockNo
            where statement.IsAnnual
            select (int?)statement.FiscalYear).MaxAsync(cancellationToken);
        return latestYear ?? DateTime.UtcNow.AddHours(8).Year - 1;
    }

    public static IReadOnlyList<int> HistoryYears(int referenceYear) =>
        Enumerable.Range(referenceYear - 6, 7).Reverse().ToArray();

    public static FinancialCompanyRow CompanyRow(Stock stock, IEnumerable<FinancialStatement> statements,
        IEnumerable<AnnualDividend>? dividends = null) =>
        new(stock, statements.Where(x => x.IsAnnual).GroupBy(x => x.FiscalYear)
            .ToDictionary(group => group.Key, ChooseSource))
        { Dividends = (dividends ?? []).ToDictionary(x => x.FiscalYear) };

    public static bool HasCoreSummary(FinancialStatement? statement) =>
        statement is not null && statement.Eps is not null && statement.NetIncome is not null &&
        statement.TotalEquity is not null && statement.TotalAssets is not null;

    public static string NormalizeSort(string? sort) => sort?.Trim().ToLowerInvariant() switch
    {
        "stock" => "stock",
        "market" => "market",
        "roe" => "roe",
        "profit" => "profit",
        "coverage" => "coverage",
        "cash" => "cash",
        "stockdividend" => "stockdividend",
        "payout" => "payout",
        "price" => "price",
        "yield" => "yield",
        "target" => "target",
        _ => "eps"
    };

    public static string DefaultDirection(string sort) => sort is "stock" or "market" ? "asc" : "desc";

    public static string NormalizeDirection(string? direction, string sort) => direction?.ToLowerInvariant() switch
    {
        "asc" => "asc",
        "desc" => "desc",
        _ => DefaultDirection(sort)
    };

    public static IReadOnlyList<FinancialCompanyRow> SortRows(
        IEnumerable<FinancialCompanyRow> rows, string sort, string direction, int referenceYear)
    {
        var descending = direction == "desc";
        var years = HistoryYears(referenceYear);
        IOrderedEnumerable<FinancialCompanyRow> ordered = sort switch
        {
            "stock" => descending
                ? rows.OrderByDescending(x => x.Stock.StockNo, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(x => x.Stock.StockNo, StringComparer.OrdinalIgnoreCase),
            "market" => descending ? rows.OrderByDescending(x => x.Stock.Market) : rows.OrderBy(x => x.Stock.Market),
            "roe" => SortNullable(rows, x => x.RoeFor(referenceYear), descending),
            "profit" => SortNullable(rows, x => x.StatementFor(referenceYear)?.NetIncome, descending),
            "cash" => SortNullable(rows, x => x.DividendFor(referenceYear)?.CashPerShare, descending),
            "stockdividend" => SortNullable(rows, x => x.DividendFor(referenceYear)?.StockPerShare, descending),
            "payout" => SortNullable(rows, x => x.CashPayoutRatio(referenceYear), descending),
            "price" => SortNullable(rows, x => x.Stock.ClosePrice is > 0 ? x.Stock.ClosePrice : null, descending),
            "yield" => SortNullable(rows, x => x.CashYield(referenceYear), descending),
            "target" => SortNullable(rows, x => x.CashTargetPrice(referenceYear), descending),
            "coverage" => SortNullable(rows, x => x.EpsYearCount(years), descending),
            _ => SortNullable(rows, x => x.StatementFor(referenceYear)?.Eps, descending)
        };
        return ordered.ThenBy(x => x.Stock.StockNo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Money(decimal? value) => value is null ? "—" : $"{value.Value / 100000m:N1}";

    public static string MoneyWithUnit(decimal? value) => value is null ? "—" : $"{Money(value)} 億元";
    public static string Price(decimal? value) => value is null ? "—" : $"{value:N2}";
    public static string? QuoteSourceLink(Stock stock) => stock.QuoteSource.StartsWith("TWSE", StringComparison.Ordinal)
        ? "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL"
        : stock.QuoteSource.StartsWith("TPEx", StringComparison.Ordinal)
        ? "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes" : null;

    public static string? SourceLink(FinancialStatement statement) =>
        SourceLink(statement.SourceUrl);

    public static string? SourceLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri.AbsoluteUri : null;

    public static string Basis(FinancialStatement? statement) => statement is null ? "尚無年報" :
        statement.ReportBasis == "Consolidated" || statement.SourceUrl.Contains("REPORT_ID=C", StringComparison.OrdinalIgnoreCase)
            ? "合併報表" : statement.ReportBasis == "Individual" || statement.SourceUrl.Contains("REPORT_ID=A", StringComparison.OrdinalIgnoreCase)
            ? "個體報表" : "報表口徑未標示";

    private static IOrderedEnumerable<FinancialCompanyRow> SortNullable(
        IEnumerable<FinancialCompanyRow> rows, Func<FinancialCompanyRow, decimal?> value, bool descending)
    {
        var withMissingLast = rows.OrderBy(x => value(x) is null);
        return descending ? withMissingLast.ThenByDescending(value) : withMissingLast.ThenBy(value);
    }

    private static FinancialStatement ChooseSource(IEnumerable<FinancialStatement> statements) => statements
        .OrderByDescending(x => x.IsPreferred)
        .ThenByDescending(x => x.Eps is not null)
        .ThenByDescending(HasCoreSummary)
        .ThenBy(x => x.SourceKind switch
        {
            SourceKind.Mops => 0,
            SourceKind.FinMind => 1,
            SourceKind.Csv => 2,
            SourceKind.Twse or SourceKind.Tpex => 3,
            _ => 9
        })
        .ThenByDescending(x => x.FetchedAt)
        .ThenByDescending(x => x.Id)
        .First();
}

public sealed record FinancialCompanyRow(Stock Stock, IReadOnlyDictionary<int, FinancialStatement> AnnualStatements)
{
    public IReadOnlyDictionary<int, AnnualDividend> Dividends { get; init; } = new Dictionary<int, AnnualDividend>();
    public AnnualDividend? DividendFor(int year) => Dividends.GetValueOrDefault(year);
    public decimal? CashPayoutRatio(int year) => DividendFor(year)?.CashEarnings is decimal cash &&
        StatementFor(year)?.Eps is decimal eps && eps > 0 ? cash / eps * 100m : null;
    public DateOnly? QuoteTradingDate => DateOnly.TryParseExact(Stock.QuoteDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) ? date : null;
    public bool HasDatedQuote(DateOnly? today = null) => Stock.ClosePrice is > 0 && QuoteTradingDate is DateOnly date &&
        date <= (today ?? FinancePageData.Today) && !string.IsNullOrWhiteSpace(Stock.QuoteSource);
    public bool IsOldQuote(DateOnly? today = null) => QuoteTradingDate is DateOnly date && date < (today ?? FinancePageData.Today).AddDays(-7);
    public decimal? CashYield(int year, DateOnly? today = null) => HasDatedQuote(today)
        ? DividendPriceCalculator.YieldPercent(DividendFor(year)?.CashPerShare, Stock.ClosePrice) : null;
    public decimal? CashTargetPrice(int year) => DividendPriceCalculator.TargetPrice(DividendFor(year)?.CashPerShare);
    public decimal? DividendFaceValueTotal(int year) => DividendFor(year)?.CashPerShare + DividendFor(year)?.StockPerShare;
    public decimal? FaceValueYield(int year) => HasDatedQuote()
        ? DividendPriceCalculator.YieldPercent(DividendFaceValueTotal(year), Stock.ClosePrice) : null;
    public bool MeetsCashTarget(int year, DateOnly? today = null) => HasDatedQuote(today) && !IsOldQuote(today) &&
        CashTargetPrice(year) is decimal target && Stock.ClosePrice <= target;
    public FinancialStatement? LatestStatement => AnnualStatements.Values.OrderByDescending(x => x.FiscalYear).FirstOrDefault();

    public FinancialStatement? StatementFor(int year) =>
        AnnualStatements.TryGetValue(year, out var statement) ? statement : null;

    public decimal? RoeFor(int year) => StatementFor(year) is FinancialStatement statement
        ? FinancialCalculator.ValueFor(MetricKey.Roe, statement) : null;

    public int EpsYearCount(IEnumerable<int> years) => years.Count(year => StatementFor(year)?.Eps is not null);

    public decimal? EpsChange(int year) =>
        StatementFor(year)?.Eps is decimal current && StatementFor(year - 1)?.Eps is decimal previous
            ? current - previous : null;
}
