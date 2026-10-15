using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Web.Pages;

public sealed class FinanceModel(RoceDbContext db, FinanceQuoteRefreshService? quotes = null,
    FinanceResearchStore? research = null) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public string? Direction { get; set; }
    [BindProperty(SupportsGet = true)] public bool OnlyTarget { get; set; }

    public IReadOnlyList<FinancialCompanyRow> Items { get; private set; } = [];
    public int ReferenceYear { get; private set; }
    public IReadOnlyList<int> HistoryYears { get; private set; } = [];
    public int CompanyCount => Items.Count;
    public int ComparableEpsCount => Items.Count(x => x.StatementFor(ReferenceYear)?.Eps is not null);
    public int CompleteHistoryCount => Items.Count(x => x.EpsYearCount(HistoryYears) == HistoryYears.Count);
    public int TargetCount { get; private set; }
    public IReadOnlyCollection<FinanceResearchCompany> ResearchCompanies => research?.Companies.Values.ToArray() ?? [];
    public string? ResearchError => research?.Error;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Query = Query?.Trim();
        Sort = FinancePageData.NormalizeSort(Sort);
        Direction = FinancePageData.NormalizeDirection(Direction, Sort);
        ReferenceYear = await FinancePageData.ReferenceYearAsync(db, cancellationToken);
        HistoryYears = FinancePageData.HistoryYears(ReferenceYear);

        var stocksQuery = FinancePageData.Companies(db);
        if (!string.IsNullOrWhiteSpace(Query))
            stocksQuery = stocksQuery.Where(x => x.StockNo.Contains(Query) || x.Name.Contains(Query) || x.IndustryName.Contains(Query));

        var stocks = await stocksQuery.OrderBy(x => x.StockNo).ToListAsync(cancellationToken);
        var stockNos = stocks.Select(x => x.StockNo).ToArray();
        var statements = stockNos.Length == 0 ? new List<FinancialStatement>()
            : await db.FinancialStatements.AsNoTracking()
                .Where(x => x.IsAnnual && stockNos.Contains(x.StockNo)).ToListAsync(cancellationToken);
        var byStock = statements.ToLookup(x => x.StockNo, StringComparer.OrdinalIgnoreCase);
        var dividends = await db.AnnualDividends.AsNoTracking().Where(x => stockNos.Contains(x.StockNo)).ToListAsync(cancellationToken);
        var byDividend = dividends.ToLookup(x => x.StockNo, StringComparer.OrdinalIgnoreCase);
        var rows = stocks.Select(stock => FinancePageData.CompanyRow(stock, byStock[stock.StockNo], byDividend[stock.StockNo])).ToList();
        TargetCount = rows.Count(x => x.MeetsCashTarget(ReferenceYear));
        Items = FinancePageData.SortRows(rows.Where(x => !OnlyTarget || x.MeetsCashTarget(ReferenceYear)), Sort, Direction, ReferenceYear);
    }

    public string NextDirection(string column) => Sort == column
        ? Direction == "asc" ? "desc" : "asc"
        : FinancePageData.DefaultDirection(column);

    public string AriaSort(string column) => Sort != column ? "none" : Direction == "asc" ? "ascending" : "descending";

    public string SortIndicator(string column) => Sort != column ? "↕" : Direction == "asc" ? "↑" : "↓";

    public async Task<IActionResult> OnPostQuotesAsync(CancellationToken cancellationToken)
    {
        if (quotes is null) throw new InvalidOperationException("股價資料服務未設定。");
        try
        {
            var result = await quotes.RefreshAsync(cancellationToken: cancellationToken);
            TempData[result.Updated > 0 ? "Message" : "Error"] = $"已更新 {result.Updated} 家金融公司官方收盤資料；" +
                $"{result.Retained} 家未取得較新的有效資料，保留原紀錄。交易日期以各公司標示為準。";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        { TempData["Error"] = "官方股價查詢暫時失敗，原資料已保留。"; }
        return RedirectToPage(new { query = Query, sort = Sort, direction = Direction, onlyTarget = OnlyTarget });
    }
}
