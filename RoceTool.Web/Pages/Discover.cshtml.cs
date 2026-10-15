using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages;

public sealed class DiscoverModel(IStockAnalysisService service, IWebHostEnvironment env) : PageModel
{
    [BindProperty] public string TargetsInput { get; set; } = ""; // 格式: 2330,來源,筆記\n2454,來源,筆記
    [BindProperty] public decimal MinRoce { get; set; } = 20m;
    [BindProperty] public decimal? MinScore { get; set; }

    public ExternalTargetSettings Settings { get; private set; } = new();
    public ExternalTargetVerificationResult? Result { get; private set; }
    public IReadOnlyList<ExternalTargetVerificationItem> Items { get; private set; } = [];
    public int UnverifiedCount => Result is null ? 0 : Math.Max(0, Result.TotalTargets - Result.VerifiedCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(env.ContentRootPath, "App_Data");
        Settings = ExternalTargetStateFile.Load(dataDirectory);
        MinRoce = Settings.MinRoce;
        MinScore = Settings.MinScore;

        // 將現有標的轉換為輸入格式
        TargetsInput = string.Join("\n", Settings.Targets.Select(t =>
            $"{t.StockNo},{t.Source},{t.Note}"));

        await Task.CompletedTask;
    }

    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        MinRoce = Math.Clamp(MinRoce, 0m, 1000m);
        if (MinScore is decimal score) MinScore = Math.Clamp(score, 0m, 100m);

        var settings = new ExternalTargetSettings
        {
            MinRoce = MinRoce,
            MinScore = MinScore,
            Targets = ParseTargetsInput(TargetsInput)
        };

        Result = await service.VerifyExternalTargetsAsync(settings, cancellationToken);
        settings.LastVerifiedAt = DateTime.UtcNow;
        var dataDirectory = Path.Combine(env.ContentRootPath, "App_Data");
        ExternalTargetStateFile.Save(dataDirectory, settings);

        Items = Result.Items.OrderByDescending(x => x.MeetsRoceCriteria && x.MeetsScoreCriteria)
            .ThenByDescending(x => x.AnnualRoce ?? 0).ToList();
        Settings = settings;
        return Page();
    }

    private static List<ExternalTarget> ParseTargetsInput(string input)
    {
        var targets = new List<ExternalTarget>();
        if (string.IsNullOrWhiteSpace(input)) return targets;

        foreach (var line in input.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;

            targets.Add(new ExternalTarget
            {
                StockNo = parts[0].Trim(),
                Source = parts.Length > 1 ? parts[1].Trim() : "",
                Note = parts.Length > 2 ? string.Join(",", parts.Skip(2)).Trim() : ""
            });
        }

        return targets;
    }
}
