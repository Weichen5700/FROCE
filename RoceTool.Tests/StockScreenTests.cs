using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Tests;

public sealed class StockScreenTests
{
    [Fact]
    public async Task FindsOnlyStocksMeetingRoceScoreAndCoverageThresholds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);

        db.Stocks.AddRange(
            new Stock { StockNo = "2330", Name = "台積電", IndustryCode = "24", IndustryName = "半導體業", StatementKind = StatementKind.General, UpdatedAt = DateTime.UtcNow },
            new Stock { StockNo = "6488", Name = "環球晶", IndustryCode = "24", IndustryName = "半導體業", StatementKind = StatementKind.General, Market = MarketKind.Otc, UpdatedAt = DateTime.UtcNow });
        db.FinancialStatements.AddRange(
            Annual("2330", 250, 1000, 200, 1000, 200, 500, 400, 600),
            Annual("6488", 80, 1000, 400, 1000, 50, 500, 600, 400));
        await db.SaveChangesAsync();

        var service = new StockAnalysisService(db, new UnusedOfficialProvider(), new UnusedAnnualProvider());
        var result = await service.ScreenAsync(20, 80, 100, industryName: "半導體業", sizeBand: CompanySizeBand.Medium);

        Assert.Equal(2, result.EligibleStocks);
        Assert.Equal(2, result.AnalyzedStocks);
        var match = Assert.Single(result.Items);
        Assert.Equal("2330", match.Stock.StockNo);
        Assert.True(match.Roce >= 20);
        Assert.True(match.Score >= 80);
        Assert.Equal(100, match.CoveragePercent);
        Assert.Equal(1, match.IndustryRoceRank);
        Assert.Equal(2, match.IndustrySampleSize);
        Assert.Equal(800, match.CapitalEmployed);
        Assert.Equal(CompanySizeBand.Medium, match.SizeBand);
        Assert.Equal(1, match.SizeRoceRank);
        Assert.Equal(1, match.SizeSampleSize);
        Assert.Contains("半導體業", result.AvailableIndustries);

        var analysis = await service.GetAnalysisAsync("2330");
        Assert.NotNull(analysis?.PeerComparison);
        Assert.Equal("半導體業", analysis.PeerComparison.IndustryName);
        Assert.Equal(1, analysis.PeerComparison.RoceRank);
        Assert.Equal(2, analysis.PeerComparison.SampleSize);
        Assert.Equal(800, analysis.PeerComparison.CapitalEmployed);
        Assert.Equal(CompanySizeBand.Medium, analysis.PeerComparison.SizeBand);
        Assert.Equal(1, analysis.PeerComparison.SizeRoceRank);
    }

    [Fact]
    public async Task MarketScanCanStartResumeFromExistingReportsAndPause()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        db.Stocks.AddRange(
            new Stock { StockNo = "2330", Name = "台積電", IndustryCode = "24", IndustryName = "半導體業", StatementKind = StatementKind.General, UpdatedAt = DateTime.UtcNow },
            new Stock { StockNo = "6488", Name = "環球晶", StatementKind = StatementKind.General, UpdatedAt = DateTime.UtcNow });
        var existing = Annual("2330", 250, 1000, 200, 1000, 200, 500, 400, 600);
        existing.SourceKind = SourceKind.Mops;
        existing.SourceName = "MOPS iXBRL";
        db.FinancialStatements.Add(existing);
        await db.SaveChangesAsync();

        var scanner = new MarketScanService(db);
        var started = await scanner.StartAsync(2025);
        Assert.Equal(MarketScanStatus.Running, started.Status);
        Assert.Equal(2, started.Total);
        Assert.Equal(1, started.Completed);
        Assert.Equal(1, started.Success);

        var paused = await scanner.PauseAsync();
        Assert.Equal(MarketScanStatus.Paused, paused.Status);
        Assert.Equal(1, paused.Completed);
    }

    [Fact]
    public async Task RangeScanRetriesFailuresAndCountsOnlyCashFlowCompleteReports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        db.Stocks.AddRange(
            new Stock { StockNo = "2330", Name = "台積電", IndustryCode = "24", IndustryName = "半導體業", StatementKind = StatementKind.General, UpdatedAt = DateTime.UtcNow },
            new Stock { StockNo = "6488", Name = "環球晶", StatementKind = StatementKind.General, UpdatedAt = DateTime.UtcNow });
        var complete = Annual("2330", 250, 1000, 200, 1000, 200, 500, 400, 600);
        complete.SourceKind = SourceKind.Mops;
        complete.SourceName = "MOPS iXBRL";
        var incomplete = Annual("6488", 80, 1000, 400, 1000, 50, 500, 600, 400);
        incomplete.SourceKind = SourceKind.Mops;
        incomplete.SourceName = "MOPS iXBRL";
        incomplete.OperatingCashFlow = null;
        db.FinancialStatements.AddRange(complete, incomplete);
        db.MarketScanItems.Add(new MarketScanItem
        {
            StockNo = "6488",
            FiscalYear = 2024,
            Success = false,
            Message = "old failure"
        });
        await db.SaveChangesAsync();

        var scanner = new MarketScanService(db);
        var started = await scanner.StartRangeAsync(2025, 2, retryFailed: true);

        Assert.Equal(MarketScanStatus.Running, started.Status);
        Assert.Equal(2024, started.RangeStartFiscalYear);
        Assert.Equal(2025, started.RangeEndFiscalYear);
        Assert.Equal(4, started.Total);
        Assert.Equal(1, started.Completed);
        Assert.Equal(1, started.Success);
        Assert.Equal(0, started.Failed);
        Assert.Equal(2025, started.FiscalYear);
        Assert.Empty(await db.MarketScanItems.Where(x => !x.Success).ToListAsync());
    }

    [Fact]
    public async Task MarketScanCountsCompleteFinMindBackfillRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        db.Stocks.Add(new Stock
        {
            StockNo = "9958",
            Name = "世紀鋼",
            IndustryCode = "20",
            IndustryName = "鋼鐵工業",
            StatementKind = StatementKind.General,
            UpdatedAt = DateTime.UtcNow
        });
        var complete = Annual("9958", 250, 1000, 200, 1000, 200, 500, 400, 600);
        complete.SourceKind = SourceKind.FinMind;
        complete.SourceName = "FinMind 合併補充";
        db.FinancialStatements.Add(complete);
        await db.SaveChangesAsync();

        var scanner = new MarketScanService(db);
        var started = await scanner.StartAsync(2025);

        Assert.Equal(MarketScanStatus.Completed, started.Status);
        Assert.Equal(1, started.Total);
        Assert.Equal(1, started.Completed);
        Assert.Equal(1, started.Success);
    }

    [Fact]
    public async Task MarketScanCountsAttemptedIncompleteSuccessRowsAsFailed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        db.Stocks.Add(new Stock
        {
            StockNo = "1409",
            Name = "新纖",
            IndustryCode = "14",
            IndustryName = "紡織纖維",
            StatementKind = StatementKind.General,
            UpdatedAt = DateTime.UtcNow
        });
        var incomplete = Annual("1409", 250, 1000, 200, 1000, 200, 500, 400, 600);
        incomplete.SourceKind = SourceKind.Mops;
        incomplete.SourceName = "MOPS iXBRL";
        incomplete.OperatingIncome = null;
        db.FinancialStatements.Add(incomplete);
        db.MarketScanItems.Add(new MarketScanItem
        {
            StockNo = "1409",
            FiscalYear = 2025,
            Success = true,
            Message = "old success"
        });
        await db.SaveChangesAsync();

        var scanner = new MarketScanService(db);
        var started = await scanner.StartAsync(2025);

        Assert.Equal(MarketScanStatus.Completed, started.Status);
        Assert.Equal(1, started.Total);
        Assert.Equal(1, started.Completed);
        Assert.Equal(0, started.Success);
        Assert.Equal(1, started.Failed);
    }

    [Fact]
    public async Task AnalysisPrefersCompleteFinMindRowWhenMopsLacksCashFlow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        db.Stocks.Add(new Stock
        {
            StockNo = "9958",
            Name = "世紀鋼",
            IndustryCode = "20",
            IndustryName = "鋼鐵工業",
            StatementKind = StatementKind.General,
            UpdatedAt = DateTime.UtcNow
        });
        var mops = Annual("9958", 250, 1000, 200, 1000, 200, 500, 400, 600);
        mops.SourceKind = SourceKind.Mops;
        mops.SourceName = "MOPS iXBRL";
        mops.OperatingCashFlow = null;
        mops.CapexPpe = null;
        var finMind = Annual("9958", 250, 1000, 200, 1000, 200, 500, 400, 600);
        finMind.SourceKind = SourceKind.FinMind;
        finMind.SourceName = "FinMind 合併補充";
        db.FinancialStatements.AddRange(mops, finMind);
        await db.SaveChangesAsync();

        var service = new StockAnalysisService(db, new UnusedOfficialProvider(), new UnusedAnnualProvider());
        var analysis = await service.GetAnalysisAsync("9958");

        Assert.NotNull(analysis);
        Assert.Equal(SourceKind.FinMind, analysis.SelectedAnnual?.SourceKind);
        Assert.Equal(500, analysis.SelectedAnnual?.OperatingCashFlow);
        Assert.Equal(-100, analysis.SelectedAnnual?.CapexPpe);
    }

    private static FinancialStatement Annual(string stockNo, decimal opIncome, decimal assets, decimal currentLiabilities,
        decimal revenue, decimal netIncome, decimal currentAssets, decimal liabilities, decimal equity) => new()
    {
        StockNo = stockNo, FiscalYear = 2025, Period = "FY", IsAnnual = true,
        OperatingIncome = opIncome, TotalAssets = assets, CurrentLiabilities = currentLiabilities,
        OperatingRevenue = revenue, NetIncome = netIncome, CurrentAssets = currentAssets,
        TotalLiabilities = liabilities, TotalEquity = equity, OperatingCashFlow = 500, CapexPpe = -100,
        SourceKind = SourceKind.Csv, SourceName = "test"
    };

    private sealed class UnusedOfficialProvider : IOfficialDataProvider
    {
        public Task<IReadOnlyList<Stock>> FetchStockCatalogAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<FinancialStatement>> FetchLatestStatementsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task ApplyValuationsAsync(IDictionary<string, Stock> stocks, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<DailyClosingPrice>> FetchDailyClosingPricesAsync(string stockNo, MarketKind market,
            bool force = false, int minimumTradingDays = 240, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<AnnualPriceReference>> FetchAnnualClosingPricesAsync(string stockNo, MarketKind market,
            IEnumerable<int> fiscalYears, bool force = false, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<DateTime?> GetDailyPriceCachedAtAsync(string stockNo, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class UnusedAnnualProvider : IFinancialDataProvider
    {
        public Task<FinancialStatement?> FetchAnnualAsync(string stockNo, int fiscalYear, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<FinancialStatement?> FetchQuarterAsync(string stockNo, int fiscalYear, int quarter, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}




