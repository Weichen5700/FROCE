using System.Text.Json;

namespace RoceTool.Core;

public static class FinancialCalculator
{
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

    private static decimal? Divide(decimal? numerator, decimal? denominator) =>
        numerator is null || denominator is null || denominator <= 0 ? null : numerator.Value / denominator.Value * 100m;
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

    private static ScoreRule Rule(MetricKey key, decimal weight, (decimal value, decimal score)[] points) => new()
    {
        MetricKey = key,
        Weight = weight,
        PointsJson = SerializePoints(points.Select(x => new ThresholdPoint(x.value, x.score)))
    };

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);
}


