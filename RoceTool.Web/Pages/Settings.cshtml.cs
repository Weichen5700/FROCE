using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages;

public sealed class SettingsModel(IStockAnalysisService service) : PageModel
{
    [BindProperty] public List<RuleInput> Inputs { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var template = new ScoreTemplate { Name = "品質／資本效率", IsDefault = true };
            foreach (var input in Inputs)
            {
                var values = input.Thresholds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.Parse(x, NumberStyles.Any, CultureInfo.InvariantCulture)).ToList();
                if (values.Count != 5) throw new InvalidOperationException($"{ScoreEngine.Label(input.MetricKey)} 必須填寫五個門檻，以逗號分隔。");
                var scores = input.MetricKey == MetricKey.DebtRatio
                    ? new decimal[] { 100, 75, 50, 25, 0 }
                    : [0, 25, 50, 75, 100];
                template.Rules.Add(new ScoreRule
                {
                    MetricKey = input.MetricKey,
                    IsEnabled = input.IsEnabled,
                    Weight = input.Weight,
                    PointsJson = ScoreEngine.SerializePoints(values.Select((value, i) => new ThresholdPoint(value, scores[i])))
                });
            }
            await service.SaveTemplateAsync(template, cancellationToken);
            TempData["Message"] = "評分模板已儲存，觀察清單會立即套用新設定。";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostResetAsync(CancellationToken cancellationToken)
    {
        await service.ResetTemplateAsync(cancellationToken);
        TempData["Message"] = "已恢復品質／資本效率預設模板。";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var template = await service.GetDefaultTemplateAsync(cancellationToken);
        Inputs = template.Rules.OrderBy(x => x.Id).Select(x => new RuleInput
        {
            MetricKey = x.MetricKey,
            IsEnabled = x.IsEnabled,
            Weight = x.Weight,
            Thresholds = string.Join(", ", ScoreEngine.ParsePoints(x.PointsJson).OrderBy(p => p.Value).Select(p => p.Value.ToString("0.##", CultureInfo.InvariantCulture)))
        }).ToList();
    }

    public sealed class RuleInput
    {
        public MetricKey MetricKey { get; set; }
        public bool IsEnabled { get; set; }
        public decimal Weight { get; set; }
        public string Thresholds { get; set; } = "";
    }
}
