using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages;

public sealed class IndexModel(IStockAnalysisService service) : PageModel
{
    public IReadOnlyList<StockAnalysis> Items { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "score";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var items = await service.GetWatchlistAsync(cancellationToken);
        Items = Sort switch
        {
            "stock" => items.OrderBy(x => x.Stock.StockNo).ToList(),
            "roce" => items.OrderByDescending(x => x.Score?.Metrics.FirstOrDefault(m => m.Key == MetricKey.Roce)?.Value).ToList(),
            "eps" => items.OrderByDescending(x => x.SelectedAnnual?.Eps).ToList(),
            "pe" => items.OrderBy(x => x.Stock.PeRatio ?? decimal.MaxValue).ToList(),
            "roe" => items.OrderByDescending(x => x.SelectedAnnual is null ? null : FinancialCalculator.ValueFor(MetricKey.Roe, x.SelectedAnnual)).ToList(),
            _ => items.OrderByDescending(x => x.Score?.TotalScore).ToList()
        };
    }

    public async Task<IActionResult> OnPostRemoveAsync(string stockNo, CancellationToken cancellationToken)
    {
        await service.RemoveWatchlistAsync(stockNo, cancellationToken);
        TempData["Message"] = $"已將 {stockNo} 從觀察清單移除。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshAsync(string stockNo, CancellationToken cancellationToken)
    {
        var result = await service.RefreshStockAsync(stockNo, true, cancellationToken);
        TempData[result.Success ? "Message" : "Error"] = result.Message;
        return RedirectToPage("/Stocks/Details", new { stockNo });
    }
}
