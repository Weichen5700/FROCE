using System.Text.Json;

namespace RoceTool.Core;

public static class FinancialCalculator
{
    public const string FreeCashFlowDefinition =
        "FCF_PPE = 營業活動現金流量 - |取得不動產、廠房及設備|；V1 顯示無形資產支出但不自動納入資本支出。若 FCF/股的股數來源為「NetIncome/EPS 推估」，則為推估值，並非正式加權平均股數。";

    public static RoceResult CalculateRoce(FinancialStatement statement)
    {
        if (statement.OperatingIncome is null || statement.TotalAssets is null || statement.CurrentLiabilities is null)
            return new(false, null, null, "必要欄位不足");

        var capital = statement.TotalAssets.Value - statement.CurrentLiabilities.Value;
        if (capital <= 0) return new(false, capital, null, "已動用資本小於或等於 0");
        return new(true, capital, statement.OperatingIncome.Value / capital * 100m, null);
    }

    public static RoceResult CalculateRoceIncludingInterest(FinancialStatement statement)
    {
        if (statement.InterestIncome is null)
            return new(false, null, null, "利息收入欄位不足");
        if (statement.OperatingIncome is null || statement.TotalAssets is null || statement.CurrentLiabilities is null)
            return new(false, null, null, "必要欄位不足");

        var capital = statement.TotalAssets.Value - statement.CurrentLiabilities.Value;
        if (capital <= 0) return new(false, capital, null, "已動用資本小於或等於 0");
        return new(true, capital, (statement.OperatingIncome.Value + statement.InterestIncome.Value) / capital * 100m, null);
    }

    public static decimal? ValueFor(MetricKey key, FinancialStatement s) => key switch
    {
        MetricKey.Roce => CalculateRoce(s).RocePercent,
        MetricKey.OperatingMargin => Divide(s.OperatingIncome, s.OperatingRevenue),
        MetricKey.Roe => Divide(s.NetIncome, s.TotalEquity),
        MetricKey.DebtRatio => Divide(s.TotalLiabilities, s.TotalAssets),
        MetricKey.CurrentRatio => Divide(s.CurrentAssets, s.CurrentLiabilities),
        _ => null
    };

    public static decimal? Roa(FinancialStatement s) => Divide(s.NetIncome, s.TotalAssets);
    public static decimal? CalculateStaticPe(decimal? closePrice, decimal? eps) =>
        closePrice is > 0 && eps is > 0 ? closePrice.Value / eps.Value : null;

    public static decimal? CalculateBookValuePerShare(decimal? totalEquity, decimal? shares)
    {
        // FinancialStatement amounts are stored in thousand TWD; shares are individual shares.
        return totalEquity is > 0 && shares is > 0
            ? totalEquity.Value * 1000m / shares.Value
            : null;
    }

    public static decimal? CalculateStaticPb(decimal? closePrice, decimal? totalEquity, decimal? shares)
    {
        var bookValuePerShare = CalculateBookValuePerShare(totalEquity, shares);
        return closePrice is > 0 && bookValuePerShare is > 0
            ? closePrice.Value / bookValuePerShare.Value
            : null;
    }

    public static decimal? YtdAnnualizationFactor(string period)
    {
        if (string.IsNullOrWhiteSpace(period) || period.Length < 2 ||
            char.ToUpperInvariant(period[0]) != 'Q' ||
            !int.TryParse(period[1..2], out var quarter) || quarter is < 1 or > 4)
            return null;

        return 4m / quarter;
    }

    public static decimal? RoughAnnualizeYtdRoce(decimal? rocePercent, string period) =>
        rocePercent is decimal value && YtdAnnualizationFactor(period) is decimal factor
            ? value * factor
            : null;

    public static FreeCashFlowResult CalculateFreeCashFlow(FinancialStatement statement)
    {
        decimal? capitalExpenditure = statement.CapexPpe is null ? null : Math.Abs(statement.CapexPpe.Value);
        decimal? freeCashFlow = statement.OperatingCashFlow is null || capitalExpenditure is null
            ? null : statement.OperatingCashFlow.Value - capitalExpenditure.Value;
        var shares = statement.WeightedAverageShares is > 0
            ? statement.WeightedAverageShares
            : EstimateWeightedAverageShares(statement);
        var sharesSource = statement.WeightedAverageShares is > 0
            ? (string.IsNullOrWhiteSpace(statement.WeightedAverageSharesSource) ? "財報揭露股數" : statement.WeightedAverageSharesSource)
            : shares is null ? null : "NetIncome/EPS 推估";
        decimal? fcfPerShare = freeCashFlow is null || shares is null or <= 0
            ? null : freeCashFlow.Value * 1000m / shares.Value;

        return new(
            statement.OperatingCashFlow,
            capitalExpenditure,
            statement.CapexPpe,
            statement.CapexIntangibles,
            freeCashFlow,
            shares,
            sharesSource,
            fcfPerShare);
    }

    public static string? CashFlowReviewNote(FinancialStatement current, FinancialStatement? previous)
    {
        if (previous is null) return null;

        var notes = new List<string>();
        if (LargeJump(previous.OperatingCashFlow, current.OperatingCashFlow, 100m))
            notes.Add("營業現金流年變動超過 100%");
        if (LargeJump(CalculateFreeCashFlow(previous).CapitalExpenditure, CalculateFreeCashFlow(current).CapitalExpenditure, 100m))
            notes.Add("資本支出年變動超過 100%");
        if (LargeJump(CalculateFreeCashFlow(previous).WeightedAverageShares, CalculateFreeCashFlow(current).WeightedAverageShares, 20m))
            notes.Add("股數年變動超過 20%");

        return notes.Count == 0 ? null : string.Join("；", notes);
    }

    private static decimal? Divide(decimal? numerator, decimal? denominator) =>
        numerator is null || denominator is null || denominator <= 0 ? null : numerator.Value / denominator.Value * 100m;

    private static decimal? EstimateWeightedAverageShares(FinancialStatement statement)
    {
        if (statement.NetIncome is null || statement.Eps is null or 0) return null;
        var estimated = statement.NetIncome.Value * 1000m / statement.Eps.Value;
        return estimated <= 0 ? null : estimated;
    }

    private static bool LargeJump(decimal? previous, decimal? current, decimal thresholdPercent)
    {
        if (previous is null || current is null || previous == 0) return false;
        return Math.Abs((current.Value - previous.Value) / previous.Value * 100m) >= thresholdPercent;
    }
}

public static class ScoreEngine
{
    public static ScoreBreakdown Calculate(FinancialStatement statement, IEnumerable<ScoreRule> rules)
    {
        var enabled = rules.Where(r => r.IsEnabled).ToList();
        var totalWeight = enabled.Sum(r => r.Weight);
        var results = enabled.Select(rule =>
        {
            var value = FinancialCalculator.ValueFor(rule.MetricKey, statement);
            decimal? score = value is null ? null : Piecewise(value.Value, ParsePoints(rule.PointsJson));
            return new MetricResult(rule.MetricKey, Label(rule.MetricKey), value, score, rule.Weight);
        }).ToList();

        var availableWeight = results.Where(r => r.Score is not null).Sum(r => r.Weight);
        decimal? total = availableWeight <= 0 ? null :
            results.Where(r => r.Score is not null).Sum(r => r.Score!.Value * r.Weight) / availableWeight;
        var coverage = totalWeight <= 0 ? 0 : availableWeight / totalWeight * 100m;
        return new(total, coverage, results);
    }

    public static decimal Piecewise(decimal value, IReadOnlyList<ThresholdPoint> points)
    {
        var p = points.OrderBy(x => x.Value).ToList();
        if (p.Count == 0) return 0;
        if (value <= p[0].Value) return Clamp(p[0].Score);
        if (value >= p[^1].Value) return Clamp(p[^1].Score);

        for (var i = 1; i < p.Count; i++)
        {
            if (value > p[i].Value) continue;
            var low = p[i - 1];
            var high = p[i];
            var ratio = (value - low.Value) / (high.Value - low.Value);
            return Clamp(low.Score + ratio * (high.Score - low.Score));
        }
        return 0;
    }

    public static IReadOnlyList<ThresholdPoint> ParsePoints(string json) =>
        JsonSerializer.Deserialize<List<ThresholdPoint>>(json) ?? [];

    public static string SerializePoints(IEnumerable<ThresholdPoint> points) => JsonSerializer.Serialize(points);

    public static IReadOnlyList<ScoreRule> DefaultRules() =>
    [
        Rule(MetricKey.Roce, 40, [(0,0),(5,25),(10,50),(15,75),(20,100)]),
        Rule(MetricKey.OperatingMargin, 20, [(0,0),(5,25),(10,50),(20,75),(30,100)]),
        Rule(MetricKey.Roe, 15, [(0,0),(5,25),(10,50),(15,75),(20,100)]),
        Rule(MetricKey.DebtRatio, 15, [(20,100),(35,75),(50,50),(65,25),(80,0)]),
        Rule(MetricKey.CurrentRatio, 10, [(50,0),(75,25),(100,50),(150,75),(200,100)])
    ];

    public static string Label(MetricKey key) => key switch
    {
        MetricKey.Roce => "ROCE",
        MetricKey.OperatingMargin => "營業利益率",
        MetricKey.Roe => "ROE",
        MetricKey.DebtRatio => "負債比",
        MetricKey.CurrentRatio => "流動比率",
        _ => key.ToString()
    };

    public static string Description(MetricKey key) => key switch
    {
        MetricKey.Roce => "資本報酬率：每投入 100 元資本能賺回多少，越高越好（一般 > 15% 合格）",
        MetricKey.OperatingMargin => "營業利潤：每 100 元營收去掉成本後剩多少，越高代表產品有定價能力（一般 > 10% 合格）",
        MetricKey.Roe => "股東權益報酬率：股東投入的錢能賺回多少，越高越好（一般 > 15% 合格）",
        MetricKey.DebtRatio => "债務比例：資產中有多少比例是借來的，越低越安全（一般 < 50% 合格）",
        MetricKey.CurrentRatio => "短期償債能力：流動資產能否支付流動負債，越高越安全（一般 > 100% 合格）",
        _ => ""
    };

    private static ScoreRule Rule(MetricKey key, decimal weight, (decimal value, decimal score)[] points) => new()
    {
        MetricKey = key,
        Weight = weight,
        PointsJson = SerializePoints(points.Select(x => new ThresholdPoint(x.value, x.score)))
    };

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);
}


