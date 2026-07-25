using System.Globalization;
using System.Text.Json;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class OfficialDataProvider(HttpClient httpClient) : IOfficialDataProvider
{
    private const string Twse = "https://openapi.twse.com.tw/v1";
    private const string Tpex = "https://www.tpex.org.tw/openapi/v1";
    private const string TwseDailyHistory = "https://www.twse.com.tw/exchangeReport/STOCK_DAY";
    private const string TpexDailyHistory = "https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock";
    private static readonly Dictionary<string, (DateTime CachedAt, IReadOnlyList<DailyClosingPrice> Prices)> DailyPriceCache = [];
    private static readonly object DailyPriceCacheLock = new();

    public async Task<IReadOnlyList<Stock>> FetchStockCatalogAsync(CancellationToken cancellationToken = default)
    {
        var listedTask = GetArrayAsync($"{Twse}/opendata/t187ap03_L", cancellationToken);
        var otcTask = GetArrayAsync($"{Tpex}/mopsfin_t187ap03_O", cancellationToken);
        var listedIndustryTask = GetArrayAsync($"{Twse}/opendata/t187ap14_L", cancellationToken);
        var otcIndustryTask = GetArrayAsync($"{Tpex}/mopsfin_t187ap14_O", cancellationToken);
        await Task.WhenAll(listedTask, otcTask, listedIndustryTask, otcIndustryTask);

        var industryNames = listedIndustryTask.Result.Concat(otcIndustryTask.Result)
            .Select(x => new
            {
                Code = Get(x, "公司代號", "SecuritiesCompanyCode"),
                Name = Get(x, "產業別").Trim()
            })
            .Where(x => x.Code.Length > 0 && x.Name.Length > 0)
            .GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.First().Name, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var stocks = new List<Stock>();
        stocks.AddRange(listedTask.Result.Select(x => MapStock(x, MarketKind.Listed, now)));
        stocks.AddRange(otcTask.Result.Select(x => MapStock(x, MarketKind.Otc, now)));
        foreach (var stock in stocks)
            if (industryNames.TryGetValue(stock.StockNo, out var industryName)) stock.IndustryName = industryName;

        var industryNamesByCode = stocks
            .Where(x => x.IndustryCode.Length > 0 && x.IndustryName.Length > 0)
            .GroupBy(x => x.IndustryCode)
            .ToDictionary(x => x.Key, x => x.GroupBy(v => v.IndustryName)
                .OrderByDescending(v => v.Count()).First().Key, StringComparer.OrdinalIgnoreCase);
        foreach (var stock in stocks.Where(x => x.IndustryName.Length == 0))
            stock.IndustryName = industryNamesByCode.TryGetValue(stock.IndustryCode, out var name) ? name : "未分類";

        return stocks.Where(x => !string.IsNullOrWhiteSpace(x.StockNo)).ToList();
    }

    public async Task<IReadOnlyList<FinancialStatement>> FetchLatestStatementsAsync(CancellationToken cancellationToken = default)
    {
        var listedIncome = GetArrayAsync($"{Twse}/opendata/t187ap06_L_ci", cancellationToken);
        var listedBalance = GetArrayAsync($"{Twse}/opendata/t187ap07_L_ci", cancellationToken);
        var otcIncome = GetArrayAsync($"{Tpex}/mopsfin_t187ap06_O_ci", cancellationToken);
        var otcBalance = GetArrayAsync($"{Tpex}/mopsfin_t187ap07_O_ci", cancellationToken);
        await Task.WhenAll(listedIncome, listedBalance, otcIncome, otcBalance);

        var result = new List<FinancialStatement>();
        result.AddRange(JoinStatements(listedIncome.Result, listedBalance.Result, SourceKind.Twse,
            "證交所 OpenAPI", $"{Twse}/opendata/t187ap06_L_ci"));
        result.AddRange(JoinStatements(otcIncome.Result, otcBalance.Result, SourceKind.Tpex,
            "櫃買中心 OpenAPI", $"{Tpex}/mopsfin_t187ap06_O_ci"));
        return result;
    }

    public async Task ApplyValuationsAsync(IDictionary<string, Stock> stocks, CancellationToken cancellationToken = default)
    {
        var listedTask = GetArrayAsync($"{Twse}/exchangeReport/BWIBBU_ALL", cancellationToken);
        var otcTask = GetArrayAsync($"{Tpex}/tpex_mainboard_peratio_analysis", cancellationToken);
        var listedQuotesTask = TryGetArrayAsync($"{Twse}/exchangeReport/STOCK_DAY_ALL", cancellationToken);
        var otcQuotesTask = TryGetArrayAsync($"{Tpex}/tpex_mainboard_quotes", cancellationToken);
        await Task.WhenAll(listedTask, otcTask, listedQuotesTask, otcQuotesTask);
        var now = DateTime.UtcNow;

        foreach (var row in listedTask.Result)
        {
            var code = Get(row, "Code");
            if (!stocks.TryGetValue(code, out var stock)) continue;
            stock.PeRatio = Number(Get(row, "PEratio"));
            stock.PbRatio = Number(Get(row, "PBratio"));
            stock.DividendYield = Number(Get(row, "DividendYield"));
            stock.ValuationFetchedAt = now;
        }

        foreach (var row in otcTask.Result)
        {
            var code = Get(row, "SecuritiesCompanyCode");
            if (!stocks.TryGetValue(code, out var stock)) continue;
            stock.PeRatio = Number(Get(row, "PriceEarningRatio"));
            stock.PbRatio = Number(Get(row, "PriceBookRatio"));
            stock.DividendYield = Number(Get(row, "YieldRatio"));
            stock.ValuationFetchedAt = now;
        }

        ApplyQuotes(stocks, listedQuotesTask.Result, "Code", "ClosingPrice", "HighestPrice", "LowestPrice", "TWSE STOCK_DAY_ALL", now);
        ApplyQuotes(stocks, otcQuotesTask.Result, "SecuritiesCompanyCode", "Close", "High", "Low", "TPEx tpex_mainboard_quotes", now);
    }

    public async Task<IReadOnlyList<DailyClosingPrice>> FetchDailyClosingPricesAsync(string stockNo, MarketKind market,
        bool force = false, int minimumTradingDays = 240, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockNo)) return [];
        var cacheKey = $"{market}:{stockNo.Trim().ToUpperInvariant()}";
        lock (DailyPriceCacheLock)
        {
            if (!force && DailyPriceCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(30))
                return cached.Prices;
        }

        var requestedDays = Math.Max(1, minimumTradingDays);
        var prices = new Dictionary<DateOnly, decimal>();
        var currentMonth = DateTime.UtcNow.AddHours(8);
        currentMonth = new DateTime(currentMonth.Year, currentMonth.Month, 1);

        // Three months at a time avoids a large burst to either official data source.
        for (var monthOffset = 0; monthOffset < 15 && prices.Count < requestedDays; monthOffset += 3)
        {
            var tasks = Enumerable.Range(monthOffset, 3)
                .Select(offset => TryFetchMonthlyClosingPricesAsync(stockNo, market, currentMonth.AddMonths(-offset), cancellationToken));
            var months = await Task.WhenAll(tasks);
            foreach (var month in months)
                foreach (var point in month)
                    prices[point.Date] = point.Close;
        }

        IReadOnlyList<DailyClosingPrice> result = prices.Count == 0
            ? []
            : prices.Select(x => new DailyClosingPrice(x.Key, x.Value)).OrderByDescending(x => x.Date).ToList();
        if (result.Count > 0)
        {
            lock (DailyPriceCacheLock)
                DailyPriceCache[cacheKey] = (DateTime.UtcNow, result);
        }
        return result;
    }

    private async Task<IReadOnlyList<DailyClosingPrice>> TryFetchMonthlyClosingPricesAsync(string stockNo, MarketKind market,
        DateTime month, CancellationToken cancellationToken)
    {
        var date = month.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var url = market == MarketKind.Listed
            ? $"{TwseDailyHistory}?response=json&date={date}&stockNo={Uri.EscapeDataString(stockNo)}"
            : $"{TpexDailyHistory}?date={month:yyyy}%2F{month:MM}%2F01&code={Uri.EscapeDataString(stockNo)}&response=json";
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return market == MarketKind.Listed ? ParseTwseDailyPrices(document.RootElement) : ParseTpexDailyPrices(document.RootElement);
        }
        catch (HttpRequestException) { return []; }
        catch (JsonException) { return []; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
    }

    private static IReadOnlyList<DailyClosingPrice> ParseTwseDailyPrices(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        return ParseDailyPriceRows(rows);
    }

    private static IReadOnlyList<DailyClosingPrice> ParseTpexDailyPrices(JsonElement root)
    {
        if (!root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Array ||
            tables.GetArrayLength() == 0 || !tables[0].TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return [];
        return ParseDailyPriceRows(rows);
    }

    private static IReadOnlyList<DailyClosingPrice> ParseDailyPriceRows(JsonElement rows)
    {
        var prices = new List<DailyClosingPrice>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 7) continue;
            var date = ParseRocDate(row[0].ToString());
            var close = Number(row[6].ToString());
            if (date is not null && close is > 0) prices.Add(new DailyClosingPrice(date.Value, close.Value));
        }
        return prices;
    }

    private static DateOnly? ParseRocDate(string value)
    {
        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day))
            return null;
        if (year < 1911) year += 1911;
        return DateOnly.TryParse($"{year:D4}-{month:D2}-{day:D2}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static void ApplyQuotes(IDictionary<string, Stock> stocks, IReadOnlyList<JsonElement> rows,
        string codeField, string closeField, string highField, string lowField, string source, DateTime fetchedAt)
    {
        foreach (var row in rows)
        {
            var code = Get(row, codeField);
            if (!stocks.TryGetValue(code, out var stock)) continue;

            var close = Number(Get(row, closeField));
            if (close is null or <= 0) continue;

            var change = Number(Get(row, "Change", "PriceChange"));
            var previousClose = change is null ? null : close - change;
            stock.ClosePrice = close;
            stock.PriceChange = change;
            stock.PriceChangePercent = previousClose is > 0 ? change / previousClose * 100m : null;
            stock.DayHigh = Number(Get(row, highField));
            stock.DayLow = Number(Get(row, lowField));
            stock.TradeVolume = Number(Get(row, "TradeVolume", "TradingVolume", "TradingShares", "Volume")) is decimal volume
                ? decimal.ToInt64(volume) : null;
            stock.QuoteDate = FormatQuoteDate(Get(row, "Date", "TradeDate"));
            stock.QuoteSource = source;
            stock.QuoteFetchedAt = fetchedAt;
        }
    }

    private static string FormatQuoteDate(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 7 && int.TryParse(digits[..3], out var rocYear))
            return $"{rocYear + 1911:D4}-{digits[3..5]}-{digits[5..]}";
        if (digits.Length == 8)
            return $"{digits[..4]}-{digits[4..6]}-{digits[6..]}";
        return value.Trim();
    }

    private static Stock MapStock(JsonElement row, MarketKind market, DateTime now)
    {
        var reportType = Get(row, "編制財務報表類型", "PreparationOfFinancialReportType");
        var industry = Get(row, "產業別", "SecuritiesIndustryCode");
        return new Stock
        {
            StockNo = Get(row, "公司代號", "SecuritiesCompanyCode").Trim(),
            Name = Get(row, "公司簡稱", "CompanyAbbreviation", "公司名稱", "CompanyName").Trim(),
            Market = market,
            IndustryCode = industry.Trim(),
            StatementKind = reportType == "1" && industry != "17" ? StatementKind.General : StatementKind.Financial,
            IsSupported = true,
            UpdatedAt = now
        };
    }

    private static IEnumerable<FinancialStatement> JoinStatements(
        IReadOnlyList<JsonElement> incomeRows, IReadOnlyList<JsonElement> balanceRows,
        SourceKind sourceKind, string sourceName, string sourceUrl)
    {
        var balances = balanceRows.GroupBy(x => Get(x, "公司代號", "SecuritiesCompanyCode"))
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var income in incomeRows)
        {
            var code = Get(income, "公司代號", "SecuritiesCompanyCode");
            if (string.IsNullOrWhiteSpace(code) || !balances.TryGetValue(code, out var balance)) continue;
            var rocYear = Integer(Get(income, "年度", "Year"));
            var quarter = Integer(Get(income, "季別", "Season"));
            if (rocYear <= 0 || quarter is < 1 or > 4) continue;

            yield return new FinancialStatement
            {
                StockNo = code,
                FiscalYear = rocYear + 1911,
                Period = $"Q{quarter}YTD",
                IsAnnual = false,
                OperatingRevenue = Number(Get(income, "營業收入")),
                OperatingIncome = Number(Get(income, "營業利益（損失）")),
                InterestIncome = Number(Get(income, "利息收入", "利息收入（以有效利息法計算）", "利息收入（以有效利率法計算）")),
                NetIncome = Number(Get(income, "本期淨利（淨損）")),
                Eps = Number(Get(income, "基本每股盈餘（元）")),
                CurrentAssets = Number(Get(balance, "流動資產")),
                TotalAssets = Number(Get(balance, "資產總額", "資產總計")),
                CurrentLiabilities = Number(Get(balance, "流動負債")),
                TotalLiabilities = Number(Get(balance, "負債總額", "負債總計")),
                TotalEquity = Number(Get(balance, "權益總額", "權益總計")),
                SourceKind = sourceKind,
                SourceName = sourceName,
                SourceUrl = sourceUrl,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<IReadOnlyList<JsonElement>> GetArrayAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private async Task<IReadOnlyList<JsonElement>> TryGetArrayAsync(string url, CancellationToken cancellationToken)
    {
        try { return await GetArrayAsync(url, cancellationToken); }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
    }

    internal static string Get(JsonElement row, params string[] names)
    {
        foreach (var name in names)
            if (row.TryGetProperty(name, out var value)) return value.ToString();
        return "";
    }

    internal static decimal? Number(string? value) =>
        decimal.TryParse(value?.Replace(",", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result : null;

    private static int Integer(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
}


