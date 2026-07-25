using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class MarketScanService(RoceDbContext db) : IMarketScanService
{
    public async Task<MarketScanProgress> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await db.MarketScanStates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state is not null) return ToProgress(state);
        var total = await db.Stocks.CountAsync(x =>
            x.StatementKind == StatementKind.General && x.IsSupported, cancellationToken);
        return new(MarketScanStatus.Idle, DateTime.Today.Year - 1, total, 0, 0, 0, null, null, DateTime.UtcNow);
    }

    public async Task<MarketScanProgress> StartAsync(int fiscalYear, CancellationToken cancellationToken = default)
    {
        if (fiscalYear is < 1990 or > 2200) throw new InvalidOperationException("掃描年度格式錯誤。");

        var eligibleCodes = await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
            .Select(x => x.StockNo).ToListAsync(cancellationToken);
        var reportCodes = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.FiscalYear == fiscalYear && x.IsAnnual && x.SourceKind == SourceKind.Mops)
            .Select(x => x.StockNo).Distinct().ToListAsync(cancellationToken);
        var attempts = await db.MarketScanItems.AsNoTracking()
            .Where(x => x.FiscalYear == fiscalYear).ToListAsync(cancellationToken);

        var eligible = eligibleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var successes = reportCodes.Where(eligible.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        successes.UnionWith(attempts.Where(x => x.Success).Select(x => x.StockNo).Where(eligible.Contains));
        var failed = attempts.Where(x => !x.Success && !successes.Contains(x.StockNo))
            .Select(x => x.StockNo).Where(eligible.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var completed = Math.Min(eligible.Count, successes.Count + failed);

        var state = await db.MarketScanStates.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state is null)
        {
            state = new MarketScanState { Id = 1 };
            db.MarketScanStates.Add(state);
        }

        state.FiscalYear = fiscalYear;
        state.Status = completed >= eligible.Count && eligible.Count > 0 ? MarketScanStatus.Completed : MarketScanStatus.Running;
        state.Total = eligible.Count;
        state.Completed = completed;
        state.Success = successes.Count;
        state.Failed = failed;
        state.CurrentStockNo = null;
        state.StartedAt ??= DateTime.UtcNow;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToProgress(state);
    }

    public async Task<MarketScanProgress> PauseAsync(CancellationToken cancellationToken = default)
    {
        var state = await db.MarketScanStates.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state is null) return await GetStatusAsync(cancellationToken);
        if (state.Status == MarketScanStatus.Running) state.Status = MarketScanStatus.Paused;
        state.CurrentStockNo = null;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToProgress(state);
    }

    internal static MarketScanProgress ToProgress(MarketScanState state) => new(
        state.Status, state.FiscalYear, state.Total, state.Completed, state.Success, state.Failed,
        state.CurrentStockNo, state.StartedAt, state.UpdatedAt);
}

public sealed class MarketScanWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MarketScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await ProcessOneAsync(stoppingToken);
                await Task.Delay(worked ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "全市場背景掃描發生未預期錯誤");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoceDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IFinancialDataProvider>();
        var state = await db.MarketScanStates.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state?.Status != MarketScanStatus.Running) return false;

        var year = state.FiscalYear;
        var stock = await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
            .Where(x => !db.FinancialStatements.Any(s =>
                s.StockNo == x.StockNo && s.FiscalYear == year && s.IsAnnual && s.SourceKind == SourceKind.Mops))
            .Where(x => !db.MarketScanItems.Any(i => i.StockNo == x.StockNo && i.FiscalYear == year))
            .OrderBy(x => x.StockNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (stock is null)
        {
            state.Status = MarketScanStatus.Completed;
            state.Completed = state.Total;
            state.CurrentStockNo = null;
            state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        state.CurrentStockNo = stock.StockNo;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var success = false;
        string? message = null;
        try
        {
            var statement = await provider.FetchAnnualAsync(stock.StockNo, year, cancellationToken);
            if (statement is null) message = "找不到完整年度年報";
            else
            {
                var existing = await db.FinancialStatements.FirstOrDefaultAsync(x =>
                    x.StockNo == statement.StockNo && x.FiscalYear == statement.FiscalYear &&
                    x.Period == statement.Period && x.SourceName == statement.SourceName, cancellationToken);
                if (existing is null) db.FinancialStatements.Add(statement);
                else
                {
                    existing.OperatingRevenue = statement.OperatingRevenue;
                    existing.OperatingIncome = statement.OperatingIncome;
                    existing.InterestIncome = statement.InterestIncome;
                    existing.NetIncome = statement.NetIncome;
                    existing.CurrentAssets = statement.CurrentAssets;
                    existing.TotalAssets = statement.TotalAssets;
                    existing.CurrentLiabilities = statement.CurrentLiabilities;
                    existing.TotalLiabilities = statement.TotalLiabilities;
                    existing.TotalEquity = statement.TotalEquity;
                    existing.Eps = statement.Eps;
                    existing.SourceUrl = statement.SourceUrl;
                    existing.FetchedAt = statement.FetchedAt;
                }
                success = true;
            }
        }
        catch (Exception ex)
        {
            message = ex.Message;
            db.DataErrors.Add(new DataError
            {
                StockNo = stock.StockNo,
                Provider = "全市場 MOPS 掃描",
                Message = $"{year}: {ex.Message}"
            });
        }

        db.MarketScanItems.Add(new MarketScanItem
        {
            StockNo = stock.StockNo,
            FiscalYear = year,
            Success = success,
            Message = message,
            AttemptedAt = DateTime.UtcNow
        });
        state.Completed = Math.Min(state.Total, state.Completed + 1);
        if (success) state.Success++; else state.Failed++;
        state.CurrentStockNo = null;
        state.UpdatedAt = DateTime.UtcNow;
        if (state.Completed >= state.Total) state.Status = MarketScanStatus.Completed;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
