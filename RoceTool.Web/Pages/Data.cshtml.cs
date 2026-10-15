using System.Globalization;
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
    [BindProperty(SupportsGet = true)] public int? FiscalYear { get; set; }
    public CsvImportPreview? Preview { get; private set; }
    public IReadOnlyList<DataError> Errors { get; private set; } = [];
    public IReadOnlyList<int> FinancialYears { get; private set; } = [];
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
        const string sample = "StockNo,FiscalYear,Period,Unit,OperatingRevenue,OperatingIncome,InterestIncome,NetIncome,CurrentAssets,TotalAssets,CurrentLiabilities,TotalLiabilities,TotalEquity,Eps,OperatingCashFlow,CapexPpe,CapexIntangibles,WeightedAverageShares,WeightedAverageSharesSource,SourceName\n2330,2025,FY,ThousandTWD,3809054272,1936091677,105739081,1715396780,3817130817,7933023878,1458019289,2472228595,5460795283,66.15,2754537827,-1212174401,-28753470,25930380458,基本股數,手動匯入\n";
        return File(new UTF8Encoding(true).GetBytes(sample), "text/csv", "roce-import-sample.csv");
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        if (FiscalYear is not int fiscalYear)
        {
            TempData["Error"] = "請先選擇要匯出的財報年度。";
            return RedirectToPage();
        }

        var rows = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.FiscalYear == fiscalYear)
            .OrderBy(x => x.StockNo)
            .ThenBy(x => x.Period)
            .ThenBy(x => x.SourceName)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            TempData["Error"] = $"{fiscalYear} 年沒有可匯出的財報資料。";
            return RedirectToPage();
        }

        var csv = new StringBuilder();
        AppendCsvRecord(csv,
        [
            "StockNo", "FiscalYear", "Period", "Unit", "OperatingRevenue", "OperatingIncome", "InterestIncome",
            "NetIncome", "CurrentAssets", "TotalAssets", "CurrentLiabilities", "TotalLiabilities", "TotalEquity",
            "Eps", "OperatingCashFlow", "CapexPpe", "CapexIntangibles", "WeightedAverageShares",
            "WeightedAverageSharesSource", "SourceName"
        ]);
        foreach (var row in rows)
        {
            AppendCsvRecord(csv,
            [
                row.StockNo,
                row.FiscalYear.ToString(CultureInfo.InvariantCulture),
                row.Period,
                "ThousandTWD",
                Format(row.OperatingRevenue),
                Format(row.OperatingIncome),
                Format(row.InterestIncome),
                Format(row.NetIncome),
                Format(row.CurrentAssets),
                Format(row.TotalAssets),
                Format(row.CurrentLiabilities),
                Format(row.TotalLiabilities),
                Format(row.TotalEquity),
                Format(row.Eps),
                Format(row.OperatingCashFlow),
                Format(row.CapexPpe),
                Format(row.CapexIntangibles),
                Format(row.WeightedAverageShares),
                SafeSpreadsheetText(row.WeightedAverageSharesSource),
                SafeSpreadsheetText(row.SourceName)
            ]);
        }

        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv", $"roce-financial-statements-{fiscalYear}.csv");
    }

    private async Task LoadSummaryAsync(CancellationToken cancellationToken)
    {
        StockCount = await db.Stocks.CountAsync(cancellationToken);
        StatementCount = await db.FinancialStatements.CountAsync(cancellationToken);
        FinancialYears = await db.FinancialStatements.AsNoTracking()
            .Select(x => x.FiscalYear)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(cancellationToken);
        if (FinancialYears.Count > 0 && (FiscalYear is null || !FinancialYears.Contains(FiscalYear.Value)))
            FiscalYear = FinancialYears[0];
        Errors = await db.DataErrors.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(cancellationToken);
    }

    private static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? SafeSpreadsheetText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var firstNonWhitespace = value.TrimStart().FirstOrDefault();
        return firstNonWhitespace is '=' or '+' or '-' or '@' ? $"'{value}" : value;
    }

    private static void AppendCsvRecord(StringBuilder csv, IEnumerable<string?> fields)
    {
        csv.AppendLine(string.Join(",", fields.Select(EscapeCsvField)));
    }

    private static string EscapeCsvField(string? value)
    {
        value ??= "";
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
