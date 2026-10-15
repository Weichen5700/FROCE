using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;
using RoceTool.Web;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var keyDirectory = Path.Combine(dataDirectory, "Keys");
Directory.CreateDirectory(keyDirectory);

builder.WebHost.UseUrls(builder.Configuration["AppUrl"] ?? "http://127.0.0.1:5050");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<FinanceResearchStore>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("RoceTool");
builder.Services.AddRoceInfrastructure($"Data Source={Path.Combine(dataDirectory, "roce.db")}");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<RoceDbContext>());
}

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/api/stocks", async (string? query, IStockAnalysisService service, CancellationToken ct) =>
    Results.Ok(await service.SearchAsync(query, ct)));

app.MapGet("/api/stocks/{stockNo}/analysis", async (string stockNo, int? year, IStockAnalysisService service, CancellationToken ct) =>
{
    var analysis = await service.GetAnalysisAsync(stockNo, year, ct);
    return analysis is null ? Results.NotFound() : Results.Ok(analysis);
});

app.MapGet("/api/stocks/{stockNo}/focus-metrics", async (string stockNo, int? year, IStockAnalysisService service, CancellationToken ct) =>
{
    var analysis = await service.GetAnalysisAsync(stockNo, year, ct);
    if (analysis is null) return Results.NotFound();

    var annual = analysis.SelectedAnnual;
    var roce = annual is null ? null : FinancialCalculator.CalculateRoce(annual).RocePercent;
    var roceWithInterest = annual is null ? null : FinancialCalculator.CalculateRoceIncludingInterest(annual).RocePercent;
    var roe = annual is null ? null : FinancialCalculator.ValueFor(MetricKey.Roe, annual);
    var freeCashFlow = annual is null ? null : FinancialCalculator.CalculateFreeCashFlow(annual);
    var quarter = analysis.LatestQuarter;

    return Results.Ok(new
    {
        analysis.Stock.StockNo,
        analysis.Stock.Name,
        analysis.Stock.Market,
        analysis.Stock.IndustryName,
        Year = annual?.FiscalYear,
        Period = annual?.Period,
        RocePercent = roce,
        RoceWithInterestPercent = roceWithInterest,
        annual?.InterestIncome,
        annual?.Eps,
        OperatingCashFlow = freeCashFlow?.OperatingCashFlow,
        CapitalExpenditure = freeCashFlow?.CapitalExpenditure,
        FreeCashFlow = freeCashFlow?.FreeCashFlow,
        WeightedAverageShares = freeCashFlow?.WeightedAverageShares,
        WeightedAverageSharesSource = freeCashFlow?.WeightedAverageSharesSource,
        FreeCashFlowPerShare = freeCashFlow?.FreeCashFlowPerShare,
        analysis.Stock.PeRatio,
        RoePercent = roe,
        PbRatio = analysis.Stock.PbRatio,
        DividendYieldPercent = analysis.Stock.DividendYield,
        analysis.Stock.ClosePrice,
        analysis.Stock.PriceChange,
        analysis.Stock.PriceChangePercent,
        analysis.Stock.DayHigh,
        analysis.Stock.DayLow,
        analysis.Stock.TradeVolume,
        analysis.Stock.QuoteDate,
        analysis.Stock.QuoteSource,
        analysis.Stock.QuoteFetchedAt,
        SourceName = annual?.SourceName,
        analysis.Stock.ValuationFetchedAt,
        LatestQuarter = quarter is null ? null : new
        {
            quarter.FiscalYear,
            quarter.Period,
            quarter.Eps,
            quarter.InterestIncome,
            RocePercent = FinancialCalculator.CalculateRoce(quarter).RocePercent,
            RoceWithInterestPercent = FinancialCalculator.CalculateRoceIncludingInterest(quarter).RocePercent,
            QualityScore = analysis.LatestQuarterScore?.TotalScore,
            QualityScoreCoveragePercent = analysis.LatestQuarterScore?.CoveragePercent,
            RoePercent = FinancialCalculator.ValueFor(MetricKey.Roe, quarter)
        }
    });
});

app.MapGet("/api/stocks/{stockNo}/price-data", async (string stockNo, IStockAnalysisService service,
    IOfficialDataProvider officialData, CancellationToken ct) =>
{
    var analysis = await service.GetAnalysisAsync(stockNo, cancellationToken: ct);
    if (analysis is null) return Results.NotFound();

    var daily = await officialData.FetchDailyClosingPricesAsync(stockNo, analysis.Stock.Market, cancellationToken: ct);
    var annual = await officialData.FetchAnnualClosingPricesAsync(
        stockNo, analysis.Stock.Market, analysis.AnnualHistory.Select(x => x.FiscalYear), cancellationToken: ct);
    var annualData = annual.Select(price =>
    {
        var statement = analysis.AnnualHistory.FirstOrDefault(x => x.FiscalYear == price.FiscalYear);
        var cashFlow = statement is null ? null : FinancialCalculator.CalculateFreeCashFlow(statement);
        return new
        {
            price.FiscalYear,
            price.TradingDate,
            price.Close,
            price.Source,
            Eps = statement?.Eps,
            StaticPe = statement is null ? null : FinancialCalculator.CalculateStaticPe(price.Close, statement.Eps),
            StaticPb = statement is null ? null : FinancialCalculator.CalculateStaticPb(
                price.Close, statement.TotalEquity, cashFlow?.WeightedAverageShares),
            BookValuePerShare = statement is null ? null : FinancialCalculator.CalculateBookValuePerShare(
                statement.TotalEquity, cashFlow?.WeightedAverageShares)
        };
    }).ToList();
    return Results.Ok(new
    {
        DailyPriceCount = daily.Count,
        LatestDailyClose = daily.OrderByDescending(x => x.Date).FirstOrDefault()?.Close,
        MovingAverages = MovingAverageCalculator.Calculate(daily),
        AnnualPrices = annualData
    });
});

app.MapGet("/api/stocks/{stockNo}/annual-prices", async (string stockNo, string? years, RoceDbContext db,
    IOfficialDataProvider officialData, CancellationToken ct) =>
{
    var stock = await db.Stocks.AsNoTracking().FirstOrDefaultAsync(x => x.StockNo == stockNo, ct);
    if (stock is null) return Results.NotFound();

    var requestedYears = (years ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => int.TryParse(value, out var year) ? year : 0)
        .Where(year => year is >= 1900 and <= 2200)
        .Distinct()
        .Take(10)
        .ToArray();
    var prices = await officialData.FetchAnnualClosingPricesAsync(stockNo, stock.Market, requestedYears, cancellationToken: ct);
    return Results.Ok(prices);
});

app.MapPost("/api/stocks/{stockNo}/refresh", async (string stockNo, IStockAnalysisService service, CancellationToken ct) =>
    Results.Ok(await service.RefreshStockAsync(stockNo, true, ct)));

app.MapPost("/api/stocks/{stockNo}/refresh-missing", async (string stockNo, IStockAnalysisService service, CancellationToken ct) =>
    Results.Ok(await service.RefreshStockAsync(stockNo, false, ct)));

app.MapPost("/api/stocks/{stockNo}/prices/refresh", async (string stockNo, IStockAnalysisService service, IOfficialDataProvider officialData, CancellationToken ct) =>
{
    var analysis = await service.GetAnalysisAsync(stockNo, null, ct);
    if (analysis is null) return Results.NotFound();
    try
    {
        var daily = await officialData.FetchDailyClosingPricesAsync(
            analysis.Stock.StockNo, analysis.Stock.Market, force: true, cancellationToken: ct);
        var annual = await officialData.FetchAnnualClosingPricesAsync(
            analysis.Stock.StockNo, analysis.Stock.Market,
            analysis.AnnualHistory.Select(x => x.FiscalYear), force: true, cancellationToken: ct);
        var found = annual.Count(x => x.Close is not null);
        return Results.Ok(new { success = true, message = $"已更新 {daily.Count} 筆日線、{found} 個年度末收盤參考。" });
    }
    catch (HttpRequestException ex)
    {
        return Results.Ok(new { success = false, message = $"官方資料暫時無法取得：{ex.Message}" });
    }
});

app.MapPost("/api/watchlist/{stockNo}", async (string stockNo, IStockAnalysisService service, CancellationToken ct) =>
{
    await service.AddWatchlistAsync(stockNo, ct);
    return Results.NoContent();
});

app.MapDelete("/api/watchlist/{stockNo}", async (string stockNo, IStockAnalysisService service, CancellationToken ct) =>
{
    await service.RemoveWatchlistAsync(stockNo, ct);
    return Results.NoContent();
});

app.MapGet("/api/market-scan/status", async (IMarketScanService scanner, CancellationToken ct) =>
    Results.Ok(await scanner.GetStatusAsync(ct)));

app.MapPost("/api/market-scan/start/{year:int}", async (int year, IStockAnalysisService analysis,
    IMarketScanService scanner, CancellationToken ct) =>
{
    await analysis.EnsureCatalogAsync(cancellationToken: ct);
    return Results.Ok(await scanner.StartAsync(year, ct));
});

app.MapPost("/api/market-scan/start-range/{latestYear:int}/{years:int}", async (int latestYear, int years,
    bool? retryFailed, IStockAnalysisService analysis, IMarketScanService scanner, CancellationToken ct) =>
{
    await analysis.EnsureCatalogAsync(cancellationToken: ct);
    return Results.Ok(await scanner.StartRangeAsync(latestYear, years, retryFailed ?? true, ct));
});

app.MapPost("/api/market-scan/pause", async (IMarketScanService scanner, CancellationToken ct) =>
    Results.Ok(await scanner.PauseAsync(ct)));
app.MapGet("/api/screen", async (decimal? minRoce, decimal? minScore, decimal? minCoverage, int? year,
    string? industry, CompanySizeBand? sizeBand, decimal? minTotalAssets, IStockAnalysisService service, CancellationToken ct) =>
    Results.Ok(await service.ScreenAsync(minRoce ?? 20m, minScore ?? 80m, minCoverage ?? 100m,
        year, industry, sizeBand, minTotalAssets, ct)));

app.MapGet("/api/score-template", async (IStockAnalysisService service, CancellationToken ct) =>
{
    var template = await service.GetDefaultTemplateAsync(ct);
    return Results.Ok(new
    {
        template.Id,
        template.Name,
        Rules = template.Rules.Select(x => new
        {
            Metric = x.MetricKey.ToString(), Label = ScoreEngine.Label(x.MetricKey),
            x.IsEnabled, x.Weight, Points = ScoreEngine.ParsePoints(x.PointsJson)
        })
    });
});

app.MapGet("/api/discovery/settings", () =>
{
    var settings = DiscoveryStateFile.Load(dataDirectory);
    var daysSince = settings.LastScanStartedAt is DateTime started ? (DateTime.UtcNow - started).Days : (int?)null;
    return Results.Ok(new
    {
        settings.Years,
        settings.LatestYear,
        settings.MinRoce,
        settings.MinScore,
        settings.LastScanStartedAt,
        settings.LastDiscoveredCount,
        DaysSinceLastScan = daysSince,
        CanRun = daysSince is null || daysSince >= 5
    });
});

app.MapPost("/api/discovery/discover", async (DiscoverySettings? settings, IStockAnalysisService service, CancellationToken ct) =>
{
    settings ??= DiscoveryStateFile.Load(dataDirectory);
    settings.LatestYear = Math.Clamp(settings.LatestYear, 1990, DateTime.Today.Year);
    settings.Years = Math.Clamp(settings.Years, 1, 10);
    settings.MinRoce = Math.Clamp(settings.MinRoce, 0m, 1000m);
    if (settings.MinScore is decimal minScore) settings.MinScore = Math.Clamp(minScore, 0m, 100m);

    var result = await service.DiscoverAsync(settings, ct);
    settings.LastScanStartedAt = DateTime.UtcNow;
    settings.LastDiscoveredCount = result.DiscoveredCount;
    DiscoveryStateFile.Save(dataDirectory, settings);
    return Results.Ok(result);
});

app.MapGet("/api/external-targets", () =>
{
    var settings = ExternalTargetStateFile.Load(dataDirectory);
    return Results.Ok(settings);
});

app.MapPost("/api/external-targets", (ExternalTargetSettings settings) =>
{
    settings.MinRoce = Math.Clamp(settings.MinRoce, 0m, 1000m);
    if (settings.MinScore is decimal minScore) settings.MinScore = Math.Clamp(minScore, 0m, 100m);
    ExternalTargetStateFile.Save(dataDirectory, settings);
    return Results.Ok(settings);
});

app.MapPost("/api/external-targets/verify", async (ExternalTargetSettings settings, IStockAnalysisService service, CancellationToken ct) =>
{
    settings.MinRoce = Math.Clamp(settings.MinRoce, 0m, 1000m);
    if (settings.MinScore is decimal minScore) settings.MinScore = Math.Clamp(minScore, 0m, 100m);
    var result = await service.VerifyExternalTargetsAsync(settings, ct);
    settings.LastVerifiedAt = DateTime.UtcNow;
    ExternalTargetStateFile.Save(dataDirectory, settings);
    return Results.Ok(result);
});

if (builder.Configuration.GetValue("OpenBrowser", true) && OperatingSystem.IsWindows())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo(builder.Configuration["AppUrl"] ?? "http://127.0.0.1:5050") { UseShellExecute = true }); }
        catch { }
    });
}

await app.RunAsync();




