using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages.Stocks;

public sealed class DetailsModel(IStockAnalysisService service, IOfficialDataProvider officialData) : PageModel
{
    [BindProperty(SupportsGet = true)] public string StockNo { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    public StockAnalysis Analysis { get; private set; } = null!;
    public IReadOnlyList<MovingAverage> MovingAverages { get; private set; } = [];
    public int AvailableDailyPrices { get; private set; }
    public string? MovingAverageError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await service.EnsureCatalogAsync(cancellationToken: cancellationToken);
        var analysis = await service.GetAnalysisAsync(StockNo, Year, cancellationToken);
        if (analysis is null) return NotFound();
        if (analysis.Stock.StatementKind == StatementKind.General &&
            (analysis.AnnualHistory.Count == 0 || analysis.SelectedAnnual?.InterestIncome is null || analysis.LatestQuarter?.InterestIncome is null))
        {
            var refresh = await service.RefreshStockAsync(StockNo, false, cancellationToken);
            TempData[refresh.Success ? "Message" : "Error"] = refresh.Message;
            analysis = await service.GetAnalysisAsync(StockNo, Year, cancellationToken) ?? analysis;
        }
        Analysis = analysis;
        try
        {
            var dailyPrices = await officialData.FetchDailyClosingPricesAsync(analysis.Stock.StockNo, analysis.Stock.Market,
                cancellationToken: cancellationToken);
            AvailableDailyPrices = dailyPrices.Count;
            MovingAverages = MovingAverageCalculator.Calculate(dailyPrices);
        }
        catch (HttpRequestException)
        {
            MovingAverageError = "官方歷史日線暫時無法取得，請稍後再試。";
        }
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
            }
        }
        TempData[result.Success ? "Message" : "Error"] = message;
        return RedirectToPage(new { stockNo = StockNo, year = Year });
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        await service.AddWatchlistAsync(StockNo, cancellationToken);
        TempData["Message"] = $"{StockNo} 已加入觀察清單。";
        return RedirectToPage(new { stockNo = StockNo, year = Year });
    }

    public static string Money(decimal? value) => value is null ? "—" : $"{value.Value / 100000m:N1} 億";
    public static string Price(decimal? value) => value is null ? "—" : $"{value.Value:N2} 元";
    public static string Signed(decimal? value) => value is null ? "—" : value.Value.ToString("+0.00;-0.00;0.00");
    public static string Volume(long? value) => value is null ? "—" : $"{value.Value:N0} 股";
}
