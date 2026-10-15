using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages;

public sealed class ScreenModel(IStockAnalysisService service, IMarketScanService scanner) : PageModel
{
    [BindProperty(SupportsGet = true)] public decimal MinRoce { get; set; } = 20m;
    [BindProperty(SupportsGet = true)] public decimal MinScore { get; set; } = 80m;
    [BindProperty(SupportsGet = true)] public decimal MinCoverage { get; set; } = 100m;
    [BindProperty(SupportsGet = true)] public decimal? MinTotalAssets { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string? Industry { get; set; }
    [BindProperty(SupportsGet = true)] public CompanySizeBand? SizeBand { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "score";
    [BindProperty] public int ScanYear { get; set; } = DateTime.Today.Year - 1;
    [BindProperty] public int ScanYears { get; set; } = 7;

    public StockScreenResult Result { get; private set; } = new([], 0, 0, [], []);
    public IReadOnlyList<StockScreenItem> Items { get; private set; } = [];
    public MarketScanProgress Scan { get; private set; } =
        new(MarketScanStatus.Idle, DateTime.Today.Year - 1, 0, 0, 0, 0, null, null, DateTime.UtcNow);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MinRoce = Math.Clamp(MinRoce, -1000m, 1000m);
        MinScore = Math.Clamp(MinScore, 0m, 100m);
        MinCoverage = Math.Clamp(MinCoverage, 0m, 100m);
        if (MinTotalAssets is decimal minAssets) MinTotalAssets = Math.Clamp(minAssets, 0m, 1_000_000m);
        Result = await service.ScreenAsync(MinRoce, MinScore, MinCoverage, Year, Industry, SizeBand, MinTotalAssets, cancellationToken);
        Year ??= Result.SelectedYear;
        Scan = await scanner.GetStatusAsync(cancellationToken);
        ScanYear = Scan.Status == MarketScanStatus.Idle ? DateTime.Today.Year - 1 : Scan.RangeEndFiscalYear ?? Scan.FiscalYear;
        ScanYears = Scan.Status == MarketScanStatus.Idle ? 7 : Scan.ScanYears;
        Items = Sort switch
        {
            "roce" => Result.Items.OrderByDescending(x => x.Roce).ThenByDescending(x => x.Score).ToList(),
            "eps" => Result.Items.OrderByDescending(x => x.Statement.Eps).ThenByDescending(x => x.Roce).ToList(),
            "pe" => Result.Items.OrderBy(x => x.Stock.PeRatio ?? decimal.MaxValue).ThenByDescending(x => x.Roce).ToList(),
            "roe" => Result.Items.OrderByDescending(x => FinancialCalculator.ValueFor(MetricKey.Roe, x.Statement)).ThenByDescending(x => x.Roce).ToList(),
            "stock" => Result.Items.OrderBy(x => x.Stock.StockNo).ToList(),
            _ => Result.Items.OrderByDescending(x => x.Score).ThenByDescending(x => x.Roce).ToList()
        };
    }

    public async Task<IActionResult> OnPostStartScanAsync(CancellationToken cancellationToken)
    {
        await service.EnsureCatalogAsync(cancellationToken: cancellationToken);
        ScanYears = Math.Clamp(ScanYears, 1, 20);
        var progress = ScanYears == 1
            ? await scanner.StartAsync(ScanYear, cancellationToken)
            : await scanner.StartRangeAsync(ScanYear, ScanYears, retryFailed: true, cancellationToken);
        TempData["Message"] = progress.Status == MarketScanStatus.Completed
            ? $"{ScanYear} 起算 {ScanYears} 年已完成全市場掃描。"
            : $"已開始掃描 {progress.RangeEndFiscalYear ?? ScanYear} 至 {progress.RangeStartFiscalYear ?? ScanYear} 完整年度；可關閉網站，稍後重新啟動後續跑。";
        return RedirectWithFilters();
    }

    public async Task<IActionResult> OnPostPauseScanAsync(CancellationToken cancellationToken)
    {
        await scanner.PauseAsync(cancellationToken);
        TempData["Message"] = "全市場掃描已暫停，進度已保存。";
        return RedirectWithFilters();
    }

    private IActionResult RedirectWithFilters() => RedirectToPage(new
    {
        MinRoce, MinScore, MinCoverage, Year, Industry, SizeBand, Sort
    });

    public static string StatusLabel(MarketScanStatus status) => status switch
    {
        MarketScanStatus.Running => "掃描中",
        MarketScanStatus.Paused => "已暫停",
        MarketScanStatus.Completed => "已完成",
        _ => "尚未開始"
    };
}


