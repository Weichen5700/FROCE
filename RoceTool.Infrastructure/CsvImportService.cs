using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RoceTool.Core;

namespace RoceTool.Infrastructure;

public sealed class CsvImportService(RoceDbContext db) : ICsvImportService
{
    private static readonly string[] RequiredHeaders = ["StockNo", "FiscalYear", "Period", "Unit"];

    public async Task<CsvImportPreview> PreviewAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var records = ParseRecords(content).ToList();
        var rows = new List<FinancialStatement>();
        var errors = new List<string>();
        if (records.Count == 0) return new(rows, ["CSV 沒有內容。"]);

        var headers = records[0].Select((x, i) => (x: x.Trim(), i))
            .ToDictionary(x => x.x, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredHeaders)
            if (!headers.ContainsKey(required)) errors.Add($"缺少必要欄位：{required}");
        if (errors.Count > 0) return new(rows, errors);

        var knownStocks = await db.Stocks.AsNoTracking().Select(x => x.StockNo).ToHashSetAsync(cancellationToken);
        for (var line = 1; line < records.Count; line++)
        {
            var record = records[line];
            if (record.All(string.IsNullOrWhiteSpace)) continue;
            try
            {
                string Get(string name) => headers.TryGetValue(name, out var index) && index < record.Count ? record[index].Trim() : "";
                var stockNo = Get("StockNo");
                if (!knownStocks.Contains(stockNo)) throw new FormatException($"未知股票代號 {stockNo}，請先更新公司清單。");
                if (!int.TryParse(Get("FiscalYear"), out var year) || year is < 1990 or > 2200)
                    throw new FormatException("FiscalYear 格式錯誤。");
                var period = Get("Period").ToUpperInvariant();
                if (period is not ("FY" or "Q1YTD" or "Q2YTD" or "Q3YTD" or "Q4YTD"))
                    throw new FormatException("Period 必須為 FY 或 Q1YTD～Q4YTD。");
                var multiplier = Get("Unit").ToUpperInvariant() switch
                {
                    "TWD" => 0.001m,
                    "THOUSANDTWD" => 1m,
                    "MILLIONTWD" => 1000m,
                    _ => throw new FormatException("Unit 必須為 TWD、ThousandTWD 或 MillionTWD。")
                };

                decimal? Amount(string name)
                {
                    var raw = Get(name).Replace(",", "");
                    if (raw.Length == 0) return null;
                    return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                        ? value * multiplier : throw new FormatException($"{name} 不是有效數字。");
                }

                rows.Add(new FinancialStatement
                {
                    StockNo = stockNo,
                    FiscalYear = year,
                    Period = period,
                    IsAnnual = period == "FY",
                    OperatingRevenue = Amount("OperatingRevenue"),
                    OperatingIncome = Amount("OperatingIncome"),
                    InterestIncome = Amount("InterestIncome"),
                    NetIncome = Amount("NetIncome"),
                    CurrentAssets = Amount("CurrentAssets"),
                    TotalAssets = Amount("TotalAssets"),
                    CurrentLiabilities = Amount("CurrentLiabilities"),
                    TotalLiabilities = Amount("TotalLiabilities"),
                    TotalEquity = Amount("TotalEquity"),
                    SourceKind = SourceKind.Csv,
                    SourceName = string.IsNullOrWhiteSpace(Get("SourceName")) ? "CSV 匯入" : Get("SourceName"),
                    FetchedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex) { errors.Add($"第 {line + 1} 列：{ex.Message}"); }
        }

        return new(rows, errors);
    }

    public async Task<int> ImportAsync(IEnumerable<FinancialStatement> rows, bool preferred, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var row in rows)
        {
            var existing = await db.FinancialStatements.FirstOrDefaultAsync(x =>
                x.StockNo == row.StockNo && x.FiscalYear == row.FiscalYear &&
                x.Period == row.Period && x.SourceName == row.SourceName, cancellationToken);
            row.IsPreferred = preferred;
            if (existing is null) db.FinancialStatements.Add(row);
            else
            {
                existing.OperatingRevenue = row.OperatingRevenue;
                existing.OperatingIncome = row.OperatingIncome;
                existing.InterestIncome = row.InterestIncome;
                existing.NetIncome = row.NetIncome;
                existing.CurrentAssets = row.CurrentAssets;
                existing.TotalAssets = row.TotalAssets;
                existing.CurrentLiabilities = row.CurrentLiabilities;
                existing.TotalLiabilities = row.TotalLiabilities;
                existing.TotalEquity = row.TotalEquity;
                existing.IsPreferred = preferred;
                existing.FetchedAt = DateTime.UtcNow;
            }
            count++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    internal static IEnumerable<List<string>> ParseRecords(string content)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (quoted && ch == '"' && i + 1 < content.Length && content[i + 1] == '"')
            {
                field.Append('"'); i++; continue;
            }
            if (ch == '"') { quoted = !quoted; continue; }
            if (!quoted && ch == ',') { record.Add(field.ToString()); field.Clear(); continue; }
            if (!quoted && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                record.Add(field.ToString()); field.Clear();
                yield return record; record = [];
                continue;
            }
            field.Append(ch);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
