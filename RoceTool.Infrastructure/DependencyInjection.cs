using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRoceInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<RoceDbContext>(options => options.UseSqlite(connectionString));
        services.AddHttpClient<IOfficialDataProvider, OfficialDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RoceTool/1.0 (+local research tool)");
        });
        services.AddHttpClient<IFinancialDataProvider, MopsAnnualReportProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RoceTool/1.0 (+local research tool)");
        });
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();
        services.AddScoped<IMarketScanService, MarketScanService>();
        services.AddHostedService<MarketScanWorker>();
        services.AddScoped<ICsvImportService, CsvImportService>();
        return services;
    }
}

