using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class MarketScanService(RoceDbContext db) : IMarketScanService
{
    public async Task<MarketScanProgress> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await db.MarketScanStates.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state is not null)
        {
            var rangeStart = state.RangeStartFiscalYear ?? state.FiscalYear;
            var rangeEnd = state.RangeEndFiscalYear ?? state.FiscalYear;
            var eligibleCodes = await db.Stocks.AsNoTracking()
                .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
                .Select(x => x.StockNo).ToListAsync(cancellationToken);
            var counters = await CalculateRangeStatusAsync(
                db, eligibleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase), rangeStart, rangeEnd, cancellationToken);
            if (state.Total != counters.Total || state.Completed != counters.Completed ||
                state.Success != counters.Success || state.Failed != counters.Failed)
            {
                state.Total = counters.Total;
                state.Completed = counters.Completed;
                state.Success = counters.Success;
                state.Failed = counters.Failed;
                if (state.Completed >= state.Total && state.Total > 0) state.Status = MarketScanStatus.Completed;
                state.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            return ToProgress(state);
        }
        var total = await db.Stocks.CountAsync(x =>
            x.StatementKind == StatementKind.General && x.IsSupported, cancellationToken);
        return new(MarketScanStatus.Idle, DateTime.Today.Year - 1, total, 0, 0, 0, null, null, DateTime.UtcNow);
    }

    public async Task<MarketScanProgress> StartAsync(int fiscalYear, CancellationToken cancellationToken = default)
    {
        return await StartRangeInternalAsync(fiscalYear, 1, false, cancellationToken);
    }

    public async Task<MarketScanProgress> StartRangeAsync(
        int latestFiscalYear,
        int years,
        bool retryFailed,
        CancellationToken cancellationToken = default)
    {
        return await StartRangeInternalAsync(latestFiscalYear, years, retryFailed, cancellationToken);
    }

    private async Task<MarketScanProgress> StartRangeInternalAsync(
        int latestFiscalYear,
        int years,
        bool retryFailed,
        CancellationToken cancellationToken)
    {
        if (latestFiscalYear is < 1990 or > 2200) throw new InvalidOperationException("掃描年度格式錯誤。");
        if (years is < 1 or > 20) throw new InvalidOperationException("掃描年數必須介於 1 到 20 年。");

        var rangeStart = latestFiscalYear - years + 1;
        if (retryFailed)
        {
            var failedAttempts = await db.MarketScanItems
                .Where(x => x.FiscalYear >= rangeStart && x.FiscalYear <= latestFiscalYear && !x.Success)
                .ToListAsync(cancellationToken);
            db.MarketScanItems.RemoveRange(failedAttempts);
            await db.SaveChangesAsync(cancellationToken);
        }

        var eligibleCodes = await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
            .Select(x => x.StockNo).ToListAsync(cancellationToken);
        var eligible = eligibleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var status = await CalculateRangeStatusAsync(db, eligible, rangeStart, latestFiscalYear, cancellationToken);
        var currentYear = await FindNextPendingYearAsync(db, rangeStart, latestFiscalYear, cancellationToken) ?? latestFiscalYear;

        var state = await db.MarketScanStates.FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state is null)
        {
            state = new MarketScanState { Id = 1 };
            db.MarketScanStates.Add(state);
        }

        state.FiscalYear = currentYear;
        state.RangeStartFiscalYear = rangeStart;
        state.RangeEndFiscalYear = latestFiscalYear;
        state.Status = status.Completed >= status.Total && status.Total > 0 ? MarketScanStatus.Completed : MarketScanStatus.Running;
        state.Total = status.Total;
        state.Completed = status.Completed;
        state.Success = status.Success;
        state.Failed = status.Failed;
        state.CurrentStockNo = null;
        state.StartedAt = DateTime.UtcNow;
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
        state.CurrentStockNo, state.StartedAt, state.UpdatedAt, state.RangeStartFiscalYear, state.RangeEndFiscalYear);

    internal static bool HasCompleteBackfillFields(FinancialStatement statement) =>
        statement.IsAnnual &&
        statement.OperatingIncome is not null &&
        statement.TotalAssets is not null &&
        statement.CurrentLiabilities is not null &&
        statement.OperatingCashFlow is not null &&
        statement.CapexPpe is not null;

    internal static async Task<ScanCounters> CalculateRangeStatusAsync(
        RoceDbContext db,
        ISet<string> eligible,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken)
    {
        var reports = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.FiscalYear >= rangeStart && x.FiscalYear <= rangeEnd)
            .Where(x => x.IsAnnual &&
                x.OperatingIncome != null &&
                x.TotalAssets != null &&
                x.CurrentLiabilities != null &&
                x.OperatingCashFlow != null &&
                x.CapexPpe != null)
            .Select(x => new { x.StockNo, x.FiscalYear })
            .Distinct()
            .ToListAsync(cancellationToken);
        var completeKeys = reports.Where(x => eligible.Contains(x.StockNo))
            .Select(x => $"{x.FiscalYear}:{x.StockNo}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var attempted = await db.MarketScanItems.AsNoTracking()
            .Where(x => x.FiscalYear >= rangeStart && x.FiscalYear <= rangeEnd)
            .Select(x => new { x.StockNo, x.FiscalYear })
            .ToListAsync(cancellationToken);
        var failedKeys = attempted.Where(x => eligible.Contains(x.StockNo))
            .Select(x => $"{x.FiscalYear}:{x.StockNo}")
            .Where(x => !completeKeys.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var total = eligible.Count * (rangeEnd - rangeStart + 1);
        var success = completeKeys.Count;
        return new(total, Math.Min(total, success + failedKeys), success, failedKeys);
    }

    internal static async Task<int?> FindNextPendingYearAsync(
        RoceDbContext db,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken)
    {
        for (var year = rangeEnd; year >= rangeStart; year--)
        {
            if (await FindPendingStockAsync(db, year, cancellationToken) is not null) return year;
        }
        return null;
    }

    internal static async Task<Stock?> FindPendingStockAsync(RoceDbContext db, int year, CancellationToken cancellationToken)
    {
        return (await FindPendingStocksAsync(db, year, 1, cancellationToken)).FirstOrDefault();
    }

    internal static async Task<List<Stock>> FindPendingStocksAsync(
        RoceDbContext db,
        int year,
        int count,
        CancellationToken cancellationToken)
    {
        return await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
            .Where(x => !db.FinancialStatements.Any(s =>
                s.StockNo == x.StockNo && s.FiscalYear == year && s.IsAnnual &&
                s.OperatingIncome != null &&
                s.TotalAssets != null &&
                s.CurrentLiabilities != null &&
                s.OperatingCashFlow != null &&
                s.CapexPpe != null))
            .Where(x => !db.MarketScanItems.Any(i => i.StockNo == x.StockNo && i.FiscalYear == year && !i.Success))
            .OrderBy(x => x.StockNo)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    internal sealed record ScanCounters(int Total, int Completed, int Success, int Failed);
}

public sealed class MarketScanWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MarketScanWorker> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromMilliseconds(
            Math.Clamp(configuration.GetValue<int?>("MarketScan:DelayMilliseconds") ?? 1000, 250, 10000));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await ProcessOneAsync(stoppingToken);
                await Task.Delay(worked ? delay : TimeSpan.FromSeconds(2), stoppingToken);
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

        var rangeStart = state.RangeStartFiscalYear ?? state.FiscalYear;
        var rangeEnd = state.RangeEndFiscalYear ?? state.FiscalYear;
        var year = state.FiscalYear;
        if (year < rangeStart || year > rangeEnd)
        {
            year = rangeEnd;
            state.FiscalYear = year;
        }
        var batchSize = Math.Clamp(configuration.GetValue<int?>("MarketScan:BatchSize") ?? 3, 1, 5);
        var stocks = await MarketScanService.FindPendingStocksAsync(db, year, batchSize, cancellationToken);

        if (stocks.Count == 0)
        {
            var nextYear = await MarketScanService.FindNextPendingYearAsync(
                db, rangeStart, Math.Min(year - 1, rangeEnd), cancellationToken);
            if (nextYear is not null)
            {
                state.FiscalYear = nextYear.Value;
                await RefreshCountersAsync(db, state, rangeStart, rangeEnd, cancellationToken);
                state.CurrentStockNo = null;
                state.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }

            await RefreshCountersAsync(db, state, rangeStart, rangeEnd, cancellationToken);
            state.Status = state.Completed >= state.Total ? MarketScanStatus.Completed : MarketScanStatus.Paused;
            state.CurrentStockNo = null;
            state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        state.CurrentStockNo = stocks[0].StockNo;
        state.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var results = await Task.WhenAll(stocks.Select(stock =>
            FetchForScanAsync(provider, stock.StockNo, year, cancellationToken)));
        foreach (var result in results)
        {
            if (result.ErrorMessage is not null)
            {
                db.DataErrors.Add(new DataError
                {
                    StockNo = result.StockNo,
                    Provider = "全市場 MOPS 掃描",
                    Message = $"{year}: {result.ErrorMessage}"
                });
            }

            if (result.Statement is not null)
            {
                var statement = result.Statement;
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
                    existing.OperatingCashFlow = statement.OperatingCashFlow;
                    existing.CapexPpe = statement.CapexPpe;
                    existing.CapexIntangibles = statement.CapexIntangibles;
                    existing.WeightedAverageShares = statement.WeightedAverageShares;
                    existing.WeightedAverageSharesSource = statement.WeightedAverageSharesSource;
                    existing.SourceUrl = statement.SourceUrl;
                    existing.FetchedAt = statement.FetchedAt;
                }
            }

            var scanItem = await db.MarketScanItems.FirstOrDefaultAsync(x =>
                x.StockNo == result.StockNo && x.FiscalYear == year, cancellationToken);
            if (scanItem is null)
            {
                scanItem = new MarketScanItem { StockNo = result.StockNo, FiscalYear = year };
                db.MarketScanItems.Add(scanItem);
            }
            scanItem.Success = result.Success;
            scanItem.Message = result.Message;
            scanItem.AttemptedAt = DateTime.UtcNow;
        }
        state.CurrentStockNo = null;
        await db.SaveChangesAsync(cancellationToken);

        await RefreshCountersAsync(db, state, rangeStart, rangeEnd, cancellationToken);
        state.UpdatedAt = DateTime.UtcNow;
        if (state.Completed >= state.Total) state.Status = MarketScanStatus.Completed;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task RefreshCountersAsync(
        RoceDbContext db,
        MarketScanState state,
        int rangeStart,
        int rangeEnd,
        CancellationToken cancellationToken)
    {
        var eligibleCodes = await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
            .Select(x => x.StockNo).ToListAsync(cancellationToken);
        var counters = await MarketScanService.CalculateRangeStatusAsync(
            db, eligibleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase), rangeStart, rangeEnd, cancellationToken);
        state.Total = counters.Total;
        state.Completed = counters.Completed;
        state.Success = counters.Success;
        state.Failed = counters.Failed;
    }

    private static async Task<ScanFetchResult> FetchForScanAsync(
        IFinancialDataProvider provider,
        string stockNo,
        int year,
        CancellationToken cancellationToken)
    {
        try
        {
            var statement = await provider.FetchAnnualAsync(stockNo, year, cancellationToken);
            if (statement is null) return new(stockNo, null, false, "找不到完整年度年報", null);
            var success = MarketScanService.HasCompleteBackfillFields(statement);
            return new(stockNo, statement, success, success ? null : "缺少營業現金流或 PPE 資本支出欄位", null);
        }
        catch (Exception ex)
        {
            return new(stockNo, null, false, ex.Message, ex.Message);
        }
    }

    private sealed record ScanFetchResult(
        string StockNo,
        FinancialStatement? Statement,
        bool Success,
        string? Message,
        string? ErrorMessage);
}
