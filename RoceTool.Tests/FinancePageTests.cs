using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;
using RoceTool.Web.Pages;
using FinancialDetailsModel = RoceTool.Web.Pages.Finance.DetailsModel;

namespace RoceTool.Tests;

public sealed class FinancePageTests
{
    [Theory]
    [InlineData("asc", "2802", "2801", "2803")]
    [InlineData("desc", "2801", "2802", "2803")]
    public void EpsSortKeepsMissingYearLastInBothDirections(string direction, params string[] expected)
    {
        var rows = new[]
        {
            FinancePageData.CompanyRow(Company("2801"), [Report("2801", 2025, 4m)]),
            FinancePageData.CompanyRow(Company("2802"), [Report("2802", 2025, -1m)]),
            // A high EPS in a different year must not replace the comparison year's missing value.
            FinancePageData.CompanyRow(Company("2803"), [Report("2803", 2024, 100m)])
        };
        var sorted = FinancePageData.SortRows(rows, "eps", direction, 2025);
        Assert.Equal(expected, sorted.Select(x => x.Stock.StockNo));
    }

    [Fact]
    public void FinanceSourcesPreferEpsAndPreserveExplicitUserPreference()
    {
        var missingEps = Report("2801", 2025, null);
        missingEps.OperatingIncome = 100m;
        missingEps.CurrentLiabilities = 10m;
        missingEps.TotalAssets = 1000m;
        var withEps = Report("2801", 2025, 2m);
        withEps.SourceKind = SourceKind.FinMind;
        var selected = FinancePageData.CompanyRow(Company("2801"), [missingEps, withEps]);
        Assert.Same(withEps, selected.StatementFor(2025));

        missingEps.IsPreferred = true;
        selected = FinancePageData.CompanyRow(Company("2801"), [missingEps, withEps]);
        Assert.Same(missingEps, selected.StatementFor(2025));
    }

    [Fact]
    public void EpsChangeRequiresAdjacentCalendarYearsAndKeepsZeroEps()
    {
        var row = FinancePageData.CompanyRow(Company("2801"),
            [Report("2801", 2025, 3m), Report("2801", 2023, 0m), Report("2801", 2022, -1m)]);
        Assert.Null(row.EpsChange(2025));
        Assert.Equal(1m, row.EpsChange(2023));
        Assert.Equal(3, row.EpsYearCount(FinancePageData.HistoryYears(2025)));
    }

    [Fact]
    public async Task SearchDoesNotChangeTheComparisonYearOrUseOlderEps()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        db.Stocks.AddRange(Company("2801"), Company("2802"), Company("2330", "24"));
        db.FinancialStatements.AddRange(Report("2801", 2025, 2m), Report("2802", 2019, 50m));
        await db.SaveChangesAsync();

        var page = new FinanceModel(db) { Query = "2802", Sort = "eps", Direction = "desc" };
        await page.OnGetAsync(default);
        Assert.Equal(2025, page.ReferenceYear);
        Assert.Equal(1, page.CompanyCount);
        Assert.Null(page.Items[0].StatementFor(page.ReferenceYear));
        Assert.Equal(0, page.ComparableEpsCount);
        Assert.Equal("asc", page.NextDirection("eps"));
        Assert.Equal("descending", page.AriaSort("eps"));
        Assert.Equal("asc", page.NextDirection("stock"));
    }

    [Fact]
    public async Task DetailKeepsMissingSelectedYearAndShowsSevenCalendarYears()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        db.Stocks.AddRange(Company("2801"), Company("2802"));
        db.FinancialStatements.AddRange(Report("2801", 2025, 2m), Report("2802", 2019, 50m));
        await db.SaveChangesAsync();

        var page = new FinancialDetailsModel(db) { StockNo = "2802", Year = 2025, Query = "280", Sort = "roe", Direction = "asc" };
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.Null(page.SelectedStatement);
        Assert.Equal(new[] { 2025, 2024, 2023, 2022, 2021, 2020, 2019 }, page.HistoryYears);
        Assert.Equal("280", page.Query);
        Assert.Equal("roe", page.Sort);
        Assert.Equal("asc", page.Direction);

        page.Year = 2019;
        Assert.Equal(50m, page.SelectedStatement?.Eps);
    }

    [Fact]
    public async Task FinancialDetailRejectsCompaniesOutsideOfficialIndustry17()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        db.Stocks.Add(Company("2330", "24"));
        await db.SaveChangesAsync();
        var page = new FinancialDetailsModel(db) { StockNo = "2330" };
        Assert.IsType<NotFoundResult>(await page.OnGetAsync(default));
    }

    [Fact]
    public void AnnualSourceLinksRejectExecutableUrls()
    {
        var report = Report("2801", 2025, 2m);
        report.SourceUrl = "javascript:alert(1)";
        Assert.Null(FinancePageData.SourceLink(report));
        report.SourceUrl = "https://mops.twse.com.tw/report";
        Assert.Equal(report.SourceUrl, FinancePageData.SourceLink(report));
    }

    private static RoceDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options);

    private static Stock Company(string stockNo, string industryCode = "17") => new()
    {
        StockNo = stockNo, Name = $"Company {stockNo}", IndustryCode = industryCode,
        StatementKind = StatementKind.Financial, IsSupported = true
    };

    private static FinancialStatement Report(string stockNo, int year, decimal? eps) => new()
    {
        StockNo = stockNo, FiscalYear = year, IsAnnual = true, Eps = eps,
        SourceKind = SourceKind.Mops, SourceName = "MOPS", FetchedAt = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)
    };
}
