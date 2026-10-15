using System.ComponentModel.DataAnnotations;

namespace RoceTool.Core;

public sealed class AnnualDividend
{
    public int Id { get; set; }
    [MaxLength(12)] public string StockNo { get; set; } = "";
    public int FiscalYear { get; set; }
    public decimal? CashEarnings { get; set; }
    public decimal? CashReserves { get; set; }
    public decimal? StockEarnings { get; set; }
    public decimal? StockReserves { get; set; }
    public decimal? CashPerShare => CashEarnings + CashReserves;
    public decimal? StockPerShare => StockEarnings + StockReserves;
    [MaxLength(200)] public string DecisionStatus { get; set; } = "";
    [MaxLength(40)] public string DecisionDate { get; set; } = "";
    [MaxLength(500)] public string SourceUrl { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

// Amounts are thousand TWD; entity earnings are not additive to holding company earnings.
public sealed class FinancialSubsidiaryIncome
{
    public int Id { get; set; }
    [MaxLength(12)] public string StockNo { get; set; } = "";
    public int FiscalYear { get; set; }
    [MaxLength(120)] public string EntityName { get; set; } = "";
    [MaxLength(40)] public string Business { get; set; } = "";
    public decimal NetIncome { get; set; }
    [MaxLength(200)] public string Basis { get; set; } = "";
    [MaxLength(80)] public string SourceName { get; set; } = "";
    [MaxLength(500)] public string SourceUrl { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public static class DividendPriceCalculator
{
    public static decimal? YieldPercent(decimal? dividendPerShare, decimal? sharePrice) =>
        dividendPerShare is >= 0 && sharePrice is > 0 ? dividendPerShare / sharePrice * 100m : null;

    public static decimal? TargetPrice(decimal? dividendPerShare, decimal targetYieldPercent = 5m) =>
        dividendPerShare is > 0 && targetYieldPercent > 0 ? dividendPerShare * 100m / targetYieldPercent : null;
}
