using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;
using RoceTool.Infrastructure;

namespace RoceTool.Web.Pages;

public sealed class ResearchQueueModel(RoceDbContext db, IWebHostEnvironment env) : PageModel
{
    private static readonly Regex StockNumberPattern = new(@"^\d{4}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedStatuses = ResearchQueueStatuses.All.ToHashSet(StringComparer.Ordinal);

    [BindProperty(SupportsGet = true)] public string? StockNo { get; set; }
    [BindProperty] public string Reason { get; set; } = "";
    [BindProperty] public DateOnly? NextReviewOn { get; set; }
    [BindProperty] public string Status { get; set; } = ResearchQueueStatuses.ToReview;

    public ResearchQueueState Queue { get; private set; } = new();
    public string? SuggestedCompanyName { get; private set; }
    public IReadOnlyList<string> Statuses => ResearchQueueStatuses.All;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Queue = ResearchQueueStateFile.Load(DataDirectory);
        if (StockNo is { } stockNo && StockNumberPattern.IsMatch(stockNo))
            SuggestedCompanyName = await db.Stocks.AsNoTracking().Where(x => x.StockNo == stockNo)
                .Select(x => x.Name).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        StockNo = StockNo?.Trim();
        Reason = (Reason ?? "").Trim();
        if (StockNo is null || !StockNumberPattern.IsMatch(StockNo))
            ModelState.AddModelError(nameof(StockNo), "請輸入四位數股票代號。");
        if (Reason.Length is < 1 or > 500)
            ModelState.AddModelError(nameof(Reason), "請填寫研究原因，最多 500 字。");

        var company = ModelState.IsValid
            ? await db.Stocks.AsNoTracking().Where(x => x.StockNo == StockNo).Select(x => new { x.StockNo, x.Name, x.StatementKind }).FirstOrDefaultAsync(cancellationToken)
            : null;
        if (ModelState.IsValid && company is null)
            ModelState.AddModelError(nameof(StockNo), "本機公司清單找不到此代號，請先更新公司清單。");
        else if (company is not null && company.StatementKind != StatementKind.General)
            ModelState.AddModelError(nameof(StockNo), "金融／特殊業不套用一般業 ROCE，無法加入此例外研究佇列。");
        if (!ModelState.IsValid)
        {
            Queue = ResearchQueueStateFile.Load(DataDirectory);
            SuggestedCompanyName = company?.Name;
            return Page();
        }

        var confirmedCompany = company!;
        var saved = ResearchQueueStateFile.TryUpdate(DataDirectory, state =>
        {
            var entry = state.Entries.FirstOrDefault(x => x.StockNo == confirmedCompany.StockNo);
            if (entry is null)
            {
                state.Entries.Add(new ResearchQueueEntry
                {
                    StockNo = confirmedCompany.StockNo,
                    CompanyName = confirmedCompany.Name,
                    Reason = Reason,
                    NextReviewOn = NextReviewOn,
                    Status = ResearchQueueStatuses.ToReview
                });
            }
            else
            {
                entry.CompanyName = confirmedCompany.Name;
                entry.Reason = Reason;
                entry.NextReviewOn = NextReviewOn;
                entry.UpdatedAt = DateTime.UtcNow;
            }
            return true;
        }, out var error);

        if (!saved) TempData["Error"] = error;
        else TempData["Message"] = $"{confirmedCompany.StockNo} {confirmedCompany.Name} 已加入或更新例外研究佇列；不會改變 ROCE 篩選或觀察清單。";
        return RedirectToPage("/ResearchQueue");
    }

    public IActionResult OnPostUpdate(string stockNo, string reason, DateOnly? nextReviewOn, string status)
    {
        reason = (reason ?? "").Trim();
        if (!ModelState.IsValid || !StockNumberPattern.IsMatch(stockNo) || reason.Length is < 1 or > 500 || !AllowedStatuses.Contains(status))
        {
            TempData["Error"] = "更新失敗：請確認股票代號、研究原因與狀態。";
            return RedirectToPage();
        }

        var saved = ResearchQueueStateFile.TryUpdate(DataDirectory, state =>
        {
            var entry = state.Entries.FirstOrDefault(x => x.StockNo == stockNo);
            if (entry is null) return false;
            entry.Reason = reason;
            entry.NextReviewOn = nextReviewOn;
            entry.Status = status;
            entry.UpdatedAt = DateTime.UtcNow;
            return true;
        }, out var error);
        TempData[saved ? "Message" : "Error"] = saved ? $"{stockNo} 已更新。" : error;
        return RedirectToPage();
    }

    private string DataDirectory => Path.Combine(env.ContentRootPath, "App_Data");
}
