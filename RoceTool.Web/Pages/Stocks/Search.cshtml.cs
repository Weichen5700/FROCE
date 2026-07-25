using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Web.Pages.Stocks;

public sealed class SearchModel(IStockAnalysisService service, RoceDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    public IReadOnlyList<Stock> Results { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int ListedCount { get; private set; }
    public int OtcCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Results = await service.SearchAsync(Query, cancellationToken);
        TotalCount = await db.Stocks.CountAsync(cancellationToken);
        ListedCount = await db.Stocks.CountAsync(x => x.Market == MarketKind.Listed, cancellationToken);
        OtcCount = TotalCount - ListedCount;
    }

    public async Task<IActionResult> OnPostAddAsync(string stockNo, CancellationToken cancellationToken)
    {
        try
        {
            await service.AddWatchlistAsync(stockNo, cancellationToken);
            var result = await service.RefreshStockAsync(stockNo, false, cancellationToken);
            TempData[result.Success ? "Message" : "Error"] = result.Message;
            return RedirectToPage("/Stocks/Details", new { stockNo });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { query = Query });
        }
    }
}
