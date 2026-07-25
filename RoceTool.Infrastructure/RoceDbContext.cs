using Microsoft.EntityFrameworkCore;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class RoceDbContext(DbContextOptions<RoceDbContext> options) : DbContext(options)
{
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<WatchlistItem> Watchlist => Set<WatchlistItem>();
    public DbSet<FinancialStatement> FinancialStatements => Set<FinancialStatement>();
    public DbSet<ScoreTemplate> ScoreTemplates => Set<ScoreTemplate>();
    public DbSet<ScoreRule> ScoreRules => Set<ScoreRule>();
    public DbSet<DataError> DataErrors => Set<DataError>();
    public DbSet<MarketScanState> MarketScanStates => Set<MarketScanState>();
    public DbSet<MarketScanItem> MarketScanItems => Set<MarketScanItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(RoceDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureStockSchemaAsync(db, cancellationToken);
        await EnsureFinancialStatementSchemaAsync(db, cancellationToken);
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
            var hasInterestIncome = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"FinancialStatements\");";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    if (string.Equals(reader.GetString(1), "InterestIncome", StringComparison.OrdinalIgnoreCase))
                        hasInterestIncome = true;
            }
            if (!hasInterestIncome)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE \"FinancialStatements\" ADD COLUMN \"InterestIncome\" TEXT NULL;";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    private static Task EnsureScanSchemaAsync(RoceDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MarketScanStates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MarketScanStates" PRIMARY KEY,
                "FiscalYear" INTEGER NOT NULL,
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
}

