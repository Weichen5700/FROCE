using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;
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
        """;
        var result = MopsAnnualReportProvider.Parse(html, "2330", 2025);
        Assert.NotNull(result);
        Assert.Equal(1000, result.OperatingRevenue);
        Assert.Equal(2000, result.TotalAssets);
        Assert.Equal(200, result.OperatingIncome);
        Assert.Equal(70, result.InterestIncome);
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

        var csv = "StockNo,FiscalYear,Period,Unit,OperatingIncome,TotalAssets,CurrentLiabilities\n" +
                  "2330,2025,FY,MillionTWD,100,1000,400\n" +
                  "9999,2025,FY,ThousandTWD,1,2,1\n";
        var service = new CsvImportService(db);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var preview = await service.PreviewAsync(stream);

        Assert.Single(preview.Rows);
        Assert.Equal(100000, preview.Rows[0].OperatingIncome);
        Assert.Single(preview.Errors);
    }
}
