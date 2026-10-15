using RoceTool.Core;

namespace RoceTool.Infrastructure;

public static class FinanceIncomeUpdate
{
    public static bool TryUpdateDividend(AnnualDividend existing, AnnualDividend incoming)
    {
        if (existing.StockNo != incoming.StockNo || existing.FiscalYear != incoming.FiscalYear)
            throw new ArgumentException("不能合併不同公司或年度的股利。");
        var oldValues = new[] { existing.CashEarnings, existing.CashReserves, existing.StockEarnings, existing.StockReserves };
        var newValues = new[] { incoming.CashEarnings, incoming.CashReserves, incoming.StockEarnings, incoming.StockReserves };
        if (oldValues.Where((value, index) => value is not null && newValues[index] is null).Any()) return false;
        existing.CashEarnings = incoming.CashEarnings;
        existing.CashReserves = incoming.CashReserves;
        existing.StockEarnings = incoming.StockEarnings;
        existing.StockReserves = incoming.StockReserves;
        existing.DecisionStatus = incoming.DecisionStatus;
        existing.DecisionDate = incoming.DecisionDate;
        existing.SourceUrl = incoming.SourceUrl;
        existing.FetchedAt = incoming.FetchedAt;
        return true;
    }

    public static bool CanEnrichParentFields(FinancialStatement existing, FinancialStatement incoming)
    {
        static string Basis(FinancialStatement statement) => !string.IsNullOrEmpty(statement.ReportBasis) ? statement.ReportBasis :
            statement.SourceUrl.Contains("REPORT_ID=C", StringComparison.OrdinalIgnoreCase) ? "Consolidated" :
            statement.SourceUrl.Contains("REPORT_ID=A", StringComparison.OrdinalIgnoreCase) ? "Individual" : "";
        var basis = Basis(existing);
        return !string.IsNullOrEmpty(basis) && basis == Basis(incoming) &&
            existing.StockNo == incoming.StockNo && existing.FiscalYear == incoming.FiscalYear &&
            existing.Eps == incoming.Eps && existing.NetIncome == incoming.NetIncome && existing.TotalEquity == incoming.TotalEquity;
    }
}
