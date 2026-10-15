namespace RoceTool.Core;

public interface IOfficialDataProvider
{
    Task<IReadOnlyList<Stock>> FetchStockCatalogAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinancialStatement>> FetchLatestStatementsAsync(CancellationToken cancellationToken = default);
    Task ApplyValuationsAsync(IDictionary<string, Stock> stocks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyClosingPrice>> FetchDailyClosingPricesAsync(string stockNo, MarketKind market,
        bool force = false, int minimumTradingDays = 240, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AnnualPriceReference>> FetchAnnualClosingPricesAsync(string stockNo, MarketKind market,
        IEnumerable<int> fiscalYears, bool force = false, CancellationToken cancellationToken = default);
    Task<DateTime?> GetDailyPriceCachedAtAsync(string stockNo, CancellationToken cancellationToken = default);
}

public interface IAnnualReportProvider
{
    Task<FinancialStatement?> FetchAnnualAsync(string stockNo, int fiscalYear, CancellationToken cancellationToken = default);
    Task<FinancialStatement?> FetchQuarterAsync(string stockNo, int fiscalYear, int quarter, CancellationToken cancellationToken = default);
}

public interface IFinancialDataProvider : IAnnualReportProvider { }

public interface IStockAnalysisService
{
    Task EnsureCatalogAsync(bool force = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Stock>> SearchAsync(string? query, CancellationToken cancellationToken = default);
    Task<StockScreenResult> ScreenAsync(decimal minRoce = 20m, decimal minScore = 80m, decimal minCoverage = 100m, int? year = null, string? industryName = null, CompanySizeBand? sizeBand = null, decimal? minTotalAssets = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockAnalysis>> GetWatchlistAsync(CancellationToken cancellationToken = default);
    Task<StockAnalysis?> GetAnalysisAsync(string stockNo, int? year = null, CancellationToken cancellationToken = default);
    Task AddWatchlistAsync(string stockNo, CancellationToken cancellationToken = default);
    Task RemoveWatchlistAsync(string stockNo, CancellationToken cancellationToken = default);
    Task<RefreshResult> RefreshStockAsync(string stockNo, bool force = false, CancellationToken cancellationToken = default);
    Task<ScoreTemplate> GetDefaultTemplateAsync(CancellationToken cancellationToken = default);
    Task SaveTemplateAsync(ScoreTemplate template, CancellationToken cancellationToken = default);
    Task ResetTemplateAsync(CancellationToken cancellationToken = default);
    Task<DiscoveryResult> DiscoverAsync(DiscoverySettings settings, CancellationToken cancellationToken = default);
    Task<ExternalTargetVerificationResult> VerifyExternalTargetsAsync(ExternalTargetSettings settings, CancellationToken cancellationToken = default);
}

public interface IMarketScanService
{
    Task<MarketScanProgress> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<MarketScanProgress> StartAsync(int fiscalYear, CancellationToken cancellationToken = default);
    Task<MarketScanProgress> StartRangeAsync(int latestFiscalYear, int years, bool retryFailed, CancellationToken cancellationToken = default);
    Task<MarketScanProgress> PauseAsync(CancellationToken cancellationToken = default);
}

public interface ICsvImportService
{
    Task<CsvImportPreview> PreviewAsync(Stream stream, CancellationToken cancellationToken = default);
    Task<int> ImportAsync(IEnumerable<FinancialStatement> rows, bool preferred, CancellationToken cancellationToken = default);
}




