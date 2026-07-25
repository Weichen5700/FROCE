using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Web.Pages;

public sealed class DataModel(
    IStockAnalysisService analysisService,
    ICsvImportService csvService,
    RoceDbContext db) : PageModel
{
    [BindProperty] public IFormFile? CsvFile { get; set; }
    [BindProperty] public bool Preferred { get; set; }
    public CsvImportPreview? Preview { get; private set; }
    public IReadOnlyList<DataError> Errors { get; private set; } = [];
    public int StockCount { get; private set; }
    public int StatementCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadSummaryAsync(cancellationToken);

    public async Task<IActionResult> OnPostUpdateOfficialAsync(CancellationToken cancellationToken)
    {
        try
        {
            await analysisService.EnsureCatalogAsync(true, cancellationToken);
            TempData["Message"] = "上市、上櫃公司清單、最新季財報、日行情與估值已更新。";
        }
        catch (Exception ex) { TempData["Error"] = $"官方資料更新失敗：{ex.Message}"; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUploadAsync(string mode, CancellationToken cancellationToken)
    {
        if (CsvFile is null || CsvFile.Length == 0)
        {
            TempData["Error"] = "請選擇 CSV 檔案。";
            return RedirectToPage();
        }
        await analysisService.EnsureCatalogAsync(cancellationToken: cancellationToken);
        await using var stream = CsvFile.OpenReadStream();
        Preview = await csvService.PreviewAsync(stream, cancellationToken);
        if (mode == "import" && Preview.Errors.Count == 0)
        {
            var count = await csvService.ImportAsync(Preview.Rows, Preferred, cancellationToken);
            TempData["Message"] = $"已匯入 {count} 筆財報資料。";
            return RedirectToPage();
        }
        await LoadSummaryAsync(cancellationToken);
        return Page();
    }

    public FileContentResult OnGetSample()
    {
        const string sample = "StockNo,FiscalYear,Period,Unit,OperatingRevenue,OperatingIncome,InterestIncome,NetIncome,CurrentAssets,TotalAssets,CurrentLiabilities,TotalLiabilities,TotalEquity,SourceName\n2330,2025,FY,ThousandTWD,3809054272,1936091677,105739081,1715396780,3817130817,7933023878,1458019289,2472228595,5460795283,手動匯入\n";
        return File(new UTF8Encoding(true).GetBytes(sample), "text/csv", "roce-import-sample.csv");
    }

    private async Task LoadSummaryAsync(CancellationToken cancellationToken)
    {
        StockCount = await db.Stocks.CountAsync(cancellationToken);
        StatementCount = await db.FinancialStatements.CountAsync(cancellationToken);
        Errors = await db.DataErrors.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(cancellationToken);
    }
}
