using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class FinanceQuoteRefreshService(RoceDbContext db, IOfficialDataProvider officialData)
{
    public async Task<FinanceQuoteRefreshResult> RefreshAsync(string? stockNo = null, CancellationToken cancellationToken = default)
    {
        var query = db.Stocks.Where(x => x.IndustryCode == "17" && x.StatementKind == StatementKind.Financial && x.IsSupported);
        if (!string.IsNullOrWhiteSpace(stockNo)) query = query.Where(x => x.StockNo == stockNo);
        var stocks = await query.ToListAsync(cancellationToken);
        // The existing provider also assigns PE/PB/yield; only copy its quote fields back to the tracked entities.
        var incoming = stocks.ToDictionary(x => x.StockNo, x => new Stock { StockNo = x.StockNo, Market = x.Market });
        await officialData.ApplyValuationsAsync(incoming, cancellationToken);
        var updated = 0;
        var retained = 0;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        foreach (var stock in stocks)
        {
            var quote = incoming[stock.StockNo];
            if (quote.ClosePrice is not > 0 || quote.QuoteFetchedAt is null ||
                !DateOnly.TryParseExact(quote.QuoteDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var quoteDate) ||
                quoteDate > today) { retained++; continue; }
            if (DateOnly.TryParseExact(stock.QuoteDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var existingDate) &&
                existingDate <= today && quoteDate < existingDate) { retained++; continue; }
            stock.ClosePrice = quote.ClosePrice;
            stock.PriceChange = quote.PriceChange;
            stock.PriceChangePercent = quote.PriceChangePercent;
            stock.DayHigh = quote.DayHigh;
            stock.DayLow = quote.DayLow;
            stock.TradeVolume = quote.TradeVolume;
            stock.QuoteDate = quote.QuoteDate;
            stock.QuoteSource = quote.QuoteSource;
            stock.QuoteFetchedAt = quote.QuoteFetchedAt;
            updated++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(updated, retained);
    }
}

public sealed record FinanceQuoteRefreshResult(int Updated, int Retained);
