using Microsoft.EntityFrameworkCore;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class RoceDbContext(DbContextOptions<RoceDbContext> options) : DbContext(options)
{
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<WatchlistItem> Watchlist => Set<WatchlistItem>();
    public DbSet<FinancialStatement> FinancialStatements => Set<FinancialStatement>();
    public DbSet<AnnualDividend> AnnualDividends => Set<AnnualDividend>();
    public DbSet<FinancialSubsidiaryIncome> FinancialSubsidiaryIncomes => Set<FinancialSubsidiaryIncome>();
    public DbSet<ScoreTemplate> ScoreTemplates => Set<ScoreTemplate>();
    public DbSet<ScoreRule> ScoreRules => Set<ScoreRule>();
    public DbSet<DataError> DataErrors => Set<DataError>();
    public DbSet<MarketScanState> MarketScanStates => Set<MarketScanState>();
    public DbSet<MarketScanItem> MarketScanItems => Set<MarketScanItem>();
    public DbSet<StockDailyPriceCache> DailyPriceCaches => Set<StockDailyPriceCache>();
    public DbSet<StockAnnualPriceCache> AnnualPriceCaches => Set<StockAnnualPriceCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnnualDividend>().HasIndex(x => new { x.StockNo, x.FiscalYear }).IsUnique();
        modelBuilder.Entity<FinancialSubsidiaryIncome>()
            .HasIndex(x => new { x.StockNo, x.FiscalYear, x.EntityName, x.SourceUrl }).IsUnique();
        modelBuilder.Entity<WatchlistItem>()
            .HasOne(x => x.Stock).WithOne().HasForeignKey<WatchlistItem>(x => x.StockNo)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialStatement>()
            .HasIndex(x => new { x.StockNo, x.FiscalYear, x.Period, x.SourceName }).IsUnique();
        modelBuilder.Entity<FinancialStatement>()
            .HasIndex(x => new { x.StockNo, x.IsAnnual, x.FiscalYear });
        modelBuilder.Entity<ScoreRule>()
            .HasIndex(x => new { x.ScoreTemplateId, x.MetricKey }).IsUnique();
        modelBuilder.Entity<MarketScanItem>()
            .HasIndex(x => new { x.StockNo, x.FiscalYear }).IsUnique();
        modelBuilder.Entity<ScoreTemplate>()
            .HasMany(x => x.Rules).WithOne(x => x.ScoreTemplate)
            .HasForeignKey(x => x.ScoreTemplateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<StockAnnualPriceCache>()
            .HasIndex(x => new { x.StockNo, x.FiscalYear }).IsUnique();
    }
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(RoceDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureStockSchemaAsync(db, cancellationToken);
        await EnsureFinancialStatementSchemaAsync(db, cancellationToken);
        await EnsureFinanceIncomeSchemaAsync(db, cancellationToken);
        await EnsureScanSchemaAsync(db, cancellationToken);
        if (await db.ScoreTemplates.AnyAsync(cancellationToken)) return;
        var template = new ScoreTemplate { Name = "品質／資本效率", IsDefault = true };
        template.Rules.AddRange(ScoreEngine.DefaultRules());
        db.ScoreTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureStockSchemaAsync(RoceDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(cancellationToken);
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"Stocks\");";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    columns.Add(reader.GetString(1));
            }

            var additions = new (string Name, string Definition)[]
            {
                ("IndustryName", "TEXT NOT NULL DEFAULT ''"),
                ("ClosePrice", "TEXT NULL"),
                ("PriceChange", "TEXT NULL"),
                ("PriceChangePercent", "TEXT NULL"),
                ("DayHigh", "TEXT NULL"),
                ("DayLow", "TEXT NULL"),
                ("TradeVolume", "INTEGER NULL"),
                ("QuoteDate", "TEXT NOT NULL DEFAULT ''"),
                ("QuoteSource", "TEXT NOT NULL DEFAULT ''"),
                ("QuoteFetchedAt", "TEXT NULL")
            };
            foreach (var addition in additions.Where(x => !columns.Contains(x.Name)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE \"Stocks\" ADD COLUMN \"{addition.Name}\" {addition.Definition};";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }
    private static async Task EnsureFinancialStatementSchemaAsync(RoceDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(cancellationToken);
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"FinancialStatements\");";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    columns.Add(reader.GetString(1));
            }

            var additions = new (string Name, string Definition)[]
            {
                ("InterestIncome", "TEXT NULL"),
                ("ParentNetIncome", "TEXT NULL"),
                ("ParentEquity", "TEXT NULL"),
                ("ReportBasis", "TEXT NOT NULL DEFAULT ''"),
                ("OperatingCashFlow", "TEXT NULL"),
                ("CapexPpe", "TEXT NULL"),
                ("CapexIntangibles", "TEXT NULL"),
                ("WeightedAverageShares", "TEXT NULL"),
                ("WeightedAverageSharesSource", "TEXT NOT NULL DEFAULT ''")
            };
            foreach (var addition in additions.Where(x => !columns.Contains(x.Name)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE \"FinancialStatements\" ADD COLUMN \"{addition.Name}\" {addition.Definition};";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    private static async Task EnsureFinanceIncomeSchemaAsync(RoceDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AnnualDividends" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT, "StockNo" TEXT NOT NULL, "FiscalYear" INTEGER NOT NULL,
                "CashEarnings" TEXT NULL, "CashReserves" TEXT NULL, "StockEarnings" TEXT NULL, "StockReserves" TEXT NULL,
                "DecisionStatus" TEXT NOT NULL, "DecisionDate" TEXT NOT NULL,
                "SourceUrl" TEXT NOT NULL, "FetchedAt" TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AnnualDividends_StockNo_FiscalYear" ON "AnnualDividends" ("StockNo", "FiscalYear");
            CREATE TABLE IF NOT EXISTS "FinancialSubsidiaryIncomes" (
                "Id" INTEGER PRIMARY KEY AUTOINCREMENT, "StockNo" TEXT NOT NULL, "FiscalYear" INTEGER NOT NULL,
                "EntityName" TEXT NOT NULL, "Business" TEXT NOT NULL, "NetIncome" TEXT NOT NULL,
                "Basis" TEXT NOT NULL, "SourceName" TEXT NOT NULL, "SourceUrl" TEXT NOT NULL, "FetchedAt" TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FinancialSubsidiaryIncomes_StockNo_FiscalYear_EntityName_SourceUrl"
                ON "FinancialSubsidiaryIncomes" ("StockNo", "FiscalYear", "EntityName", "SourceUrl");
            """, cancellationToken);
    }

    private static async Task EnsureScanSchemaAsync(RoceDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MarketScanStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MarketScanStates" PRIMARY KEY,
                "FiscalYear" INTEGER NOT NULL,
                "RangeStartFiscalYear" INTEGER NULL,
                "RangeEndFiscalYear" INTEGER NULL,
                "Status" INTEGER NOT NULL,
                "Total" INTEGER NOT NULL,
                "Completed" INTEGER NOT NULL,
                "Success" INTEGER NOT NULL,
                "Failed" INTEGER NOT NULL,
                "CurrentStockNo" TEXT NULL,
                "StartedAt" TEXT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "MarketScanItems" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MarketScanItems" PRIMARY KEY AUTOINCREMENT,
                "StockNo" TEXT NOT NULL,
                "FiscalYear" INTEGER NOT NULL,
                "Success" INTEGER NOT NULL,
                "Message" TEXT NULL,
                "AttemptedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_MarketScanItems_StockNo_FiscalYear"
                ON "MarketScanItems" ("StockNo", "FiscalYear");
            """, cancellationToken);
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(cancellationToken);
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"MarketScanStates\");";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    columns.Add(reader.GetString(1));
            }

            var additions = new (string Name, string Definition)[]
            {
                ("RangeStartFiscalYear", "INTEGER NULL"),
                ("RangeEndFiscalYear", "INTEGER NULL")
            };
            foreach (var addition in additions.Where(x => !columns.Contains(x.Name)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE \"MarketScanStates\" ADD COLUMN \"{addition.Name}\" {addition.Definition};";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
        await EnsurePriceCacheSchemaAsync(db, cancellationToken);
    }

    private static async Task EnsurePriceCacheSchemaAsync(RoceDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "DailyPriceCaches" (
                "StockNo" TEXT NOT NULL CONSTRAINT "PK_DailyPriceCaches" PRIMARY KEY,
                "PricesJson" TEXT NOT NULL,
                "FetchedAt" TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "AnnualPriceCaches" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AnnualPriceCaches" PRIMARY KEY AUTOINCREMENT,
                "StockNo" TEXT NOT NULL,
                "FiscalYear" INTEGER NOT NULL,
                "Close" TEXT NULL,
                "TradingDate" TEXT NULL,
                "Source" TEXT NOT NULL DEFAULT '',
                "FetchedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AnnualPriceCaches_StockNo_FiscalYear"
                ON "AnnualPriceCaches" ("StockNo", "FiscalYear");
            """, cancellationToken);
    }
}

