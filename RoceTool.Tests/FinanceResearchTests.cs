using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoceTool.Core;
using RoceTool.Infrastructure;
using RoceTool.Web;
using FinancialDetailsModel = RoceTool.Web.Pages.Finance.DetailsModel;

namespace RoceTool.Tests;

public sealed class FinanceResearchTests
{
    [Fact]
    public void DividendCutShowsFourPercentAtHistoricalFivePercentPrice()
    {
        var scenario = FinanceResearchCalculator.DividendScenario(4.25m, 160.50m, -20m, 5m);
        Assert.Equal(3.4m, scenario.CashPerShare);
        Assert.Equal(68m, scenario.TargetPrice);
        Assert.Equal(4m, scenario.YieldAtHistoricalFivePercentPrice);
        Assert.Equal(3.4m / 160.50m * 100m, scenario.YieldPercent);
    }

    [Fact]
    public void ScenariosKeepUnknownZeroAndInvalidPricesDistinct()
    {
        var missing = FinanceResearchCalculator.DividendScenario(null, 100m, 0m, 5m);
        Assert.Null(missing.CashPerShare);
        Assert.Null(missing.TargetPrice);
        var zero = FinanceResearchCalculator.DividendScenario(0m, 100m, 0m, 5m);
        Assert.Equal(0m, zero.CashPerShare);
        Assert.Equal(0m, zero.YieldPercent);
        Assert.Null(zero.TargetPrice);
        Assert.Null(zero.YieldAtHistoricalFivePercentPrice);
        var cutAll = FinanceResearchCalculator.DividendScenario(4m, null, -100m, 5m);
        Assert.Equal(0m, cutAll.CashPerShare);
        Assert.Null(cutAll.YieldPercent);
        Assert.Equal(0m, cutAll.YieldAtHistoricalFivePercentPrice);
    }

    [Theory]
    [InlineData(-101, 5)]
    [InlineData(101, 5)]
    [InlineData(0, 0)]
    [InlineData(0, 31)]
    public void ScenarioRejectsInvalidAssumptions(int change, int target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceResearchCalculator.DividendScenario(4m, 100m, change, target));

    [Fact]
    public void TargetYieldChangesPriceWithoutChangingDividendOrHistoricalBaseline()
    {
        var scenario = FinanceResearchCalculator.DividendScenario(4m, 80m, 25m, 10m);
        Assert.Equal(5m, scenario.CashPerShare);
        Assert.Equal(50m, scenario.TargetPrice);
        Assert.Equal(6.25m, scenario.YieldPercent);
        Assert.Equal(6.25m, scenario.YieldAtHistoricalFivePercentPrice);
    }

    [Fact]
    public void ResearchKeepsUnknownPublicationAndNullObservationWithoutInventingZero()
    {
        var parsed = FinanceResearchStore.Parse(Fixture().ToJsonString());
        Assert.Null(parsed.Companies[0].Observations[0].PublishedOn);
        Assert.Null(parsed.Companies[0].Observations[0].Value);
    }

    [Theory]
    [InlineData("sourceUrl", "javascript:alert(1)")]
    [InlineData("sourceUrl", "https://name:password@example.com/a")]
    [InlineData("sourceVersion", "")]
    [InlineData("accountingRegime", "")]
    [InlineData("basis", "")]
    [InlineData("period", "2024FY")]
    [InlineData("asOfDate", "2025-06-30")]
    public void ResearchRejectsUntraceableOrMismatchedObservations(string field, string value)
    {
        var json = Fixture();
        json["companies"]![0]!["observations"]![0]![field] = value;
        Assert.Throws<InvalidDataException>(() => FinanceResearchStore.Parse(json.ToJsonString()));
    }

    [Fact]
    public void ResearchRetainsDifferentSourceVersionsForTheSameYear()
    {
        var json = Fixture();
        var observations = json["companies"]![0]!["observations"]!.AsArray();
        var revised = observations[0]!.DeepClone();
        revised["sourceVersion"] = "2026 新期簡報比較值";
        revised["value"] = 132.66m;
        observations[0]!["value"] = 133.01m;
        observations.Add(revised);
        var parsed = FinanceResearchStore.Parse(json.ToJsonString());
        Assert.Equal(2, parsed.Companies[0].Observations.Count);
        Assert.Equal(133.01m, parsed.Companies[0].Observations[0].Value);
        Assert.Equal(132.66m, parsed.Companies[0].Observations[1].Value);
    }

    [Fact]
    public void DividendSourceDateAndUnknownDateRemainSeparateFromFinancialPeriodEnd()
    {
        var json = Fixture();
        var item = json["companies"]![0]!["observations"]![0]!;
        item["dateBasis"] = "sourceDate";
        item["asOfDate"] = "2026-06-12";
        Assert.Equal(new DateOnly(2026, 6, 12), FinanceResearchStore.Parse(json.ToJsonString()).Companies[0].Observations[0].AsOfDate);
        item["dateBasis"] = "unknown";
        item["asOfDate"] = null;
        Assert.Null(FinanceResearchStore.Parse(json.ToJsonString()).Companies[0].Observations[0].AsOfDate);
        item["value"] = 5m;
        Assert.Throws<InvalidDataException>(() => FinanceResearchStore.Parse(json.ToJsonString()));
    }

    [Fact]
    public void BridgeMustReconcileAndDoesNotReplaceEps()
    {
        var json = Fixture();
        json["companies"]![0]!["bridges"] = JsonSerializer.SerializeToNode(new[] { new FinanceEarningsBridge {
            Period = "2025FY", Entity = "測試金控", Unit = "新臺幣百萬元", ReportedProfit = 100m, Adjustment = 20m,
            AdjustmentLabel = "稅後處分利益", Result = 120m, ResultLabel = "對保留盈餘影響",
            Basis = "合併本期淨利", AccountingRegime = "IFRS 17 / IFRS 9", SourceName = "官方法說",
            SourceUrl = "https://example.com/report.pdf", AsOfDate = new DateOnly(2025, 12, 31)
        } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(FinanceResearchStore.Parse(json.ToJsonString()).Companies[0].Bridges[0].Reconciles);
        json["companies"]![0]!["bridges"]![0]!["result"] = 119m;
        Assert.Throws<InvalidDataException>(() => FinanceResearchStore.Parse(json.ToJsonString()));
    }

    [Fact]
    public void MissingResearchFilesFailVisibly()
    {
        var store = new FinanceResearchStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), NullLogger<FinanceResearchStore>.Instance);
        Assert.NotNull(store.Error);
        Assert.Empty(store.Companies);
    }

    [Fact]
    public void PublishedResearchIsPackagedAndCoversFourCompaniesAndTwoAnnualPeriods()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ResearchData"), "*.json"))
            FinanceResearchStore.Parse(File.ReadAllText(file));
        var store = new FinanceResearchStore(NullLogger<FinanceResearchStore>.Instance);
        Assert.Null(store.Error);
        Assert.Equal(new[] { "2881", "2882", "2884", "2885" }, store.Companies.Keys.Order().ToArray());
        foreach (var company in store.Companies.Values)
        {
            Assert.True(company.AnnualYears >= 2);
            Assert.NotEmpty(company.Businesses);
            foreach (var category in new[] { "business", "quality", "capital" }) Assert.NotEmpty(company.ForCategory(category));
        }
    }

    [Fact]
    public async Task DetailScenarioUsesChosenDividendYearAndPreservesStoredData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new RoceDbContext(new DbContextOptionsBuilder<RoceDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Stocks.Add(new Stock { StockNo = "2881", IndustryCode = "17", StatementKind = StatementKind.Financial,
            ClosePrice = 100m, QuoteDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).ToString("yyyy-MM-dd"), QuoteSource = "TWSE" });
        db.FinancialStatements.Add(new FinancialStatement { StockNo = "2881", FiscalYear = 2025, IsAnnual = true, Eps = 8m });
        db.AnnualDividends.AddRange(new AnnualDividend { StockNo = "2881", FiscalYear = 2024, CashEarnings = 2m, CashReserves = 1m },
            new AnnualDividend { StockNo = "2881", FiscalYear = 2025, CashEarnings = 5m, CashReserves = 0m });
        await db.SaveChangesAsync();
        var page = new FinancialDetailsModel(db) { StockNo = "2881", ScenarioYear = 2024, ScenarioChange = -50m, TargetYield = 6m };
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.True(page.ScenarioInputValid);
        Assert.Equal(1.5m, page.Scenarios.Single(x => x.ChangePercent == -50m).CashPerShare);
        Assert.Equal(25m, page.Scenarios.Single(x => x.ChangePercent == -50m).TargetPrice);
        Assert.Equal(5m, (await db.AnnualDividends.SingleAsync(x => x.FiscalYear == 2025)).CashEarnings);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), x => x.State is EntityState.Modified or EntityState.Added or EntityState.Deleted);
        page.TargetYield = 0;
        await page.OnGetAsync(default);
        Assert.False(page.ScenarioInputValid);
        Assert.Empty(page.Scenarios);
        page.TargetYield = 5m;
        page.ModelState.AddModelError(nameof(page.TargetYield), "invalid decimal");
        await page.OnGetAsync(default);
        Assert.False(page.ScenarioInputValid);
    }

    private static JsonObject Fixture() => JsonNode.Parse("""
    {"schemaVersion":1,"verifiedOn":"2026-10-15","companies":[{"stockNo":"2881","name":"測試金控",
    "businessSummary":"銀行與壽險","businesses":["銀行"],"risks":[],"observations":[{
    "category":"capital","period":"2025FY","metric":"資本適足率","value":null,"unit":"%","entity":"測試金控",
    "basis":"金控監理口徑","accountingRegime":"該期監理制度","meaning":"觀察資本緩衝，不代表已決議股利",
    "sourceName":"官方法說","sourceUrl":"https://example.com/report.pdf","sourceVersion":"原期法說揭露",
    "publishedOn":null,"asOfDate":"2025-12-31"}],"bridges":[]}]}
    """)!.AsObject();
}
