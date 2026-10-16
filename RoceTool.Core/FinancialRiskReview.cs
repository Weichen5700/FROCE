namespace RoceTool.Core;

public enum RiskSignalStatus { Review, NotTriggered, InsufficientData }

public sealed record FinancialRiskSource(int FiscalYear, string Name, string? Url);

public sealed record FinancialRiskSignal(
    string Key,
    string Title,
    RiskSignalStatus Status,
    string Evidence,
    IReadOnlyList<FinancialRiskSource> Sources,
    string Limitation);

public static class FinancialRiskReview
{
    public static IReadOnlyList<FinancialRiskSignal> Assess(IEnumerable<FinancialStatement> statements)
    {
        // Callers pass the source-resolved annual history from StockAnalysisService.
        // Duplicate years are left visible as a gap rather than selecting a different source here.
        var annuals = statements.Where(x => x.IsAnnual)
            .OrderByDescending(x => x.FiscalYear)
            .Take(3)
            .ToArray();

        var periodsComplete = annuals.Length == 3 &&
            annuals[0].FiscalYear == annuals[1].FiscalYear + 1 &&
            annuals[1].FiscalYear == annuals[2].FiscalYear + 1;
        var basisComparable = periodsComplete && annuals.All(x => !string.IsNullOrWhiteSpace(x.ReportBasis)) &&
            annuals.Select(x => x.ReportBasis.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

        var sources = annuals.Select(x => new FinancialRiskSource(x.FiscalYear, x.SourceName,
            Uri.TryCreate(x.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
                ? uri.AbsoluteUri : null)).ToArray();
        return [AssessCashConversion(annuals, basisComparable, sources), AssessOperatingTrend(annuals, basisComparable, sources)];
    }

    private static FinancialRiskSignal AssessCashConversion(FinancialStatement[] annuals, bool periodsComplete,
        IReadOnlyList<FinancialRiskSource> sources)
    {
        const string limitation = "這是現金流與盈餘差異的初步核對，不代表舞弊判定；需查看營運資金變化、一次性項目與原始公告。";
        if (!periodsComplete || annuals.Any(x => x.NetIncome is null || x.OperatingCashFlow is null))
            return new("cash-conversion", "營業現金流與淨利", RiskSignalStatus.InsufficientData,
                "需要三個連續完整年度、相同財報口徑，且每年同時有淨利與營業現金流。", sources, limitation);

        decimal netIncome;
        decimal operatingCashFlow;
        try
        {
            netIncome = annuals.Sum(x => x.NetIncome!.Value);
            operatingCashFlow = annuals.Sum(x => x.OperatingCashFlow!.Value);
        }
        catch (OverflowException)
        {
            return new("cash-conversion", "營業現金流與淨利", RiskSignalStatus.InsufficientData,
                "數值超出可計算範圍，請核對來源資料。", sources, limitation);
        }
        var evidence = $"{annuals[2].FiscalYear}–{annuals[0].FiscalYear}：淨利合計 {netIncome:N0}，營業現金流合計 {operatingCashFlow:N0}（資料庫金額單位：新臺幣千元）。";
        if (netIncome <= 0)
            return new("cash-conversion", "營業現金流與淨利", RiskSignalStatus.InsufficientData,
                $"{evidence}淨利合計非正，這項差異規則不適用。", sources, limitation);

        return new("cash-conversion", "營業現金流與淨利",
            operatingCashFlow < netIncome ? RiskSignalStatus.Review : RiskSignalStatus.NotTriggered,
            evidence + (operatingCashFlow < netIncome ? "累計現金流低於累計淨利，建議核對差異來源。" : "此項差異提示未觸發。"), sources,
            limitation);
    }

    private static FinancialRiskSignal AssessOperatingTrend(FinancialStatement[] annuals, bool periodsComplete,
        IReadOnlyList<FinancialRiskSource> sources)
    {
        const string limitation = "連續下滑只代表營運趨勢需要了解，不等同結構性衰退；本提示不檢查產業、產品、景氣或公司說明。";
        if (!periodsComplete || annuals.Any(x => x.OperatingRevenue is null || x.OperatingIncome is null))
            return new("operating-trend", "營收與營業利益趨勢", RiskSignalStatus.InsufficientData,
                "需要三個連續完整年度、相同財報口徑，且每年同時有營收與營業利益。", sources, limitation);

        var newest = annuals[0];
        var prior = annuals[1];
        var oldest = annuals[2];
        var revenueDecline = newest.OperatingRevenue < prior.OperatingRevenue && prior.OperatingRevenue < oldest.OperatingRevenue;
        var incomeDecline = newest.OperatingIncome < prior.OperatingIncome && prior.OperatingIncome < oldest.OperatingIncome;
        var evidence = $"營收：{oldest.FiscalYear} {oldest.OperatingRevenue:N0} → {prior.FiscalYear} {prior.OperatingRevenue:N0} → {newest.FiscalYear} {newest.OperatingRevenue:N0}；營業利益：{oldest.OperatingIncome:N0} → {prior.OperatingIncome:N0} → {newest.OperatingIncome:N0}（新臺幣千元）。";
        var triggered = revenueDecline || incomeDecline;
        var reasons = new List<string>();
        if (revenueDecline) reasons.Add("營收連續兩年下降");
        if (incomeDecline) reasons.Add("營業利益連續兩年下降");
        return new("operating-trend", "營收與營業利益趨勢",
            triggered ? RiskSignalStatus.Review : RiskSignalStatus.NotTriggered,
            evidence + (triggered ? string.Join("；", reasons) + "，建議查看公司說明與產業變化。" : "未觸發連續兩年下滑提示。"), sources,
            limitation);
    }
}
