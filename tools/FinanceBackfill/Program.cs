using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoceTool.Core;
using RoceTool.Infrastructure;

var options = Options.Parse(args);
var databasePath = Path.GetFullPath(options.DatabasePath);
if (!File.Exists(databasePath)) throw new FileNotFoundException("找不到財報資料庫", databasePath);

var appDataDirectory = Path.GetDirectoryName(databasePath)!;
var logPath = Path.Combine(appDataDirectory, "finance-backfill.log");
using var processLock = AcquireLock(Path.Combine(appDataDirectory, "finance-backfill.lock"));

var services = new ServiceCollection();
services.AddRoceInfrastructure($"Data Source={databasePath};Pooling=False");
await using var serviceProvider = services.BuildServiceProvider();
await using var scope = serviceProvider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<RoceDbContext>();
var provider = scope.ServiceProvider.GetRequiredService<IFinancialDataProvider>();
// Backup precedes schema additions as well as report writes.
if (!options.DryRun)
{
    WriteLog($"寫入前備份：{CreateBackup(databasePath, appDataDirectory)}");
    await DatabaseInitializer.InitializeAsync(db);
}

var scanState = await db.MarketScanStates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
if (scanState?.Status == MarketScanStatus.Running)
    throw new InvalidOperationException("一般股全市場掃描仍在執行；請等它暫停或完成後再回填金融股。");

var stocksQuery = db.Stocks.AsNoTracking()
    .Where(x => x.StatementKind == StatementKind.Financial && x.IsSupported && x.IndustryCode == "17");
if (options.StockNo is not null)
    stocksQuery = stocksQuery.Where(x => x.StockNo == options.StockNo);
var stocks = await stocksQuery.OrderBy(x => x.StockNo).ToListAsync();
if (stocks.Count == 0) throw new InvalidOperationException("指定股票不在支援中的官方產業代碼 17 金融名單。");

var firstYear = options.FiscalYear ?? options.LatestYear - options.Years + 1;
var lastYear = options.FiscalYear ?? options.LatestYear;
var years = Enumerable.Range(firstYear, lastYear - firstYear + 1).OrderByDescending(x => x).ToArray();
var stockNos = stocks.Select(x => x.StockNo).ToArray();
var existing = await db.FinancialStatements.AsNoTracking()
    .Where(x => x.IsAnnual && x.FiscalYear >= firstYear && x.FiscalYear <= lastYear && stockNos.Contains(x.StockNo))
    .ToListAsync();
var pending = (
    from stock in stocks
    from year in years
    let hasCoreAnnual = existing.Any(x => x.StockNo == stock.StockNo && x.FiscalYear == year && HasCoreFields(x))
    where !hasCoreAnnual
    select (stock.StockNo, stock.Name, Year: year)
).ToList();

var coreCovered = existing.Where(HasCoreFields)
    .Select(x => (x.StockNo, x.FiscalYear)).Distinct().Count();
WriteLog($"金融股年報回填開始；股票={stocks.Count}，年度={firstYear}-{lastYear}，已有核心完整年度={coreCovered}，待抓={pending.Count}，dry-run={options.DryRun}。");

if (options.DryRun && (options.StockNo is null || options.FiscalYear is null))
    throw new ArgumentException("dry-run 必須同時指定 --stock 與 --year，避免大量唯讀請求。");
if (pending.Count == 0)
{
    WriteLog("所選範圍沒有缺少核心欄位的年度財報。");
    return;
}

if (options.DryRun) pending = pending.Take(1).ToList();

var fetched = 0;
var inserted = 0;
var updated = 0;
var noReport = 0;
var incomplete = 0;
var failed = 0;

for (var offset = 0; offset < pending.Count; offset += options.BatchSize)
{
    var batch = pending.Skip(offset).Take(options.BatchSize).ToList();
    var results = await Task.WhenAll(batch.Select(async target =>
    {
        try
        {
            return new FetchResult(target, await provider.FetchAnnualAsync(target.StockNo, target.Year), null);
        }
        catch (Exception ex)
        {
            return new FetchResult(target, null, ex);
        }
    }));

    for (var resultIndex = 0; resultIndex < results.Length; resultIndex++)
    {
        var result = results[resultIndex];
        var target = result.Target;
        if (result.Error is not null)
        {
            failed++;
            WriteLog($"失敗 {target.StockNo} {target.Year}: {result.Error.GetType().Name}: {result.Error.Message}");
            continue;
        }

        var statement = result.Statement;
        if (statement is null)
        {
            noReport++;
            WriteLog($"無法解析年報 {target.StockNo} {target.Year}");
            continue;
        }

        fetched++;
        var coreComplete = HasCoreFields(statement);
        if (!coreComplete) incomplete++;
        var fieldCount = CountPresentFields(statement);
        if (options.DryRun)
        {
            WriteLog($"dry-run {target.StockNo} {target.Year}: 核心欄位={(coreComplete ? "完整" : "缺漏")}，已解析欄位={fieldCount}/14；NetIncome={Show(statement.NetIncome)}，TotalEquity={Show(statement.TotalEquity)}，EPS={Show(statement.Eps)}，TotalAssets={Show(statement.TotalAssets)}，InterestIncome={Show(statement.InterestIncome)}。");
            continue;
        }

        var isNewRow = false;
        try
        {
            var existingMops = await db.FinancialStatements.FirstOrDefaultAsync(x =>
                x.StockNo == statement.StockNo && x.FiscalYear == statement.FiscalYear &&
                x.Period == statement.Period && x.SourceName == statement.SourceName);
            if (existingMops is null)
            {
                db.FinancialStatements.Add(statement);
                isNewRow = true;
            }
            else
            {
                MergeNonNull(existingMops, statement);
            }

            await db.SaveChangesAsync();
            if (isNewRow) inserted++;
            else updated++;
            WriteLog($"{offset + resultIndex + 1}/{pending.Count} {target.StockNo} {target.Year}: {(coreComplete ? "核心完整" : "核心仍缺欄位")}，解析欄位={fieldCount}/14。");
        }
        catch (Exception ex)
        {
            failed++;
            WriteLog($"寫入失敗 {target.StockNo} {target.Year}: {ex.GetType().Name}: {ex.Message}");
            db.ChangeTracker.Clear();
        }
    }

    if (!options.DryRun && offset + batch.Count < pending.Count)
        await Task.Delay(TimeSpan.FromMilliseconds(options.DelayMilliseconds));
}

WriteLog($"金融股年報回填結束；試抓={fetched}，新增={inserted}，更新={updated}，無年報/無法解析={noReport}，核心欄位仍缺={incomplete}，例外={failed}。記錄：{logPath}");

void WriteLog(string message)
{
    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}

static FileStream AcquireLock(string path)
{
    try
    {
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException ex)
    {
        throw new InvalidOperationException("已有另一個金融股年報回填程序正在執行。", ex);
    }
}

static string CreateBackup(string databasePath, string directory)
{
    var backupPath = Path.Combine(directory, $"roce-finance-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db");
    using var source = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
    using var destination = new SqliteConnection($"Data Source={backupPath};Pooling=False");
    source.Open();
    destination.Open();
    source.BackupDatabase(destination);
    using var check = destination.CreateCommand();
    check.CommandText = "PRAGMA quick_check;";
    if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"SQLite 備份檢查失敗：{backupPath}");
    return backupPath;
}

static bool HasCoreFields(FinancialStatement x) =>
    x.IsAnnual && x.NetIncome is not null && x.TotalEquity is not null &&
    x.Eps is not null && x.TotalAssets is not null;

static int CountPresentFields(FinancialStatement x) => new decimal?[]
{
    x.OperatingRevenue, x.OperatingIncome, x.InterestIncome, x.NetIncome,
    x.CurrentAssets, x.TotalAssets, x.CurrentLiabilities, x.TotalLiabilities,
    x.TotalEquity, x.Eps, x.OperatingCashFlow, x.CapexPpe,
    x.CapexIntangibles, x.WeightedAverageShares
}.Count(value => value is not null);

static string Show(decimal? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";

static void MergeNonNull(FinancialStatement existing, FinancialStatement incoming)
{
    existing.OperatingRevenue = incoming.OperatingRevenue ?? existing.OperatingRevenue;
    existing.OperatingIncome = incoming.OperatingIncome ?? existing.OperatingIncome;
    existing.InterestIncome = incoming.InterestIncome ?? existing.InterestIncome;
    existing.NetIncome = incoming.NetIncome ?? existing.NetIncome;
    existing.ParentNetIncome = incoming.ParentNetIncome ?? existing.ParentNetIncome;
    existing.ParentEquity = incoming.ParentEquity ?? existing.ParentEquity;
    if (!string.IsNullOrEmpty(incoming.ReportBasis)) existing.ReportBasis = incoming.ReportBasis;
    existing.CurrentAssets = incoming.CurrentAssets ?? existing.CurrentAssets;
    existing.TotalAssets = incoming.TotalAssets ?? existing.TotalAssets;
    existing.CurrentLiabilities = incoming.CurrentLiabilities ?? existing.CurrentLiabilities;
    existing.TotalLiabilities = incoming.TotalLiabilities ?? existing.TotalLiabilities;
    existing.TotalEquity = incoming.TotalEquity ?? existing.TotalEquity;
    existing.Eps = incoming.Eps ?? existing.Eps;
    existing.OperatingCashFlow = incoming.OperatingCashFlow ?? existing.OperatingCashFlow;
    existing.CapexPpe = incoming.CapexPpe ?? existing.CapexPpe;
    existing.CapexIntangibles = incoming.CapexIntangibles ?? existing.CapexIntangibles;
    existing.WeightedAverageShares = incoming.WeightedAverageShares ?? existing.WeightedAverageShares;
    if (!string.IsNullOrWhiteSpace(incoming.WeightedAverageSharesSource))
        existing.WeightedAverageSharesSource = incoming.WeightedAverageSharesSource;
    existing.SourceUrl = incoming.SourceUrl;
    existing.FetchedAt = incoming.FetchedAt;
}

sealed record FetchResult(
    (string StockNo, string Name, int Year) Target,
    FinancialStatement? Statement,
    Exception? Error);

sealed record Options(
    string DatabasePath,
    string? StockNo,
    int? FiscalYear,
    int LatestYear,
    int Years,
    int BatchSize,
    int DelayMilliseconds,
    bool DryRun)
{
    public static Options Parse(string[] args)
    {
        string? database = null;
        string? stock = null;
        int? year = null;
        var latestYear = DateTime.Today.Year - 1;
        var years = 7;
        var batchSize = 3;
        var delayMilliseconds = 1000;
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db" when i + 1 < args.Length: database = args[++i]; break;
                case "--stock" when i + 1 < args.Length: stock = args[++i]; break;
                case "--year" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedYear): year = parsedYear; break;
                case "--latest-year" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedLatestYear): latestYear = parsedLatestYear; break;
                case "--years" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedYears): years = parsedYears; break;
                case "--batch-size" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedBatchSize): batchSize = parsedBatchSize; break;
                case "--delay-ms" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedDelay): delayMilliseconds = parsedDelay; break;
                case "--dry-run": dryRun = true; break;
                case "--help":
                    Console.WriteLine("FinanceBackfill [--latest-year 2025 --years 7] [--stock 2882 --year 2025 --dry-run] [--batch-size 3 --delay-ms 1000] [--db PATH]");
                    Environment.Exit(0);
                    break;
                default: throw new ArgumentException($"不支援的參數或缺少值：{args[i]}");
            }
        }

        if (years is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(years), "--years 範圍為 1–20。");
        if (batchSize is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(batchSize), "--batch-size 範圍為 1–5。");
        if (delayMilliseconds is < 250 or > 10000) throw new ArgumentOutOfRangeException(nameof(delayMilliseconds), "--delay-ms 範圍為 250–10000。");
        if (year is < 1990 or > 2200 || latestYear is < 1990 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(year), "年度範圍無效。");
        if (stock is not null && stock.Length > 12) throw new ArgumentException("股票代號長度不可超過 12。");
        return new Options(
            database ?? Path.Combine("RoceTool.Web", "App_Data", "roce.db"),
            stock,
            year,
            latestYear,
            years,
            batchSize,
            delayMilliseconds,
            dryRun);
    }
}
