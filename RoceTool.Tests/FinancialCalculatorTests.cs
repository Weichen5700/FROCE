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
    public void CalculatesFreeCashFlowPerShareWithPpeCapexOnly()
    {
        var result = FinancialCalculator.CalculateFreeCashFlow(new FinancialStatement
        {
            OperatingCashFlow = 1000,
            CapexPpe = -250,
            CapexIntangibles = -100,
            WeightedAverageShares = 50000,
            WeightedAverageSharesSource = "test shares"
        });

        Assert.Equal(250, result.CapitalExpenditure);
        Assert.Equal(750, result.FreeCashFlow);
        Assert.Equal(15, result.FreeCashFlowPerShare);
        Assert.Equal("test shares", result.WeightedAverageSharesSource);
    }

    [Fact]
    public void EstimatesWeightedAverageSharesFromNetIncomeAndEpsWhenMissing()
    {
        var result = FinancialCalculator.CalculateFreeCashFlow(new FinancialStatement
        {
            OperatingCashFlow = 1000,
            CapexPpe = 250,
            NetIncome = 600,
            Eps = 12
        });

        Assert.Equal(50000, result.WeightedAverageShares);
        Assert.Equal("NetIncome/EPS 推估", result.WeightedAverageSharesSource);
        Assert.Equal(15, result.FreeCashFlowPerShare);
    }

    [Fact]
    public void CalculatesStaticValuationFromAnnualStatementAndCurrentPrice()
    {
        Assert.Equal(19.31m, Math.Round(FinancialCalculator.CalculateStaticPe(176.5m, 9.14m)!.Value, 2));

        var bookValuePerShare = FinancialCalculator.CalculateBookValuePerShare(69_910_000m, 1_149_500_000m);
        Assert.Equal(60.82m, Math.Round(bookValuePerShare!.Value, 2));
        Assert.Equal(2.90m, Math.Round(FinancialCalculator.CalculateStaticPb(176.5m, 69_910_000m, 1_149_500_000m)!.Value, 2));
    }

    [Fact]
    public void RoughlyAnnualizesYtdRoceUsingReportedQuarter()
    {
        Assert.Equal(4m, FinancialCalculator.YtdAnnualizationFactor("Q1YTD"));
        Assert.Equal(2m, FinancialCalculator.YtdAnnualizationFactor("Q2YTD"));
        Assert.Equal(13.56m, Math.Round(FinancialCalculator.RoughAnnualizeYtdRoce(3.39m, "Q1YTD")!.Value, 2));
        Assert.Null(FinancialCalculator.RoughAnnualizeYtdRoce(12.22m, "FY"));
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

    [Fact]
    public void AnnualPriceReferenceUsesLastTradingCloseInsideFiscalYear()
    {
        var reference = AnnualPriceReferenceCalculator.Select(2024,
        [
            new DailyClosingPrice(new DateOnly(2024, 12, 30), 100),
            new DailyClosingPrice(new DateOnly(2024, 12, 31), 101.5m),
            new DailyClosingPrice(new DateOnly(2025, 1, 2), 999)
        ], "TWSE 個股年末收盤");

        Assert.Equal(2024, reference.FiscalYear);
        Assert.Equal(new DateOnly(2024, 12, 31), reference.TradingDate);
        Assert.Equal(101.5m, reference.Close);
    }

    [Fact]
    public void AnnualPriceReferenceReturnsMissingWhenYearHasNoClose()
    {
        var reference = AnnualPriceReferenceCalculator.Select(2024,
            [new DailyClosingPrice(new DateOnly(2025, 1, 2), 999)],
            "TWSE 個股年末收盤");

        Assert.Null(reference.TradingDate);
        Assert.Null(reference.Close);
    }
    [Fact]
    public void ConvertsCumulativeQuarterlyEpsIntoTrailingFourQuarterEps()
    {
        var statements = new[]
        {
            new FinancialStatement { FiscalYear = 2025, Period = "Q1YTD", Eps = 1.2m },
            new FinancialStatement { FiscalYear = 2025, Period = "Q2YTD", Eps = 2.5m },
            new FinancialStatement { FiscalYear = 2025, Period = "Q3YTD", Eps = 3.1m },
            new FinancialStatement { FiscalYear = 2025, Period = "FY", IsAnnual = true, Eps = 4.4m },
            new FinancialStatement { FiscalYear = 2026, Period = "Q1YTD", Eps = 1.5m }
        };
        var observations = QuarterlyEpsCalculator.Calculate(statements);
        var latest = observations.Take(4).ToList();

        Assert.Equal("2026 Q1", latest[0].Label);
        Assert.Equal(1.5m, latest[0].StandaloneEps);
        Assert.Equal(1.3m, latest[1].StandaloneEps);
        Assert.Equal(0.6m, latest[2].StandaloneEps);
        Assert.Equal(1.3m, latest[3].StandaloneEps);

        var trailing = QuarterlyEpsCalculator.CalculateTrailingFour(statements);
        Assert.True(trailing.IsComplete);
        Assert.Equal(4.7m, trailing.Value);
    }
}

