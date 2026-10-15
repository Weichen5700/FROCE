using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

var dbPath = Path.GetFullPath("RoceTool.Web/App_Data/roce.db");
var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite($"Data Source={dbPath}").Options;
using var processLock = new FileStream(Path.Combine(Path.GetDirectoryName(dbPath)!, "finance-income.lock"),
    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
using (var source = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
using (var backup = new SqliteConnection($"Data Source={dbPath}.income-backup-{DateTime.Now:yyyyMMdd-HHmmss}"))
{
    source.Open(); backup.Open(); source.BackupDatabase(backup);
    using var check = backup.CreateCommand(); check.CommandText = "PRAGMA quick_check";
    if (check.ExecuteScalar()?.ToString() != "ok") throw new InvalidDataException("SQLite 備份檢查失敗。");
    Console.WriteLine("已建立股利回填前線上備份。");
}
await using var db = new RoceDbContext(options);
await DatabaseInitializer.InitializeAsync(db);
var stocks = await db.Stocks.AsNoTracking().Where(x => x.IsSupported && x.IndustryCode == "17" &&
    x.StatementKind == StatementKind.Financial).OrderBy(x => x.StockNo).ToListAsync();
var selectedStock = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
if (selectedStock is not null) stocks = stocks.Where(x => x.StockNo == selectedStock).ToList();
if (stocks.Count == 0) throw new InvalidOperationException("指定公司不在金融產業代碼 17 名單。");
var parentMode = args.Contains("--parent");
var quotesMode = args.Contains("--quotes");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("RoceTool/1.0 (+local research tool)");
var provider = new MopsDividendProvider(http);
var results = new List<string>();
foreach (var stock in stocks.Where(_ => !parentMode && !quotesMode))
{
    IReadOnlyList<AnnualDividend>? incoming = null;
    Exception? failure = null;
    for (var attempt = 0; attempt < 2 && incoming is null; attempt++)
    {
        try { incoming = await provider.FetchAsync(stock.StockNo, 2019, 2025); }
        catch (Exception ex) { failure = ex; if (attempt == 0) await Task.Delay(2000); }
    }
    if (incoming is null) { results.Add($"{stock.StockNo} 失敗：{failure?.Message}"); continue; }
    foreach (var item in incoming)
    {
        var existing = await db.AnnualDividends.SingleOrDefaultAsync(x => x.StockNo == item.StockNo && x.FiscalYear == item.FiscalYear);
        if (existing is null) db.AnnualDividends.Add(item);
        else if (!FinanceIncomeUpdate.TryUpdateDividend(existing, item))
            results.Add($"{stock.StockNo} {item.FiscalYear} 新股利回應缺分項，保留既有資料。");
    }
    await db.SaveChangesAsync();
    var line = $"{stock.StockNo} {stock.Name} 股利取得 {incoming.Count}/7 年";
    results.Add(line); Console.WriteLine(line);
    await Task.Delay(500);
}
if (quotesMode)
{
    var quoteService = new FinanceQuoteRefreshService(db, new OfficialDataProvider(http, db));
    var result = await quoteService.RefreshAsync(selectedStock);
    results.Add($"官方收盤更新 {result.Updated} 家，未取得有效較新資料保留 {result.Retained} 家。");
    Console.WriteLine(results[^1]);
}
if (parentMode)
{
    http.Timeout = TimeSpan.FromSeconds(65);
    var reports = new MopsAnnualReportProvider(http);
    foreach (var stock in stocks)
    {
        var existingReports = await db.FinancialStatements.Where(x => x.StockNo == stock.StockNo && x.IsAnnual &&
            x.FiscalYear >= 2019 && x.FiscalYear <= 2025 && x.SourceName == "MOPS iXBRL" && x.ParentNetIncome == null)
            .OrderByDescending(x => x.FiscalYear).ToListAsync();
        foreach (var existing in existingReports)
        {
            try
            {
                var report = await reports.FetchAnnualAsync(stock.StockNo, existing.FiscalYear);
                if (report is null) { results.Add($"{stock.StockNo} {existing.FiscalYear} 歸母欄位未取得"); continue; }
                if (!FinanceIncomeUpdate.CanEnrichParentFields(existing, report))
                {
                    results.Add($"{stock.StockNo} {existing.FiscalYear} 新舊報表口徑或核心數值不同，未混入歸母欄位。");
                    continue;
                }
                existing.ParentNetIncome = report.ParentNetIncome;
                existing.ParentEquity = report.ParentEquity;
                existing.ReportBasis = report.ReportBasis;
                existing.FetchedAt = report.FetchedAt;
                await db.SaveChangesAsync();
                var line = $"{stock.StockNo} {existing.FiscalYear} 歸母淨利={report.ParentNetIncome}，原 EPS、來源與偏好保留";
                results.Add(line); Console.WriteLine(line);
            }
            catch (Exception ex) { results.Add($"{stock.StockNo} {existing.FiscalYear} 歸母更新失敗：{ex.Message}"); }
        }
    }
}
var incomeFile = "samples/finance-earnings-sources.json";
if (!quotesMode && File.Exists(incomeFile))
{
    var incomes = JsonSerializer.Deserialize<List<FinancialSubsidiaryIncome>>(await File.ReadAllTextAsync(incomeFile)) ?? [];
    foreach (var item in incomes.Where(x => stocks.Any(stock => stock.StockNo == x.StockNo)))
    {
        if (item.FiscalYear is < 2019 or > 2025 || string.IsNullOrWhiteSpace(item.EntityName) ||
            string.IsNullOrWhiteSpace(item.Basis) || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https")
            throw new InvalidDataException("子公司獲利資料缺少有效年度、口徑或來源。");
        var existing = await db.FinancialSubsidiaryIncomes.SingleOrDefaultAsync(x => x.StockNo == item.StockNo &&
            x.FiscalYear == item.FiscalYear && x.EntityName == item.EntityName && x.SourceUrl == item.SourceUrl);
        item.FetchedAt = DateTime.UtcNow;
        if (existing is null) db.FinancialSubsidiaryIncomes.Add(item);
        else { item.Id = existing.Id; db.Entry(existing).CurrentValues.SetValues(item); }
    }
    await db.SaveChangesAsync();
    results.Add($"已匯入公司官方獲利組成 {incomes.Count} 筆候選，僅寫入本次金融公司範圍。");
}
await File.WriteAllLinesAsync(quotesMode ? "RoceTool.Web/App_Data/finance-quotes.log" : parentMode ? $"RoceTool.Web/App_Data/finance-parent-{selectedStock ?? "all"}.log"
    : "RoceTool.Web/App_Data/finance-income-backfill.log", results);
Console.WriteLine("股利／獲利組成回填完成；來源、擬議狀態及缺漏均保留。");
