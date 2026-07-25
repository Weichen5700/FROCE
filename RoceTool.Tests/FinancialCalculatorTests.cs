using RoceTool.Core;

namespace RoceTool.Tests;

public sealed class FinancialCalculatorTests
{
    [Fact]
    public void CalculatesExpectedRoce()
    {
        var result = FinancialCalculator.CalculateRoce(new FinancialStatement
        {
            OperatingIncome = 100, TotalAssets = 1000, CurrentLiabilities = 400
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(600, result.CapitalEmployed);
        Assert.Equal(16.67m, Math.Round(result.RocePercent!.Value, 2));
    }

    [Fact]
    public void CalculatesRoceIncludingInterestIncome()
    {
        var result = FinancialCalculator.CalculateRoceIncludingInterest(new FinancialStatement
        {
            OperatingIncome = 100, InterestIncome = 20, TotalAssets = 1000, CurrentLiabilities = 400
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(20m, result.RocePercent);
        Assert.Equal("利息收入欄位不足", FinancialCalculator.CalculateRoceIncludingInterest(new FinancialStatement
        {
            OperatingIncome = 100, TotalAssets = 1000, CurrentLiabilities = 400
        }).ErrorMessage);
    }

    [Fact]
    public void AllowsNegativeOperatingIncome()
    {
        var result = FinancialCalculator.CalculateRoce(new FinancialStatement
        {
            OperatingIncome = -50, TotalAssets = 1000, CurrentLiabilities = 400
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(-8.33m, Math.Round(result.RocePercent!.Value, 2));
    }

    [Fact]
    public void RejectsNonPositiveCapitalAndMissingValues()
    {
        Assert.False(FinancialCalculator.CalculateRoce(new FinancialStatement
            { OperatingIncome = 100, TotalAssets = 300, CurrentLiabilities = 400 }).IsSuccess);
        Assert.Equal("必要欄位不足", FinancialCalculator.CalculateRoce(new FinancialStatement
            { TotalAssets = 1000, CurrentLiabilities = 400 }).ErrorMessage);
    }

    [Fact]
    public void CalculatesDailyMovingAveragesFromMostRecentTradingDays()
    {
        var prices = Enumerable.Range(1, 240)
            .Select(day => new DailyClosingPrice(new DateOnly(2026, 1, 1).AddDays(day), day))
            .ToList();

        var averages = MovingAverageCalculator.Calculate(prices).ToDictionary(x => x.TradingDays);

        Assert.Equal(238m, averages[5].Value);
        Assert.Equal(235.5m, averages[10].Value);
        Assert.Equal(230.5m, averages[20].Value);
        Assert.Equal(210.5m, averages[60].Value);
        Assert.Equal(180.5m, averages[120].Value);
        Assert.Equal(120.5m, averages[240].Value);
    }
}

