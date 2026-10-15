using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Web.Pages.Finance;

public sealed class DetailsModel(RoceDbContext db, MopsDividendProvider? dividendProvider = null,
    FinanceResearchStore? research = null) : PageModel
{
    [BindProperty(SupportsGet = true)] public string StockNo { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public string? Direction { get; set; }
    [BindProperty(SupportsGet = true)] public bool OnlyTarget { get; set; }
    [BindProperty(SupportsGet = true)] public int? ScenarioYear { get; set; }
    [BindProperty(SupportsGet = true)] public decimal ScenarioChange { get; set; }
    [BindProperty(SupportsGet = true)] public decimal TargetYield { get; set; } = 5m;

    public FinancialCompanyRow Company { get; private set; } = null!;
    public int ReferenceYear { get; private set; }
    public IReadOnlyList<int> HistoryYears { get; private set; } = [];
    public FinancialStatement? SelectedStatement => Year is int year ? Company.StatementFor(year) : null;
    public IReadOnlyList<FinancialSubsidiaryIncome> SubsidiaryIncomes { get; private set; } = [];
    public Stock? PreviousCompany { get; private set; }
    public Stock? NextCompany { get; private set; }
    public int CompanyPosition { get; private set; }
    public int ComparisonCount { get; private set; }
    public FinanceResearchCompany? Research => research?.Companies.GetValueOrDefault(StockNo);
    public string? ResearchError => research?.Error;
    public DateOnly? ResearchVerifiedOn => research?.VerifiedOn;
    public bool ScenarioInputValid { get; private set; }
    public IReadOnlyList<DividendScenario> Scenarios { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var stock = await FinancePageData.Companies(db).FirstOrDefaultAsync(x => x.StockNo == StockNo, cancellationToken);
        if (stock is null) return NotFound();

        var statements = await db.FinancialStatements.AsNoTracking()
            .Where(x => x.StockNo == StockNo && x.IsAnnual).ToListAsync(cancellationToken);
        var dividends = await db.AnnualDividends.AsNoTracking().Where(x => x.StockNo == StockNo).ToListAsync(cancellationToken);
        Company = FinancePageData.CompanyRow(stock, statements, dividends);
        ReferenceYear = await FinancePageData.ReferenceYearAsync(db, cancellationToken);
        HistoryYears = FinancePageData.HistoryYears(ReferenceYear);
        ScenarioYear ??= ReferenceYear;
        Scenarios = [];
        ScenarioInputValid = ModelState.IsValid && HistoryYears.Contains(ScenarioYear.Value) &&
            ScenarioChange is >= -100m and <= 100m &&
            TargetYield is >= FinanceResearchCalculator.MinimumTargetYield and <= FinanceResearchCalculator.MaximumTargetYield;
        if (ScenarioInputValid)
        {
            var baseCash = Company.DividendFor(ScenarioYear.Value)?.CashPerShare;
            var price = Company.HasDatedQuote() ? stock.ClosePrice : null;
            Scenarios = new[] { -20m, 0m, 20m, ScenarioChange }.Distinct()
                .Select(change => FinanceResearchCalculator.DividendScenario(baseCash, price, change, TargetYield)).ToArray();
        }
        Year ??= ReferenceYear;
        Query = Query?.Trim();
        Sort = FinancePageData.NormalizeSort(Sort);
        Direction = FinancePageData.NormalizeDirection(Direction, Sort);
        SubsidiaryIncomes = await db.FinancialSubsidiaryIncomes.AsNoTracking().Where(x => x.StockNo == StockNo)
            .OrderByDescending(x => x.FiscalYear).ThenBy(x => x.EntityName).ToListAsync(cancellationToken);
        var comparison = FinancePageData.Companies(db);
        if (!string.IsNullOrWhiteSpace(Query))
            comparison = comparison.Where(x => x.StockNo.Contains(Query) || x.Name.Contains(Query) || x.IndustryName.Contains(Query));
        var stocks = await comparison.ToListAsync(cancellationToken);
        var nos = stocks.Select(x => x.StockNo).ToArray();
        var comparisonStatements = await db.FinancialStatements.AsNoTracking()
            .Where(x => nos.Contains(x.StockNo) && x.IsAnnual && (Sort == "coverage" || x.FiscalYear == ReferenceYear)).ToListAsync(cancellationToken);
        var comparisonDividends = await db.AnnualDividends.AsNoTracking()
            .Where(x => nos.Contains(x.StockNo) && x.FiscalYear == ReferenceYear).ToListAsync(cancellationToken);
        var reportsByStock = comparisonStatements.ToLookup(x => x.StockNo);
        var dividendsByStock = comparisonDividends.ToLookup(x => x.StockNo);
        var sorted = FinancePageData.SortRows(stocks.Select(x => FinancePageData.CompanyRow(x,
            reportsByStock[x.StockNo], dividendsByStock[x.StockNo])).Where(x => !OnlyTarget || x.MeetsCashTarget(ReferenceYear)), Sort, Direction, ReferenceYear).ToList();
        ComparisonCount = sorted.Count;
        var index = sorted.FindIndex(x => x.Stock.StockNo == StockNo);
        CompanyPosition = index + 1;
        if (index > 0) PreviousCompany = sorted[index - 1].Stock;
        if (index >= 0 && index + 1 < sorted.Count) NextCompany = sorted[index + 1].Stock;
        return Page();
    }

    public static string Signed(decimal? value) => value?.ToString("+0.00;-0.00;0.00") ?? "—";

    public async Task<IActionResult> OnPostDividendsAsync(CancellationToken cancellationToken)
    {
        if (await FinancePageData.Companies(db).AllAsync(x => x.StockNo != StockNo, cancellationToken)) return NotFound();
        if (dividendProvider is null) throw new InvalidOperationException("股利資料服務未設定。");
        try
        {
            var year = await FinancePageData.ReferenceYearAsync(db, cancellationToken);
            var records = await dividendProvider.FetchAsync(StockNo, year - 6, year, cancellationToken);
            var retained = 0;
            foreach (var record in records)
            {
                var existing = await db.AnnualDividends.SingleOrDefaultAsync(x => x.StockNo == StockNo && x.FiscalYear == record.FiscalYear, cancellationToken);
                if (existing is null) db.AnnualDividends.Add(record);
                else if (!FinanceIncomeUpdate.TryUpdateDividend(existing, record)) retained++;
            }
            await db.SaveChangesAsync(cancellationToken);
            TempData[records.Count > 0 ? "Message" : "Error"] = records.Count > 0
                ? $"取得 {records.Count} 個盈餘年度的官方普通股股利，更新 {records.Count - retained} 年。" +
                    (retained > 0 ? $" {retained} 年新回應有缺漏，保留原資料。" : "")
                : "官方回應沒有可辨識的股利列，既有資料已保留。";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        { TempData["Error"] = "官方股利查詢暫時失敗，既有資料已保留，請稍後再試。"; }
        return RedirectToPage(new { stockNo = StockNo, year = Year, query = Query, sort = Sort, direction = Direction, onlyTarget = OnlyTarget,
            scenarioYear = ScenarioYear, scenarioChange = ScenarioChange, targetYield = TargetYield });
    }
}
