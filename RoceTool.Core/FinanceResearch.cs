namespace RoceTool.Core;

// Curated source observations are independent of the imported financial statements.
// A newer presentation may restate comparatives; retaining its version is essential.
public sealed class FinanceResearchDocument
{
    public int SchemaVersion { get; set; }
    public DateOnly VerifiedOn { get; set; }
    public List<FinanceResearchCompany> Companies { get; set; } = [];
}

public sealed class FinanceResearchCompany
{
    public string StockNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string BusinessSummary { get; set; } = "";
    public List<string> Businesses { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<FinanceResearchObservation> Observations { get; set; } = [];
    public List<FinanceEarningsBridge> Bridges { get; set; } = [];
    public IEnumerable<string> Periods => Observations.Select(x => x.Period).Distinct().OrderDescending();
    public int AnnualYears => Observations.Where(x => x.Period.EndsWith("FY", StringComparison.Ordinal))
        .Select(x => x.Period).Distinct().Count();
    public IEnumerable<FinanceResearchObservation> ForCategory(string category) =>
        Observations.Where(x => x.Category == category).OrderBy(x => x.Entity).ThenBy(x => x.Metric).ThenByDescending(x => x.AsOfDate);
}

public sealed class FinanceResearchObservation
{
    public string Category { get; set; } = "";
    public string Period { get; set; } = "";
    public string Metric { get; set; } = "";
    public decimal? Value { get; set; }
    public string Unit { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Basis { get; set; } = "";
    public string AccountingRegime { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Caveat { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourcePage { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public DateOnly? PublishedOn { get; set; }
    public DateOnly? AsOfDate { get; set; }
    public string DateBasis { get; set; } = "periodEnd";
}

public sealed class FinanceEarningsBridge
{
    public string Period { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal ReportedProfit { get; set; }
    public decimal Adjustment { get; set; }
    public string AdjustmentLabel { get; set; } = "";
    public decimal Result { get; set; }
    public string ResultLabel { get; set; } = "";
    public string Basis { get; set; } = "";
    public string AccountingRegime { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourcePage { get; set; } = "";
    public DateOnly? PublishedOn { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string Caveat { get; set; } = "";
    public bool Reconciles
    {
        get
        {
            try { return ReportedProfit + Adjustment == Result; }
            catch (OverflowException) { return false; }
        }
    }
}

public sealed record DividendScenario(decimal ChangePercent, decimal? CashPerShare,
    decimal? YieldPercent, decimal? TargetPrice, decimal? YieldAtHistoricalFivePercentPrice);

public static class FinanceResearchCalculator
{
    public const decimal MinimumTargetYield = 0.1m;
    public const decimal MaximumTargetYield = 30m;

    public static DividendScenario DividendScenario(decimal? baseCash, decimal? datedPrice,
        decimal changePercent, decimal targetYieldPercent)
    {
        if (changePercent is < -100m or > 100m) throw new ArgumentOutOfRangeException(nameof(changePercent));
        if (targetYieldPercent is < MinimumTargetYield or > MaximumTargetYield)
            throw new ArgumentOutOfRangeException(nameof(targetYieldPercent));
        var cash = baseCash is >= 0 ? baseCash * (1m + changePercent / 100m) : null;
        return new(changePercent, cash, DividendPriceCalculator.YieldPercent(cash, datedPrice),
            DividendPriceCalculator.TargetPrice(cash, targetYieldPercent),
            DividendPriceCalculator.YieldPercent(cash, DividendPriceCalculator.TargetPrice(baseCash, 5m)));
    }
}
