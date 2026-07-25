using RoceTool.Core;

namespace RoceTool.Tests;

public sealed class ScoreEngineTests
{
    [Fact]
    public void InterpolatesAndClampsScores()
    {
        var points = new[] { new ThresholdPoint(0, 0), new ThresholdPoint(10, 50), new ThresholdPoint(20, 100) };
        Assert.Equal(25, ScoreEngine.Piecewise(5, points));
        Assert.Equal(0, ScoreEngine.Piecewise(-5, points));
        Assert.Equal(100, ScoreEngine.Piecewise(25, points));
    }

    [Fact]
    public void ReweightsMissingMetricsAndReportsCoverage()
    {
        var statement = new FinancialStatement
        {
            OperatingIncome = 100, TotalAssets = 1000, CurrentLiabilities = 500,
            OperatingRevenue = null
        };
        var rules = ScoreEngine.DefaultRules().Where(x =>
            x.MetricKey is MetricKey.Roce or MetricKey.OperatingMargin).ToList();
        rules[0].Weight = 60;
        rules[1].Weight = 40;

        var result = ScoreEngine.Calculate(statement, rules);
        Assert.Equal(100, result.TotalScore);
        Assert.Equal(60, result.CoveragePercent);
    }

    [Fact]
    public void DefaultWeightsEqualOneHundred()
    {
        Assert.Equal(100, ScoreEngine.DefaultRules().Where(x => x.IsEnabled).Sum(x => x.Weight));
    }
}
