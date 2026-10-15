using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;
using RoceTool.Web.Pages;
using DetailsModel = RoceTool.Web.Pages.Finance.DetailsModel;

namespace RoceTool.Tests;

public sealed class FinanceIncomeTests
{
    [Fact]
    public void DividendsUseEarningsYearAndIgnorePreferredShares()
    {
        var html = "<table class='hasBorder'>" + DividendRow("股東會確認", "114年年度", "1", "4.25", "0", "0", "0", "0", "0") +
            DividendRow("股東會確認", "108年年度", "1", "0", "0", "0", "0", "0.07", "0") + "</table>" +
            "<table class='hasBorder'>" + DividendRow("股東會確認", "114年年度", "1", "999", "0", "0", "0", "0", "0") + "</table>";
        var records = MopsDividendProvider.Parse(html, "2881", 2019, 2025, "https://mopsov.twse.com.tw/");
        Assert.Equal(2, records.Count);
        Assert.Equal(2025, records[0].FiscalYear);
        Assert.Equal(4.25m, records[0].CashPerShare);
        Assert.Equal(0m, records[0].StockPerShare);
        Assert.Equal(0.07m, records[1].StockReserves);
        Assert.Equal(0m, records[1].CashPerShare);
        Assert.Contains("qryType=2", MopsDividendProvider.QueryUrl("2881", 2019, 2025));
        Assert.Contains("date1=108", MopsDividendProvider.QueryUrl("2881", 2019, 2025));
    }

    [Fact]
    public void DuplicateProposalsAreNotAddedButDifferentPeriodsAre()
    {
        var html = "<table class='hasBorder'>" + DividendRow("董事會擬議", "114年年度", "1", "9", "0", "0", "0", "0", "0") +
            DividendRow("股東會確認", "114年年度", "1", "1", "0", "0", "0", "0", "0") +
            DividendRow("董事會擬議", "114年年度", "2", "2", "0", "0", "0", "0", "0") + "</table>";
        var record = Assert.Single(MopsDividendProvider.Parse(html, "2801", 2019, 2025, "https://example.test"));
        Assert.Equal(3m, record.CashPerShare);
        Assert.Contains("擬議", record.DecisionStatus);
    }

    [Fact]
    public void MissingDividendComponentRemainsUnknownAndPayoutExcludesReserves()
    {
        var html = "<table class='hasBorder'>" + DividendRow("董事會擬議", "114年年度", "1", "2", "—", "0", "0", "0", "0") + "</table>";
        var dividend = Assert.Single(MopsDividendProvider.Parse(html, "2801", 2019, 2025, "https://example.test"));
        Assert.Null(dividend.CashPerShare);
        dividend.CashReserves = 1m;
        var statement = new FinancialStatement { FiscalYear = 2025, IsAnnual = true, Eps = 4m };
        var row = FinancePageData.CompanyRow(Company("2801"), [statement], [dividend]);
        Assert.Equal(3m, row.DividendFor(2025)?.CashPerShare);
        Assert.Equal(50m, row.CashPayoutRatio(2025));
        statement.Eps = 0m;
        Assert.Null(row.CashPayoutRatio(2025));
        statement.Eps = -1m;
        Assert.Null(row.CashPayoutRatio(2025));
    }

    [Fact]
    public void MopsKeepsTotalProfitSeparateFromParentProfitAndMarksBasis()
    {
        const string html = """
            <ix:nonFraction name="ifrs-full:Assets" contextRef="AsOf20251231" scale="3">2000</ix:nonFraction>
            <ix:nonFraction name="ifrs-full:ProfitLoss" contextRef="From20250101To20251231" scale="3">120</ix:nonFraction>
            <ix:nonFraction name="ifrs-full:ProfitLossAttributableToOwnersOfParent" contextRef="From20250101To20251231" scale="3">100</ix:nonFraction>
            <ix:nonFraction name="ifrs-full:EquityAttributableToOwnersOfParent" contextRef="AsOf20251231" scale="3">900</ix:nonFraction>
            """;
        var report = MopsAnnualReportProvider.Parse(html, "2881", 2025, "https://mopsov.twse.com.tw/?REPORT_ID=C");
        Assert.NotNull(report);
        Assert.Equal(120m, report.NetIncome);
        Assert.Equal(100m, report.ParentNetIncome);
        Assert.Equal(900m, report.ParentEquity);
        Assert.Equal("Consolidated", report.ReportBasis);
        Assert.Equal("合併報表", FinancePageData.Basis(report));
    }

    [Theory]
    [InlineData("asc", "2802", "2801", "2803")]
    [InlineData("desc", "2801", "2802", "2803")]
    public void CashSortKeepsMissingLastAndZeroIsKnown(string direction, params string[] expected)
    {
        var rows = new[] { Row("2801", 2m), Row("2802", 0m), Row("2803", null) };
        Assert.Equal(expected, FinancePageData.SortRows(rows, "cash", direction, 2025).Select(x => x.Stock.StockNo));
    }

    [Fact]
    public async Task CompanyNavigationUsesSameDividendSortAndKeepsAllAnnualData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new RoceDbContext(new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options);
        await DatabaseInitializer.InitializeAsync(db);
        db.Stocks.AddRange(Company("2801"), Company("2802"), Company("2803"));
        db.FinancialStatements.Add(new FinancialStatement { StockNo = "2801", FiscalYear = 2025, IsAnnual = true, Eps = 3m });
        db.AnnualDividends.AddRange(Dividend("2801", 2m), Dividend("2802", 1m));
        await db.SaveChangesAsync();
        var page = new DetailsModel(db) { StockNo = "2802", Query = "280", Sort = "cash", Direction = "desc" };
        await page.OnGetAsync(default);
        Assert.Equal("2801", page.PreviousCompany?.StockNo);
        Assert.Equal("2803", page.NextCompany?.StockNo);
        Assert.Equal(2, page.CompanyPosition);
        Assert.Equal(7, page.HistoryYears.Count);
        Assert.Equal(1m, page.Company.DividendFor(2025)?.CashPerShare);
    }

    [Fact]
    public async Task InitializerAddsIncomeTablesAndParentFieldsToExistingDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new RoceDbContext(new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options);
        // Simulate the existing schema by creating, then dropping only the new tables and columns.
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE AnnualDividends; DROP TABLE FinancialSubsidiaryIncomes; ALTER TABLE FinancialStatements DROP COLUMN ParentNetIncome; ALTER TABLE FinancialStatements DROP COLUMN ParentEquity; ALTER TABLE FinancialStatements DROP COLUMN ReportBasis;");
        await DatabaseInitializer.InitializeAsync(db);
        await DatabaseInitializer.InitializeAsync(db);
        Assert.Empty(await db.AnnualDividends.ToListAsync());
        Assert.Empty(await db.FinancialSubsidiaryIncomes.ToListAsync());
        Assert.Empty(await db.FinancialStatements.ToListAsync());
    }

    [Fact]
    public void IncompleteDividendUpdateKeepsExistingAmountsAndSource()
    {
        var existing = Dividend("2881", 4.25m);
        existing.StockEarnings = existing.StockReserves = 0m;
        existing.SourceUrl = "https://official.test/old";
        var incoming = Dividend("2881", 4.5m);
        incoming.CashReserves = null;
        incoming.SourceUrl = "https://official.test/new";
        Assert.False(FinanceIncomeUpdate.TryUpdateDividend(existing, incoming));
        Assert.Equal(4.25m, existing.CashPerShare);
        Assert.Equal("https://official.test/old", existing.SourceUrl);
    }

    [Fact]
    public void ParentEnrichmentRejectsDifferentReportBasisAndRevision()
    {
        var old = new FinancialStatement { StockNo = "2881", FiscalYear = 2025, Eps = 8.37m, SourceUrl = "https://mopsov.twse.com.tw/?REPORT_ID=C" };
        var incoming = new FinancialStatement { StockNo = "2881", FiscalYear = 2025, Eps = 8.37m, ReportBasis = "Individual" };
        Assert.False(FinanceIncomeUpdate.CanEnrichParentFields(old, incoming));
        incoming.ReportBasis = "Consolidated";
        Assert.True(FinanceIncomeUpdate.CanEnrichParentFields(old, incoming));
        incoming.Eps = 9m;
        Assert.False(FinanceIncomeUpdate.CanEnrichParentFields(old, incoming));
    }

    private static string DividendRow(string status, string year, string sequence, params string[] amounts)
    {
        var cells = Enumerable.Repeat("0", 21).ToArray();
        cells[0] = status; cells[1] = year; cells[2] = "114/01/01~114/12/31"; cells[3] = sequence; cells[4] = "115/04/30";
        cells[10] = amounts[0]; cells[11] = amounts[1]; cells[12] = amounts[2];
        cells[14] = amounts[3]; cells[15] = amounts[4]; cells[16] = amounts[5];
        return "<tr>" + string.Join("", cells.Select(x => $"<td>{x}&nbsp;</td>")) + "</tr>";
    }
    private static Stock Company(string no) => new() { StockNo = no, Name = no, StatementKind = StatementKind.Financial, IndustryCode = "17" };
    private static AnnualDividend Dividend(string no, decimal cash) => new() { StockNo = no, FiscalYear = 2025, CashEarnings = cash, CashReserves = 0m };
    private static FinancialCompanyRow Row(string no, decimal? cash) => FinancePageData.CompanyRow(Company(no), [],
        cash is decimal value ? [Dividend(no, value)] : []);
}
