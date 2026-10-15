using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;
using RoceTool.Web.Pages;
using DetailModel = RoceTool.Web.Pages.Finance.DetailsModel;

namespace RoceTool.Tests;

public sealed class DividendPriceTests
{
    [Fact]
    public void CashYieldAndTargetPriceDoNotIncludeStockDividend()
    {
        var row = Row("2881", 160m, 4.25m, 0.25m);
        Assert.Equal(2.65625m, row.CashYield(2025));
        Assert.Equal(85m, row.CashTargetPrice(2025));
        Assert.Equal(4.5m, row.DividendFaceValueTotal(2025));
        Assert.False(row.MeetsCashTarget(2025));
        Assert.Equal(0.64853556485355648535564854m, DividendPriceCalculator.YieldPercent(0.155m, 23.9m));
        Assert.Equal(3.1m, DividendPriceCalculator.TargetPrice(0.155m));
    }

    [Fact]
    public void MissingInvalidAndZeroInputsAreDistinct()
    {
        Assert.Equal(0m, DividendPriceCalculator.YieldPercent(0m, 50m));
        Assert.Null(DividendPriceCalculator.TargetPrice(0m));
        Assert.Null(DividendPriceCalculator.YieldPercent(null, 50m));
        Assert.Null(DividendPriceCalculator.YieldPercent(-1m, 50m));
        Assert.Null(DividendPriceCalculator.YieldPercent(2m, 0m));
        Assert.Null(DividendPriceCalculator.TargetPrice(2m, -5m));
        var row = Row("2801", 50m, 3m);
        row.Stock.QuoteDate = "";
        Assert.Null(row.CashYield(2025));
        Assert.False(row.MeetsCashTarget(2025));
        Assert.Equal(60m, row.CashTargetPrice(2025));
    }

    [Fact]
    public void OldOrFutureQuotesCannotPassFilterAndRoundingDoesNotChangeThreshold()
    {
        var row = Row("2801", 50.01m, 2.5m);
        Assert.Equal("5.00", row.CashYield(2025)?.ToString("F2"));
        Assert.False(row.MeetsCashTarget(2025));
        row.Stock.ClosePrice = 50m;
        Assert.True(row.MeetsCashTarget(2025));
        row.Stock.QuoteDate = FinancePageData.Today.AddDays(-8).ToString("yyyy-MM-dd");
        Assert.Equal(5m, row.CashYield(2025));
        Assert.False(row.MeetsCashTarget(2025));
        row.Stock.QuoteDate = FinancePageData.Today.AddDays(1).ToString("yyyy-MM-dd");
        Assert.Null(row.CashYield(2025));
        Assert.False(row.MeetsCashTarget(2025));
    }

    [Theory]
    [InlineData("asc", "2801", "2802", "2803")]
    [InlineData("desc", "2802", "2801", "2803")]
    public void YieldSortingKeepsMissingLast(string direction, params string[] expected)
    {
        var rows = new[] { Row("2801", 50m, 2m), Row("2802", 50m, 3m), Row("2803", null, 100m) };
        Assert.Equal(expected, FinancePageData.SortRows(rows, "yield", direction, 2025).Select(x => x.Stock.StockNo));
    }

    [Fact]
    public async Task TargetFilterAndCompanyNavigationUseSameEligibleRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await DatabaseInitializer.InitializeAsync(db);
        var rows = new[] { Row("2801", 50m, 3m), Row("2802", 50m, 4m), Row("2803", 100m, 3m), Row("2804", 10m, 5m) };
        rows[3].Stock.QuoteDate = FinancePageData.Today.AddDays(-8).ToString("yyyy-MM-dd");
        db.Stocks.AddRange(rows.Select(x => x.Stock));
        db.AnnualDividends.AddRange(rows.SelectMany(x => x.Dividends.Values));
        db.FinancialStatements.Add(new FinancialStatement { StockNo = "2801", FiscalYear = 2025, IsAnnual = true, Eps = 2m });
        await db.SaveChangesAsync();
        var list = new FinanceModel(db) { OnlyTarget = true, Sort = "yield", Direction = "desc" };
        await list.OnGetAsync(default);
        Assert.Equal(new[] { "2802", "2801" }, list.Items.Select(x => x.Stock.StockNo));
        Assert.Equal(2, list.TargetCount);
        var detail = new DetailModel(db) { StockNo = "2802", OnlyTarget = true, Sort = "yield", Direction = "desc" };
        await detail.OnGetAsync(default);
        Assert.Equal("2801", detail.NextCompany?.StockNo);
        Assert.Null(detail.PreviousCompany);
        Assert.Equal(2, detail.ComparisonCount);
    }

    [Fact]
    public async Task QuoteRefreshUpdatesOnlyFinancialQuoteFieldsAndKeepsNewerExistingPrices()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await DatabaseInitializer.InitializeAsync(db);
        var stock = Row("2881", 99m, 4m).Stock;
        stock.PeRatio = 12m; stock.PbRatio = 1m; stock.DividendYield = 2m;
        var other = Row("2330", 1000m, 5m).Stock;
        other.IndustryCode = "24"; other.StatementKind = StatementKind.General;
        db.Stocks.AddRange(stock, other);
        db.Watchlist.Add(new WatchlistItem { StockNo = "2881" });
        await db.SaveChangesAsync();
        var today = FinancePageData.Today;
        var rocDate = $"{today.Year - 1911:D3}{today.Month:D2}{today.Day:D2}";
        using var client = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var content = path.EndsWith("STOCK_DAY_ALL", StringComparison.Ordinal)
                ? $"[{{\"Code\":\"2881\",\"Date\":\"{rocDate}\",\"ClosingPrice\":\"100\",\"Change\":\"1\"}},{{\"Code\":\"2330\",\"Date\":\"{rocDate}\",\"ClosingPrice\":\"2000\"}}]"
                : path.EndsWith("BWIBBU_ALL", StringComparison.Ordinal)
                ? "[{\"Code\":\"2881\",\"PEratio\":\"999\",\"PBratio\":\"999\",\"DividendYield\":\"99\"}]" : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        }));
        var service = new FinanceQuoteRefreshService(db, new OfficialDataProvider(client, db));
        var result = await service.RefreshAsync();
        Assert.Equal(1, result.Updated);
        Assert.Equal(100m, stock.ClosePrice);
        Assert.Equal(today.ToString("yyyy-MM-dd"), stock.QuoteDate);
        Assert.Equal(12m, stock.PeRatio);
        Assert.Equal(1m, stock.PbRatio);
        Assert.Equal(2m, stock.DividendYield);
        Assert.Equal(1000m, other.ClosePrice);
        Assert.Single(await db.Watchlist.ToListAsync());
        rocDate = $"{today.AddDays(-1).Year - 1911:D3}{today.AddDays(-1).Month:D2}{today.AddDays(-1).Day:D2}";
        stock.QuoteDate = today.ToString("yyyy-MM-dd");
        stock.ClosePrice = 110m;
        await db.SaveChangesAsync();
        result = await service.RefreshAsync();
        Assert.Equal(1, result.Retained);
        Assert.Equal(110m, stock.ClosePrice);
        // An invalid future date must not permanently block a valid official quote.
        stock.QuoteDate = today.AddDays(1).ToString("yyyy-MM-dd");
        await db.SaveChangesAsync();
        result = await service.RefreshAsync();
        Assert.Equal(1, result.Updated);
        Assert.Equal(100m, stock.ClosePrice);
        Assert.Equal(today.AddDays(-1).ToString("yyyy-MM-dd"), stock.QuoteDate);
    }

    private static RoceDbContext Context(SqliteConnection connection) => new(new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options);
    private static FinancialCompanyRow Row(string no, decimal? price, decimal cash, decimal stock = 0m) => FinancePageData.CompanyRow(
        new Stock { StockNo = no, IndustryCode = "17", StatementKind = StatementKind.Financial, ClosePrice = price,
            QuoteDate = FinancePageData.Today.ToString("yyyy-MM-dd"), QuoteSource = "TWSE STOCK_DAY_ALL" }, [],
        [new AnnualDividend { StockNo = no, FiscalYear = 2025, CashEarnings = cash, CashReserves = 0m, StockEarnings = stock, StockReserves = 0m }]);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
