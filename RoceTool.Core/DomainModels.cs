using System.ComponentModel.DataAnnotations;

namespace RoceTool.Core;

public enum MarketKind { Listed, Otc }
public enum StatementKind { General, Financial }
public enum SourceKind { Twse, Tpex, Mops, Csv, FinMind }

public sealed class Stock
{
    [Key, MaxLength(12)] public string StockNo { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";
    public MarketKind Market { get; set; }
    [MaxLength(20)] public string IndustryCode { get; set; } = "";
    [MaxLength(80)] public string IndustryName { get; set; } = "";
    public StatementKind StatementKind { get; set; }
    public bool IsSupported { get; set; } = true;
    public decimal? PeRatio { get; set; }
    public decimal? PbRatio { get; set; }
    public decimal? DividendYield { get; set; }
    public DateTime? ValuationFetchedAt { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? PriceChange { get; set; }
    public decimal? PriceChangePercent { get; set; }
    public decimal? DayHigh { get; set; }
    public decimal? DayLow { get; set; }
    public long? TradeVolume { get; set; }
    [MaxLength(20)] public string QuoteDate { get; set; } = "";
    [MaxLength(80)] public string QuoteSource { get; set; } = "";
    public DateTime? QuoteFetchedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class WatchlistItem
{
    [Key, MaxLength(12)] public string StockNo { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public int SortOrder { get; set; }
    public Stock? Stock { get; set; }
}

public sealed class FinancialStatement
{
    public int Id { get; set; }
    [MaxLength(12)] public string StockNo { get; set; } = "";
    public int FiscalYear { get; set; }
    [MaxLength(12)] public string Period { get; set; } = "FY";
    public bool IsAnnual { get; set; }
    public decimal? OperatingRevenue { get; set; }
    public decimal? OperatingIncome { get; set; }
    public decimal? InterestIncome { get; set; }
    public decimal? NetIncome { get; set; }
    public decimal? ParentNetIncome { get; set; }
    public decimal? ParentEquity { get; set; }
    [MaxLength(20)] public string ReportBasis { get; set; } = "";
    public decimal? CurrentAssets { get; set; }
    public decimal? TotalAssets { get; set; }
    public decimal? CurrentLiabilities { get; set; }
    public decimal? TotalLiabilities { get; set; }
    public decimal? TotalEquity { get; set; }
    public decimal? Eps { get; set; }
    public decimal? OperatingCashFlow { get; set; }
    public decimal? CapexPpe { get; set; }
    public decimal? CapexIntangibles { get; set; }
    public decimal? WeightedAverageShares { get; set; }
    [MaxLength(80)] public string WeightedAverageSharesSource { get; set; } = "";
    public SourceKind SourceKind { get; set; }
    [MaxLength(80)] public string SourceName { get; set; } = "";
    [MaxLength(500)] public string SourceUrl { get; set; } = "";
    public bool IsPreferred { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DataError
{
    public int Id { get; set; }
    [MaxLength(12)] public string? StockNo { get; set; }
    [MaxLength(80)] public string Provider { get; set; } = "";
    [MaxLength(1000)] public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StockDailyPriceCache
{
    [Key, MaxLength(12)] public string StockNo { get; set; } = "";
    public string PricesJson { get; set; } = "[]";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StockAnnualPriceCache
{
    public int Id { get; set; }
    [MaxLength(12)] public string StockNo { get; set; } = "";
    public int FiscalYear { get; set; }
    public decimal? Close { get; set; }
    [MaxLength(20)] public string? TradingDate { get; set; }
    [MaxLength(60)] public string Source { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public enum MetricKey { Roce, OperatingMargin, Roe, DebtRatio, CurrentRatio }
public enum CompanySizeBand { Small, Medium, Large }

public static class CompanySizeBandLabels
{
    public static string Label(CompanySizeBand band) => band switch
    {
        CompanySizeBand.Small => "產業內小型",
        CompanySizeBand.Medium => "產業內中型",
        CompanySizeBand.Large => "產業內大型",
        _ => band.ToString()
    };
}

public sealed class ScoreTemplate
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "品質／資本效率";
    public bool IsDefault { get; set; }
    public List<ScoreRule> Rules { get; set; } = [];
}

public sealed class ScoreRule
{
    public int Id { get; set; }
    public int ScoreTemplateId { get; set; }
    public MetricKey MetricKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public decimal Weight { get; set; }
    [MaxLength(500)] public string PointsJson { get; set; } = "[]";
    public ScoreTemplate? ScoreTemplate { get; set; }
}

public sealed record ThresholdPoint(decimal Value, decimal Score);
public sealed record MetricResult(MetricKey Key, string Label, decimal? Value, decimal? Score, decimal Weight);
public sealed record ScoreBreakdown(decimal? TotalScore, decimal CoveragePercent, IReadOnlyList<MetricResult> Metrics);
public sealed record RoceResult(bool IsSuccess, decimal? CapitalEmployed, decimal? RocePercent, string? ErrorMessage);
public sealed record FreeCashFlowResult(
    decimal? OperatingCashFlow,
    decimal? CapitalExpenditure,
    decimal? CapexPpe,
    decimal? CapexIntangibles,
    decimal? FreeCashFlow,
    decimal? WeightedAverageShares,
    string? WeightedAverageSharesSource,
    decimal? FreeCashFlowPerShare);
public sealed record PeerComparison(string IndustryName, int RoceRank, int SampleSize, decimal MedianRoce, decimal CapitalEmployed, CompanySizeBand SizeBand, int SizeRoceRank, int SizeSampleSize, decimal SizeMedianRoce);
public sealed record DailyClosingPrice(DateOnly Date, decimal Close);
public sealed record AnnualPriceReference(int FiscalYear, DateOnly? TradingDate, decimal? Close, string Source);
public sealed record MovingAverage(int TradingDays, string Label, decimal? Value);
public sealed record QuarterlyEpsObservation(int FiscalYear, int Quarter, decimal? YtdEps, decimal? StandaloneEps)
{
    public string Label => $"{FiscalYear} Q{Quarter}";
}

public sealed record TrailingFourQuarterEps(IReadOnlyList<QuarterlyEpsObservation> Quarters)
{
    public bool IsComplete
    {
        get
        {
            if (Quarters.Count != 4 || Quarters.Any(x => x.Quarter is < 1 or > 4 || x.StandaloneEps is null))
                return false;

            var chronological = Quarters.OrderBy(x => x.FiscalYear).ThenBy(x => x.Quarter).ToList();
            for (var index = 1; index < chronological.Count; index++)
            {
                var previous = chronological[index - 1];
                var current = chronological[index];
                var quarterDistance = (current.FiscalYear - previous.FiscalYear) * 4 + current.Quarter - previous.Quarter;
                if (quarterDistance != 1) return false;
            }

            return true;
        }
    }
    public decimal? Value => IsComplete ? Quarters.Sum(x => x.StandaloneEps!.Value) : null;
}

public static class QuarterlyEpsCalculator
{
    public static IReadOnlyList<QuarterlyEpsObservation> Calculate(IEnumerable<FinancialStatement> statements)
    {
        var byPeriod = statements
            .GroupBy(x => (x.FiscalYear, x.Period))
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.IsPreferred).ThenByDescending(v => v.FetchedAt).First());
        var result = new List<QuarterlyEpsObservation>();

        foreach (var year in byPeriod.Keys.Select(x => x.FiscalYear).Distinct().OrderByDescending(x => x))
        {
            byPeriod.TryGetValue((year, "Q1YTD"), out var q1);
            byPeriod.TryGetValue((year, "Q2YTD"), out var q2);
            byPeriod.TryGetValue((year, "Q3YTD"), out var q3);
            byPeriod.TryGetValue((year, "FY"), out var fy);

            if (q1?.Eps is decimal q1Eps)
                result.Add(new QuarterlyEpsObservation(year, 1, q1Eps, q1Eps));
            if (q2?.Eps is decimal q2Eps && q1?.Eps is decimal previousQ1Eps)
                result.Add(new QuarterlyEpsObservation(year, 2, q2Eps, q2Eps - previousQ1Eps));
            if (q3?.Eps is decimal q3Eps && q2?.Eps is decimal previousQ2Eps)
                result.Add(new QuarterlyEpsObservation(year, 3, q3Eps, q3Eps - previousQ2Eps));
            if (fy?.Eps is decimal fyEps && q3?.Eps is decimal previousQ3Eps)
                result.Add(new QuarterlyEpsObservation(year, 4, fyEps, fyEps - previousQ3Eps));
        }

        return result
            .OrderByDescending(x => x.FiscalYear)
            .ThenByDescending(x => x.Quarter)
            .ToList();
    }

    public static TrailingFourQuarterEps CalculateTrailingFour(IEnumerable<FinancialStatement> statements) =>
        new(Calculate(statements).Take(4).ToList());
}

public static class AnnualPriceReferenceCalculator
{
    public static AnnualPriceReference Select(int fiscalYear, IEnumerable<DailyClosingPrice> prices, string source)
    {
        var latest = prices
            .Where(x => x.Date.Year == fiscalYear)
            .OrderByDescending(x => x.Date)
            .FirstOrDefault();

        return latest is null
            ? new AnnualPriceReference(fiscalYear, null, null, source)
            : new AnnualPriceReference(fiscalYear, latest.Date, latest.Close, source);
    }
}

public static class MovingAverageCalculator
{
    private static readonly (int TradingDays, string Label)[] DefaultPeriods =
    [
        (5, "5 日均線"),
        (10, "10 日均線"),
        (20, "20 日線（月線）"),
        (60, "60 日線（季線）"),
        (120, "120 日線（半年線）"),
        (240, "240 日線（年線）")
    ];

    public static IReadOnlyList<MovingAverage> Calculate(IEnumerable<DailyClosingPrice> prices)
    {
        var ordered = prices
            .GroupBy(x => x.Date)
            .Select(x => x.Last())
            .OrderByDescending(x => x.Date)
            .ToList();

        return DefaultPeriods.Select(period => new MovingAverage(
            period.TradingDays,
            period.Label,
            ordered.Count < period.TradingDays ? null : ordered.Take(period.TradingDays).Average(x => x.Close)))
            .ToList();
    }
}

public sealed class StockAnalysis
{
    public required Stock Stock { get; init; }
    public FinancialStatement? SelectedAnnual { get; init; }
    public FinancialStatement? LatestQuarter { get; init; }
    public IReadOnlyList<FinancialStatement> QuarterHistory { get; init; } = [];
    public IReadOnlyList<FinancialStatement> AnnualHistory { get; init; } = [];
    public ScoreBreakdown? Score { get; init; }
    public ScoreBreakdown? LatestQuarterScore { get; init; }
    public PeerComparison? PeerComparison { get; init; }
}

public sealed record RefreshResult(bool Success, string Message, int AnnualRecords = 0);
public sealed record CsvImportPreview(IReadOnlyList<FinancialStatement> Rows, IReadOnlyList<string> Errors);
public sealed record StockScreenItem(Stock Stock, FinancialStatement Statement, decimal Roce, decimal Score, decimal CoveragePercent, int IndustryRoceRank, int IndustrySampleSize, decimal IndustryMedianRoce, decimal CapitalEmployed, CompanySizeBand SizeBand, int SizeRoceRank, int SizeSampleSize, decimal SizeMedianRoce);
public sealed record StockScreenResult(IReadOnlyList<StockScreenItem> Items, int AnalyzedStocks, int EligibleStocks, IReadOnlyList<int> AvailableYears, IReadOnlyList<string> AvailableIndustries)
{
    public int? SelectedYear { get; init; }
}

public sealed class ExternalTarget
{
    public string StockNo { get; set; } = "";
    public string Source { get; set; } = ""; // 來源（例：財經週刊、PTT、Telegram）
    public string Note { get; set; } = ""; // 筆記
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ExternalTargetSettings
{
    public List<ExternalTarget> Targets { get; set; } = new();
    public decimal MinRoce { get; set; } = 20m;
    public decimal? MinScore { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
}

public sealed record ExternalTargetVerificationItem(
    Stock Stock,
    FinancialStatement? LatestAnnual,
    FinancialStatement? LatestQuarter,
    decimal? AnnualRoce,
    decimal? QuarterRoce,
    decimal? Score,
    decimal? CoveragePercent,
    bool MeetsRoceCriteria,
    bool MeetsScoreCriteria,
    string Source,
    string Note);

public sealed record ExternalTargetVerificationResult(
    IReadOnlyList<ExternalTargetVerificationItem> Items,
    int TotalTargets,
    int VerifiedCount,
    int PassedCount,
    DateTime VerifiedAt);

// 保留舊的 Discovery 模型以免破壞現有功能
public sealed class DiscoverySettings
{
    public int Years { get; set; } = 2;
    public int LatestYear { get; set; } = DateTime.Today.Year - 1;
    public decimal MinRoce { get; set; } = 20m;
    public decimal? MinScore { get; set; }
    public DateTime? LastScanStartedAt { get; set; }
    public int? LastDiscoveredCount { get; set; }
}

public sealed record DiscoveryItem(
    Stock Stock,
    FinancialStatement LatestStatement,
    decimal LatestRoce,
    decimal? Score,
    decimal CoveragePercent,
    IReadOnlyList<(int Year, decimal? Roce)> YearlyRoce,
    int YearsWithData,
    int RequestedYears);

public sealed record DiscoveryResult(
    IReadOnlyList<DiscoveryItem> Items,
    int TotalEligibleStocks,
    int DiscoveredCount,
    int LatestYear,
    int RequestedYears);

public enum MarketScanStatus { Idle, Running, Paused, Completed }

public sealed class MarketScanState
{
    public int Id { get; set; } = 1;
    public int FiscalYear { get; set; }
    public int? RangeStartFiscalYear { get; set; }
    public int? RangeEndFiscalYear { get; set; }
    public MarketScanStatus Status { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    [MaxLength(12)] public string? CurrentStockNo { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MarketScanItem
{
    public int Id { get; set; }
    [MaxLength(12)] public string StockNo { get; set; } = "";
    public int FiscalYear { get; set; }
    public bool Success { get; set; }
    [MaxLength(1000)] public string? Message { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}

public sealed record MarketScanProgress(
    MarketScanStatus Status, int FiscalYear, int Total, int Completed, int Success, int Failed,
    string? CurrentStockNo, DateTime? StartedAt, DateTime UpdatedAt,
    int? RangeStartFiscalYear = null, int? RangeEndFiscalYear = null)
{
    public decimal Percent => Total <= 0 ? 0m : Math.Min(100m, (decimal)Completed / Total * 100m);
    public int ScanYears => RangeStartFiscalYear is int start && RangeEndFiscalYear is int end && end >= start
        ? end - start + 1 : 1;
}






