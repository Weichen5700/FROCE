using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;
using System.Net;
using System.Text;

namespace RoceTool.Tests;

public sealed class DataProviderTests
{
    [Fact]
    public void ParsesMopsIxbrlWithExactCurrentYearContexts()
    {
        const string html = """
        <ix:nonFraction name="ifrs-full:Revenue" contextRef="From20250101To20251231" scale="3">1,000</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:ProfitLossFromOperatingActivities" contextRef="From20250101To20251231" scale="3">200</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:ProfitLoss" contextRef="From20250101To20251231" scale="3">150</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:RevenueFromInterest" contextRef="From20250101To20251231" scale="3">70</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:CurrentAssets" contextRef="AsOf20251231" scale="3">600</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:Assets" contextRef="AsOf20251231" scale="3">2,000</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:CurrentLiabilities" contextRef="AsOf20251231" scale="3">500</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:Liabilities" contextRef="AsOf20251231" scale="3">800</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:Equity" contextRef="AsOf20251231" scale="3">1,200</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:BasicEarningsLossPerShare" contextRef="From20250101To20251231">3.00</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:CashFlowsFromUsedInOperatingActivities" contextRef="From20250101To20251231" scale="3">500</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities" contextRef="From20250101To20251231" scale="3" sign="-">120</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:PurchaseOfIntangibleAssetsClassifiedAsInvestingActivities" contextRef="From20250101To20251231" scale="3" sign="-">20</ix:nonFraction>
        """;
        var result = MopsAnnualReportProvider.Parse(html, "2330", 2025);
        Assert.NotNull(result);
        Assert.Equal(1000, result.OperatingRevenue);
        Assert.Equal(2000, result.TotalAssets);
        Assert.Equal(200, result.OperatingIncome);
        Assert.Equal(70, result.InterestIncome);
        Assert.Equal(500, result.OperatingCashFlow);
        Assert.Equal(-120, result.CapexPpe);
        Assert.Equal(-20, result.CapexIntangibles);
        Assert.Equal(50000, result.WeightedAverageShares);
        Assert.Equal("NetIncome/EPS 推估", result.WeightedAverageSharesSource);
    }

    [Fact]
    public async Task CsvPreviewNormalizesUnitsAndRejectsUnknownStocks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Stocks.Add(new Stock { StockNo = "2330", Name = "台積電" });
        await db.SaveChangesAsync();

        var csv = "StockNo,FiscalYear,Period,Unit,OperatingIncome,TotalAssets,CurrentLiabilities,OperatingCashFlow,CapexPpe,WeightedAverageShares,Eps\n" +
                  "2330,2025,FY,MillionTWD,100,1000,400,500,-120,50000,3\n" +
                  "9999,2025,FY,ThousandTWD,1,2,1\n";
        var service = new CsvImportService(db);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var preview = await service.PreviewAsync(stream);

        Assert.Single(preview.Rows);
        Assert.Equal(100000, preview.Rows[0].OperatingIncome);
        Assert.Equal(500000, preview.Rows[0].OperatingCashFlow);
        Assert.Equal(-120000, preview.Rows[0].CapexPpe);
        Assert.Equal(50000, preview.Rows[0].WeightedAverageShares);
        Assert.Equal(3, preview.Rows[0].Eps);
        Assert.Single(preview.Errors);
    }

    [Fact]
    public async Task FetchesAnnualClosingPriceFromTwseHistoryAndUsesYearEndClose()
    {
        string? requestedUrl = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUrl = request.RequestUri?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"data":[["113/12/30","","","","","","100"],["113/12/31","","","","","","101.5"]]}
                    """)
            };
        }));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options;
        await using var db = new RoceDbContext(options);
        await DatabaseInitializer.InitializeAsync(db);
        var provider = new OfficialDataProvider(httpClient, db);

        var references = await provider.FetchAnnualClosingPricesAsync("2330", MarketKind.Listed, [2024]);
        var reference = Assert.Single(references);

        Assert.Contains("/rwd/zh/afterTrading/STOCK_DAY", requestedUrl);
        Assert.Equal(new DateOnly(2024, 12, 31), reference.TradingDate);
        Assert.Equal(101.5m, reference.Close);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
