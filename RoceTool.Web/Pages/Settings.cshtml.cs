using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RoceTool.Core;

namespace RoceTool.Web.Pages;

public sealed class SettingsModel(IStockAnalysisService service) : PageModel
{
    [BindProperty] public List<RuleInput> Inputs { get; set; } = [];

    public IReadOnlyList<ScoreMetricExplanation> MetricExplanations { get; } =
    [
        new(MetricKey.Roce, "衡量公司用投入資本創造營業利益的效率。", "營業利益 ÷（資產總額 − 流動負債）× 100%", "40%：本工具以資本效率為核心，因此給最高比重。"),
        new(MetricKey.OperatingMargin, "衡量每 100 元營收扣除營業成本費用後，留下多少營業利益。", "營業利益 ÷ 營業收入 × 100%", "20%：觀察本業獲利能力，與 ROCE 互補。"),
        new(MetricKey.Roe, "衡量公司運用股東權益創造淨利的能力。", "本期淨利 ÷ 權益總額 × 100%", "15%：補充股東報酬觀點，避免與 ROCE 重複占比過高。"),
        new(MetricKey.DebtRatio, "衡量資產中由負債支應的比例；比率較低通常代表財務槓桿較保守。", "負債總額 ÷ 資產總額 × 100%", "15%：和 ROE 同權重，讓獲利表現同時受到財務風險平衡。"),
        new(MetricKey.CurrentRatio, "衡量流動資產相對於短期負債的支應能力。", "流動資產 ÷ 流動負債 × 100%", "10%：納入短期償債觀察，但降低產業差異對總分的影響。")
    ];

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

public sealed record ScoreMetricExplanation(MetricKey Key, string Meaning, string Formula, string DefaultWeightReason);
