using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages.Stocks;

public sealed class DetailsModel(IStockAnalysisService service, IOfficialDataProvider officialData) : PageModel
{
    [BindProperty(SupportsGet = true)] public string StockNo { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public bool MarketView { get; set; }
    [BindProperty(SupportsGet = true)] public string? FinanceQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? FinanceSort { get; set; }
    [BindProperty(SupportsGet = true)] public string? FinanceDirection { get; set; }
    [BindProperty(SupportsGet = true)] public bool FinanceOnlyTarget { get; set; }
    [BindProperty(SupportsGet = true)] public int? FinanceScenarioYear { get; set; }
    [BindProperty(SupportsGet = true)] public decimal FinanceScenarioChange { get; set; }
    [BindProperty(SupportsGet = true)] public decimal FinanceTargetYield { get; set; } = 5m;
    public StockAnalysis Analysis { get; private set; } = null!;
    public IReadOnlyList<MovingAverage> MovingAverages { get; private set; } = [];
    public IReadOnlyDictionary<int, AnnualPriceReference> AnnualPriceReferences { get; private set; } =
        new Dictionary<int, AnnualPriceReference>();
    public bool NeedsFinancialRefresh { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await service.EnsureCatalogAsync(cancellationToken: cancellationToken);
        var analysis = await service.GetAnalysisAsync(StockNo, Year, cancellationToken);
        if (analysis is null) return NotFound();
        if (!MarketView && analysis.Stock.StatementKind == StatementKind.Financial &&
            analysis.Stock.IsSupported && analysis.Stock.IndustryCode == "17")
            return RedirectToPage("/Finance/Details", new { stockNo = StockNo, year = Year });
        var trailingFourEps = analysis.LatestQuarter is not null
            ? QuarterlyEpsCalculator.CalculateTrailingFour(analysis.QuarterHistory.Concat(analysis.AnnualHistory))
            : null;
        NeedsFinancialRefresh = analysis.Stock.StatementKind == StatementKind.General &&
            (analysis.AnnualHistory.Count == 0 || analysis.SelectedAnnual?.InterestIncome is null ||
             analysis.SelectedAnnual?.OperatingCashFlow is null || analysis.SelectedAnnual?.CapexPpe is null ||
             analysis.LatestQuarter?.InterestIncome is null || (analysis.LatestQuarter is not null && trailingFourEps?.IsComplete != true));
        Analysis = analysis;
        MovingAverages = MovingAverageCalculator.Calculate([]);
        return Page();
    }

    public async Task<IActionResult> OnPostRefreshAsync(CancellationToken cancellationToken)
    {
        var result = await service.RefreshStockAsync(StockNo, true, cancellationToken);
        var message = result.Message;
        if (result.Success)
        {
            var analysis = await service.GetAnalysisAsync(StockNo, Year, cancellationToken);
            if (analysis is not null)
            {
                var dailyPrices = await officialData.FetchDailyClosingPricesAsync(analysis.Stock.StockNo, analysis.Stock.Market,
                    force: true, cancellationToken: cancellationToken);
                message += dailyPrices.Count > 0 ? " 技術均線已更新。" : " 但暫時未取得歷史日線。";

                var annualPrices = await officialData.FetchAnnualClosingPricesAsync(
                    analysis.Stock.StockNo,
                    analysis.Stock.Market,
                    analysis.AnnualHistory.Select(x => x.FiscalYear),
                    force: true,
                    cancellationToken: cancellationToken);
                message += annualPrices.Any(x => x.Close is not null)
                    ? " 年度末收盤價參考已更新。"
                    : " 但暫時未取得年度收盤價參考。";
            }
        }
        TempData[result.Success ? "Message" : "Error"] = message;
        return RedirectToPage(new { stockNo = StockNo, year = Year, marketView = MarketView, financeQuery = FinanceQuery,
            financeSort = FinanceSort, financeDirection = FinanceDirection, financeOnlyTarget = FinanceOnlyTarget,
            financeScenarioYear = FinanceScenarioYear, financeScenarioChange = FinanceScenarioChange, financeTargetYield = FinanceTargetYield });
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        await service.AddWatchlistAsync(StockNo, cancellationToken);
        TempData["Message"] = $"{StockNo} 已加入觀察清單。";
        return RedirectToPage(new { stockNo = StockNo, year = Year, marketView = MarketView, financeQuery = FinanceQuery,
            financeSort = FinanceSort, financeDirection = FinanceDirection, financeOnlyTarget = FinanceOnlyTarget,
            financeScenarioYear = FinanceScenarioYear, financeScenarioChange = FinanceScenarioChange, financeTargetYield = FinanceTargetYield });
    }

    public static string Money(decimal? value) => value is null ? "—" : $"{value.Value / 100000m:N1} 億";
    public static string Price(decimal? value) => value is null ? "—" : $"{value.Value:N2} 元";
    public static string PerShare(decimal? value) => value is null ? "—" : $"{value.Value:N2} 元";
    public static string Shares(decimal? value) => value is null ? "—" : $"{value.Value / 1000000m:N1} 百萬股";
    public static string Signed(decimal? value) => value is null ? "—" : value.Value.ToString("+0.00;-0.00;0.00");
    public static string Volume(long? value) => value is null ? "—" : $"{value.Value:N0} 股";
    public static string AnnualPriceTooltip(AnnualPriceReference? reference, decimal? eps) =>
        reference?.Close is decimal close && reference.TradingDate is DateOnly date
            ? eps is decimal epsValue && epsValue != 0
                ? $"年度末收盤價：{close:N2} 元（交易日 {date:yyyy-MM-dd}）\nEPS：{epsValue:N2} 元\n本益比：{close / epsValue:N2} 倍"
                : $"年度末收盤價：{close:N2} 元（交易日 {date:yyyy-MM-dd}）\nEPS：{(eps is null ? "無資料" : "0.00 元")}\n本益比：無法計算"
            : "年度股價參考：尚無官方收盤資料";
    public static decimal? Median(IEnumerable<decimal?> values)
    {
        var ordered = values.Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToList();
        if (ordered.Count == 0) return null;
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2m;
    }
}
