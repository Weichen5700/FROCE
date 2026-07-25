using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using RoceTool.Core;
using RoceTool.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var keyDirectory = Path.Combine(dataDirectory, "Keys");
Directory.CreateDirectory(keyDirectory);

builder.WebHost.UseUrls(builder.Configuration["AppUrl"] ?? "http://127.0.0.1:5050");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorPages();
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

app.MapPost("/api/stocks/{stockNo}/refresh", async (string stockNo, IStockAnalysisService service, CancellationToken ct) =>
    Results.Ok(await service.RefreshStockAsync(stockNo, true, ct)));

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

app.MapPost("/api/market-scan/pause", async (IMarketScanService scanner, CancellationToken ct) =>
    Results.Ok(await scanner.PauseAsync(ct)));
app.MapGet("/api/screen", async (decimal? minRoce, decimal? minScore, decimal? minCoverage, int? year,
    string? industry, CompanySizeBand? sizeBand, IStockAnalysisService service, CancellationToken ct) =>
    Results.Ok(await service.ScreenAsync(minRoce ?? 20m, minScore ?? 80m, minCoverage ?? 100m,
        year, industry, sizeBand, ct)));

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

if (builder.Configuration.GetValue("OpenBrowser", true) && OperatingSystem.IsWindows())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo(builder.Configuration["AppUrl"] ?? "http://127.0.0.1:5050") { UseShellExecute = true }); }
        catch { }
    });
}

await app.RunAsync();




