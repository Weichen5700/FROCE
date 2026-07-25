using System.ComponentModel.DataAnnotations;

namespace RoceTool.Core;

public enum MarketKind { Listed, Otc }
public enum StatementKind { General, Financial }
public enum SourceKind { Twse, Tpex, Mops, Csv }

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
    public decimal? CurrentAssets { get; set; }
    public decimal? TotalAssets { get; set; }
    public decimal? CurrentLiabilities { get; set; }
    public decimal? TotalLiabilities { get; set; }
    public decimal? TotalEquity { get; set; }
    public decimal? Eps { get; set; }
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
public sealed record PeerComparison(string IndustryName, int RoceRank, int SampleSize, decimal MedianRoce, decimal CapitalEmployed, CompanySizeBand SizeBand, int SizeRoceRank, int SizeSampleSize, decimal SizeMedianRoce);
public sealed record DailyClosingPrice(DateOnly Date, decimal Close);
public sealed record MovingAverage(int TradingDays, string Label, decimal? Value);

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
    public IReadOnlyList<FinancialStatement> AnnualHistory { get; init; } = [];
    public ScoreBreakdown? Score { get; init; }
    public ScoreBreakdown? LatestQuarterScore { get; init; }
    public PeerComparison? PeerComparison { get; init; }
}

public sealed record RefreshResult(bool Success, string Message, int AnnualRecords = 0);
public sealed record CsvImportPreview(IReadOnlyList<FinancialStatement> Rows, IReadOnlyList<string> Errors);
public sealed record StockScreenItem(Stock Stock, FinancialStatement Statement, decimal Roce, decimal Score, decimal CoveragePercent, int IndustryRoceRank, int IndustrySampleSize, decimal IndustryMedianRoce, decimal CapitalEmployed, CompanySizeBand SizeBand, int SizeRoceRank, int SizeSampleSize, decimal SizeMedianRoce);
public sealed record StockScreenResult(IReadOnlyList<StockScreenItem> Items, int AnalyzedStocks, int EligibleStocks, IReadOnlyList<int> AvailableYears, IReadOnlyList<string> AvailableIndustries);

public enum MarketScanStatus { Idle, Running, Paused, Completed }

public sealed class MarketScanState
{
    public int Id { get; set; } = 1;
    public int FiscalYear { get; set; }
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
    string? CurrentStockNo, DateTime? StartedAt, DateTime UpdatedAt)
{
    public decimal Percent => Total <= 0 ? 0m : Math.Min(100m, (decimal)Completed / Total * 100m);
}






