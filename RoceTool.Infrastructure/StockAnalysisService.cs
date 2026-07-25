using Microsoft.EntityFrameworkCore;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class StockAnalysisService(
    RoceDbContext db,
    IOfficialDataProvider officialProvider,
    IFinancialDataProvider annualProvider) : IStockAnalysisService
{
    public async Task EnsureCatalogAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var newest = await db.Stocks.Select(x => (DateTime?)x.UpdatedAt).MaxAsync(cancellationToken);
        var needsIndustryBackfill = await db.Stocks.AnyAsync(x => x.IndustryName == "", cancellationToken);
        if (!force && !needsIndustryBackfill && newest is not null && newest > DateTime.UtcNow.AddDays(-1)) return;

        try
        {
            var incoming = (await officialProvider.FetchStockCatalogAsync(cancellationToken))
                .ToDictionary(x => x.StockNo, StringComparer.OrdinalIgnoreCase);
            await officialProvider.ApplyValuationsAsync(incoming, cancellationToken);
            var existing = await db.Stocks.ToDictionaryAsync(x => x.StockNo, cancellationToken);
            foreach (var item in incoming.Values)
            {
                if (existing.TryGetValue(item.StockNo, out var stock))
                {
                    stock.Name = item.Name;
                    stock.Market = item.Market;
                    stock.IndustryCode = item.IndustryCode;
                    stock.IndustryName = item.IndustryName;
                    stock.StatementKind = item.StatementKind;
                    stock.IsSupported = item.IsSupported;
                    stock.PeRatio = item.PeRatio;
                    stock.PbRatio = item.PbRatio;
                    stock.DividendYield = item.DividendYield;
                    stock.ValuationFetchedAt = item.ValuationFetchedAt;
                    stock.ClosePrice = item.ClosePrice;
                    stock.PriceChange = item.PriceChange;
                    stock.PriceChangePercent = item.PriceChangePercent;
                    stock.DayHigh = item.DayHigh;
                    stock.DayLow = item.DayLow;
                    stock.TradeVolume = item.TradeVolume;
                    stock.QuoteDate = item.QuoteDate;
                    stock.QuoteSource = item.QuoteSource;
                    stock.QuoteFetchedAt = item.QuoteFetchedAt;
                    stock.UpdatedAt = item.UpdatedAt;
                }
                else db.Stocks.Add(item);
            }

            var statements = await officialProvider.FetchLatestStatementsAsync(cancellationToken);
            foreach (var statement in statements) await UpsertStatementAsync(statement, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            db.DataErrors.Add(new DataError { Provider = "官方 OpenAPI", Message = ex.Message });
            await db.SaveChangesAsync(cancellationToken);
            if (!await db.Stocks.AnyAsync(cancellationToken)) throw;
        }
    }

    public async Task<IReadOnlyList<Stock>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(cancellationToken: cancellationToken);
        query = query?.Trim() ?? "";
        var stocks = db.Stocks.AsNoTracking();
        if (query.Length > 0) stocks = stocks.Where(x => x.StockNo.Contains(query) || x.Name.Contains(query));
        return await stocks.OrderBy(x => x.StockNo).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockAnalysis>> GetWatchlistAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(cancellationToken: cancellationToken);
        var codes = await db.Watchlist.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.AddedAt)
            .Select(x => x.StockNo).ToListAsync(cancellationToken);
        var results = new List<StockAnalysis>();
        foreach (var code in codes)
        {
            var analysis = await GetAnalysisAsync(code, cancellationToken: cancellationToken);
            if (analysis is not null) results.Add(analysis);
        }
        return results;
    }

    public async Task<StockAnalysis?> GetAnalysisAsync(string stockNo, int? year = null, CancellationToken cancellationToken = default)
    {
        var stock = await db.Stocks.AsNoTracking().FirstOrDefaultAsync(x => x.StockNo == stockNo, cancellationToken);
        if (stock is null) return null;

        var all = await db.FinancialStatements.AsNoTracking().Where(x => x.StockNo == stockNo)
            .OrderByDescending(x => x.FiscalYear).ThenByDescending(x => x.FetchedAt).ToListAsync(cancellationToken);
        var annual = all.Where(x => x.IsAnnual).GroupBy(x => x.FiscalYear)
            .Select(ChooseSource).OrderByDescending(x => x.FiscalYear).ToList();
        var selected = year is null ? annual.FirstOrDefault() : annual.FirstOrDefault(x => x.FiscalYear == year);
        var quarter = all.Where(x => !x.IsAnnual).OrderByDescending(x => x.FiscalYear)
            .ThenByDescending(x => x.Period).FirstOrDefault();
        var template = await GetDefaultTemplateAsync(cancellationToken);
        var score = stock.StatementKind == StatementKind.General && selected is not null
            ? ScoreEngine.Calculate(selected, template.Rules) : null;
        var latestQuarterScore = stock.StatementKind == StatementKind.General && quarter is not null
            ? ScoreEngine.Calculate(quarter, template.Rules) : null;
        var peerComparison = stock.StatementKind == StatementKind.General && selected is not null
            ? await GetPeerComparisonAsync(stock, selected, cancellationToken) : null;

        return new StockAnalysis
        {
            Stock = stock,
            SelectedAnnual = selected,
            LatestQuarter = quarter,
            AnnualHistory = annual,
            Score = score,
            LatestQuarterScore = latestQuarterScore,
            PeerComparison = peerComparison
        };
    }

    public async Task<StockScreenResult> ScreenAsync(
        decimal minRoce = 20m,
        decimal minScore = 80m,
        decimal minCoverage = 100m,
        int? year = null,
        string? industryName = null,
        CompanySizeBand? sizeBand = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(cancellationToken: cancellationToken);
        var stocks = await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IsSupported)
            .ToDictionaryAsync(x => x.StockNo, cancellationToken);
        var statements = await db.FinancialStatements.AsNoTracking().Where(x => x.IsAnnual).ToListAsync(cancellationToken);
        var availableYears = statements.Select(x => x.FiscalYear).Distinct().OrderByDescending(x => x).ToList();
        var availableIndustries = stocks.Values.Select(IndustryLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var template = await GetDefaultTemplateAsync(cancellationToken);
        var evaluated = new List<EvaluatedCompany>();

        foreach (var stockGroup in statements.Where(x => stocks.ContainsKey(x.StockNo)).GroupBy(x => x.StockNo))
        {
            var candidates = year is null
                ? stockGroup.Where(x => x.FiscalYear == stockGroup.Max(s => s.FiscalYear))
                : stockGroup.Where(x => x.FiscalYear == year.Value);
            var statement = candidates.GroupBy(x => x.FiscalYear).Select(ChooseSource).FirstOrDefault();
            if (statement is null) continue;
            var roce = FinancialCalculator.CalculateRoce(statement);
            var score = ScoreEngine.Calculate(statement, template.Rules);
            if (roce.RocePercent is null || roce.CapitalEmployed is null || score.TotalScore is null) continue;
            evaluated.Add(new EvaluatedCompany(stocks[stockGroup.Key], statement, roce.RocePercent.Value,
                score.TotalScore.Value, score.CoveragePercent, roce.CapitalEmployed.Value));
        }

        var peerGroups = evaluated.GroupBy(x => IndustryLabel(x.Stock), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.Roce).ToList(), StringComparer.OrdinalIgnoreCase);
        var sizeMaps = peerGroups.ToDictionary(
            x => x.Key,
            x => AssignSizeBands(x.Value.Select(v => new PeerValue(v.Stock.StockNo, v.Roce, v.CapitalEmployed))),
            StringComparer.OrdinalIgnoreCase);
        var items = new List<StockScreenItem>();
        foreach (var candidate in evaluated)
        {
            var label = IndustryLabel(candidate.Stock);
            var band = sizeMaps[label][candidate.Stock.StockNo];
            if (!string.IsNullOrWhiteSpace(industryName) && !string.Equals(label, industryName, StringComparison.OrdinalIgnoreCase)) continue;
            if (sizeBand is not null && band != sizeBand.Value) continue;
            if (candidate.Roce < minRoce || candidate.Score < minScore || candidate.Coverage < minCoverage) continue;

            var peers = peerGroups[label];
            var industryRank = peers.Count(x => x.Roce > candidate.Roce) + 1;
            var industryMedian = Median(peers.Select(x => x.Roce));
            var sizePeers = peers.Where(x => sizeMaps[label][x.Stock.StockNo] == band).ToList();
            var sizeRank = sizePeers.Count(x => x.Roce > candidate.Roce) + 1;
            var sizeMedian = Median(sizePeers.Select(x => x.Roce));
            items.Add(new StockScreenItem(candidate.Stock, candidate.Statement, candidate.Roce, candidate.Score,
                candidate.Coverage, industryRank, peers.Count, industryMedian, candidate.CapitalEmployed,
                band, sizeRank, sizePeers.Count, sizeMedian));
        }

        return new StockScreenResult(
            items.OrderByDescending(x => x.Score).ThenByDescending(x => x.Roce).ToList(),
            evaluated.Count, stocks.Count, availableYears, availableIndustries);
    }

    public async Task AddWatchlistAsync(string stockNo, CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(cancellationToken: cancellationToken);
        if (!await db.Stocks.AnyAsync(x => x.StockNo == stockNo, cancellationToken))
            throw new InvalidOperationException("找不到股票代號。");
        if (!await db.Watchlist.AnyAsync(x => x.StockNo == stockNo, cancellationToken))
        {
            var next = (await db.Watchlist.MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? 0) + 1;
            db.Watchlist.Add(new WatchlistItem { StockNo = stockNo, SortOrder = next });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveWatchlistAsync(string stockNo, CancellationToken cancellationToken = default)
    {
        var item = await db.Watchlist.FindAsync([stockNo], cancellationToken);
        if (item is null) return;
        db.Watchlist.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshResult> RefreshStockAsync(string stockNo, bool force = false, CancellationToken cancellationToken = default)
    {
        await EnsureCatalogAsync(force, cancellationToken);
        var stock = await db.Stocks.FirstOrDefaultAsync(x => x.StockNo == stockNo, cancellationToken);
        if (stock is null) return new(false, "找不到股票代號。");
        if (stock.StatementKind != StatementKind.General)
            return new(true, "金融業可追蹤財報摘要，但不套用一般業 ROCE 評分。");

        var count = 0;
        var errors = new List<string>();
        for (var year = DateTime.Today.Year - 1; year >= DateTime.Today.Year - 5; year--)
        {
            var existing = await db.FinancialStatements.FirstOrDefaultAsync(x =>
                x.StockNo == stockNo && x.FiscalYear == year && x.IsAnnual && x.SourceKind == SourceKind.Mops,
                cancellationToken);
            if (existing is not null && !force && existing.InterestIncome is not null) { count++; continue; }

            try
            {
                var statement = await annualProvider.FetchAnnualAsync(stockNo, year, cancellationToken);
                if (statement is null) { errors.Add($"{year} 無年報"); continue; }
                await UpsertStatementAsync(statement, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                count++;
            }
            catch (Exception ex)
            {
                errors.Add($"{year}: {ex.Message}");
                db.DataErrors.Add(new DataError { StockNo = stockNo, Provider = "MOPS iXBRL", Message = $"{year}: {ex.Message}" });
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var latestQuarter = await db.FinancialStatements.AsNoTracking().Where(x => x.StockNo == stockNo && !x.IsAnnual)
            .OrderByDescending(x => x.FiscalYear).ThenByDescending(x => x.Period).FirstOrDefaultAsync(cancellationToken);
        if (latestQuarter is not null)
        {
            var mopsQuarter = await db.FinancialStatements.FirstOrDefaultAsync(x =>
                x.StockNo == stockNo && x.FiscalYear == latestQuarter.FiscalYear && x.Period == latestQuarter.Period &&
                x.SourceKind == SourceKind.Mops, cancellationToken);
            if (force || mopsQuarter?.InterestIncome is null)
            {
                try
                {
                    var quarterNumber = int.Parse(latestQuarter.Period[1].ToString());
                    var statement = await annualProvider.FetchQuarterAsync(stockNo, latestQuarter.FiscalYear, quarterNumber, cancellationToken);
                    if (statement is not null)
                    {
                        await UpsertStatementAsync(statement, cancellationToken);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{latestQuarter.Period}: {ex.Message}");
                    db.DataErrors.Add(new DataError { StockNo = stockNo, Provider = "MOPS iXBRL", Message = $"{latestQuarter.Period}: {ex.Message}" });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        var message = count > 0 ? $"已取得 {count} 個完整年度；最新季已補充 MOPS iXBRL 欄位。" : "未取得完整年度資料，可改用 CSV 匯入。";
        if (errors.Count > 0) message += $"（{errors.Count} 筆未取得）";
        return new(count > 0, message, count);
    }

    public async Task<ScoreTemplate> GetDefaultTemplateAsync(CancellationToken cancellationToken = default)
    {
        var template = await db.ScoreTemplates.Include(x => x.Rules)
            .FirstOrDefaultAsync(x => x.IsDefault, cancellationToken);
        if (template is not null) return template;
        template = new ScoreTemplate { Name = "品質／資本效率", IsDefault = true };
        template.Rules.AddRange(ScoreEngine.DefaultRules());
        db.ScoreTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task SaveTemplateAsync(ScoreTemplate template, CancellationToken cancellationToken = default)
    {
        var enabled = template.Rules.Where(x => x.IsEnabled).ToList();
        if (enabled.Count == 0 || enabled.Sum(x => x.Weight) != 100m)
            throw new InvalidOperationException("啟用指標的權重合計必須等於 100%。");
        if (enabled.Any(x => x.Weight < 0 || ScoreEngine.ParsePoints(x.PointsJson).Count < 2))
            throw new InvalidOperationException("權重不可為負數，且每個指標至少需要兩個門檻點。");

        var current = await GetDefaultTemplateAsync(cancellationToken);
        current.Name = template.Name;
        foreach (var rule in template.Rules)
        {
            var target = current.Rules.First(x => x.MetricKey == rule.MetricKey);
            target.IsEnabled = rule.IsEnabled;
            target.Weight = rule.Weight;
            target.PointsJson = rule.PointsJson;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetTemplateAsync(CancellationToken cancellationToken = default)
    {
        var current = await GetDefaultTemplateAsync(cancellationToken);
        db.ScoreRules.RemoveRange(current.Rules);
        current.Rules = ScoreEngine.DefaultRules().ToList();
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<PeerComparison?> GetPeerComparisonAsync(
        Stock stock, FinancialStatement selected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stock.IndustryName)) return null;
        var peerCodes = await db.Stocks.AsNoTracking()
            .Where(x => x.StatementKind == StatementKind.General && x.IndustryName == stock.IndustryName)
            .Select(x => x.StockNo).ToListAsync(cancellationToken);
        var peerStatements = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.IsAnnual && x.FiscalYear == selected.FiscalYear && peerCodes.Contains(x.StockNo))
            .ToListAsync(cancellationToken);
        var values = new List<PeerValue>();
        foreach (var group in peerStatements.GroupBy(x => x.StockNo))
        {
            var statement = group.GroupBy(x => x.FiscalYear).Select(ChooseSource).First();
            var result = FinancialCalculator.CalculateRoce(statement);
            if (result.RocePercent is not null && result.CapitalEmployed is not null)
                values.Add(new PeerValue(group.Key, result.RocePercent.Value, result.CapitalEmployed.Value));
        }

        var target = FinancialCalculator.CalculateRoce(selected);
        if (target.RocePercent is null || target.CapitalEmployed is null || values.Count == 0) return null;
        var sizeMap = AssignSizeBands(values);
        if (!sizeMap.TryGetValue(stock.StockNo, out var band)) return null;
        var sizePeers = values.Where(x => sizeMap[x.StockNo] == band).ToList();
        return new PeerComparison(
            stock.IndustryName,
            values.Count(x => x.Roce > target.RocePercent.Value) + 1,
            values.Count,
            Median(values.Select(x => x.Roce)),
            target.CapitalEmployed.Value,
            band,
            sizePeers.Count(x => x.Roce > target.RocePercent.Value) + 1,
            sizePeers.Count,
            Median(sizePeers.Select(x => x.Roce)));
    }

    private static Dictionary<string, CompanySizeBand> AssignSizeBands(IEnumerable<PeerValue> peers)
    {
        var ordered = peers.OrderBy(x => x.CapitalEmployed).ThenBy(x => x.StockNo).ToList();
        var result = new Dictionary<string, CompanySizeBand>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            var bucket = Math.Min(2, i * 3 / ordered.Count);
            result[ordered[i].StockNo] = (CompanySizeBand)bucket;
        }
        return result;
    }

    private static decimal Median(IEnumerable<decimal> source)
    {
        var values = source.OrderBy(x => x).ToList();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2m;
    }

    private static string IndustryLabel(Stock stock) => !string.IsNullOrWhiteSpace(stock.IndustryName)
        ? stock.IndustryName : "未分類";

    private sealed record PeerValue(string StockNo, decimal Roce, decimal CapitalEmployed);
    private sealed record EvaluatedCompany(Stock Stock, FinancialStatement Statement, decimal Roce,
        decimal Score, decimal Coverage, decimal CapitalEmployed);

    private async Task UpsertStatementAsync(FinancialStatement incoming, CancellationToken cancellationToken)
    {
        var existing = await db.FinancialStatements.FirstOrDefaultAsync(x =>
            x.StockNo == incoming.StockNo && x.FiscalYear == incoming.FiscalYear &&
            x.Period == incoming.Period && x.SourceName == incoming.SourceName, cancellationToken);
        if (existing is null) { db.FinancialStatements.Add(incoming); return; }

        existing.IsAnnual = incoming.IsAnnual;
        existing.OperatingRevenue = incoming.OperatingRevenue;
        existing.OperatingIncome = incoming.OperatingIncome;
        existing.InterestIncome = incoming.InterestIncome;
        existing.NetIncome = incoming.NetIncome;
        existing.CurrentAssets = incoming.CurrentAssets;
        existing.TotalAssets = incoming.TotalAssets;
        existing.CurrentLiabilities = incoming.CurrentLiabilities;
        existing.TotalLiabilities = incoming.TotalLiabilities;
        existing.TotalEquity = incoming.TotalEquity;
        existing.Eps = incoming.Eps;
        existing.SourceKind = incoming.SourceKind;
        existing.SourceUrl = incoming.SourceUrl;
        existing.FetchedAt = incoming.FetchedAt;
    }

    private static FinancialStatement ChooseSource(IGrouping<int, FinancialStatement> group) =>
        group.OrderByDescending(x => x.IsPreferred)
            .ThenBy(x => x.SourceKind == SourceKind.Mops ? 0 : x.SourceKind == SourceKind.Csv ? 1 : 2)
            .ThenByDescending(x => x.FetchedAt).First();
}







