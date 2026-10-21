using System.Text.Json;
using System.Text.RegularExpressions;
using RoceTool.Core;

namespace RoceTool.Web;

public sealed class FinanceResearchStore
{
    public IReadOnlyDictionary<string, FinanceResearchCompany> Companies { get; private set; } =
        new Dictionary<string, FinanceResearchCompany>();
    public DateOnly? VerifiedOn { get; private set; }
    public string? Error { get; private set; }
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly string[] FileNames = ["finance-research-fubon-cathay.json", "finance-research-esun-yuanta.json", "finance-research-ctbc.json"];

    public FinanceResearchStore(ILogger<FinanceResearchStore> logger) : this(Path.Combine(AppContext.BaseDirectory, "ResearchData"), logger) { }

    public FinanceResearchStore(string directory, ILogger<FinanceResearchStore> logger)
    {
        try
        {
            var documents = FileNames.Select(name => Parse(File.ReadAllText(Path.Combine(directory, name)))).ToArray();
            var companies = documents.SelectMany(x => x.Companies).ToList();
            if (companies.Select(x => x.StockNo).Distinct().Count() != companies.Count)
                throw new InvalidDataException("公司研究代號重複。");
            Companies = companies.ToDictionary(x => x.StockNo);
            VerifiedOn = documents.Min(x => x.VerifiedOn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            logger.LogError(ex, "金融研究資料載入失敗。");
            Error = "研究資料檔缺漏或格式／口徑檢查未通過，請檢查發行檔 ResearchData。既有財報與股利仍可查看。";
        }
    }

    public static FinanceResearchDocument Parse(string json)
    {
        var document = JsonSerializer.Deserialize<FinanceResearchDocument>(json, Options)
            ?? throw new InvalidDataException("研究資料不可為空。");
        if (document.SchemaVersion != 1 || document.VerifiedOn == default ||
            document.VerifiedOn > DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)) || document.Companies is null || document.Companies.Count == 0)
            throw new InvalidDataException("不支援的研究資料版本或查核日期。");
        foreach (var company in document.Companies)
        {
            if (company is null || !Regex.IsMatch(company.StockNo ?? "", @"^\d{4}$") ||
                string.IsNullOrWhiteSpace(company.Name) || string.IsNullOrWhiteSpace(company.BusinessSummary) ||
                company.Risks is null || company.Businesses is null || company.Observations is null || company.Bridges is null || company.Observations.Count == 0)
                throw new InvalidDataException("研究公司資料不完整。");
            foreach (var item in company.Observations)
            {
                if (item is null || item.Category is not ("business" or "quality" or "capital") ||
                    string.IsNullOrWhiteSpace(item.Metric) || string.IsNullOrWhiteSpace(item.Entity) ||
                    string.IsNullOrWhiteSpace(item.Basis) || string.IsNullOrWhiteSpace(item.Unit) ||
                    string.IsNullOrWhiteSpace(item.AccountingRegime) || string.IsNullOrWhiteSpace(item.Meaning) ||
                    string.IsNullOrWhiteSpace(item.SourceVersion) || string.IsNullOrWhiteSpace(item.SourceName) ||
                    !ValidSource(item.SourceUrl) || !ValidObservationDate(item) ||
                    item.AsOfDate > document.VerifiedOn || item.PublishedOn > document.VerifiedOn)
                    throw new InvalidDataException($"{company.StockNo} 研究觀察缺少可核對口徑：{item?.Metric}");
            }
            foreach (var bridge in company.Bridges)
            {
                if (bridge is null || !ValidSource(bridge.SourceUrl) || !ValidPeriod(bridge.Period, bridge.AsOfDate) ||
                    bridge.AsOfDate > document.VerifiedOn || bridge.PublishedOn > document.VerifiedOn ||
                    string.IsNullOrWhiteSpace(bridge.Entity) || string.IsNullOrWhiteSpace(bridge.Unit) ||
                    string.IsNullOrWhiteSpace(bridge.Basis) || string.IsNullOrWhiteSpace(bridge.AccountingRegime) ||
                    string.IsNullOrWhiteSpace(bridge.AdjustmentLabel) || string.IsNullOrWhiteSpace(bridge.ResultLabel) ||
                    string.IsNullOrWhiteSpace(bridge.SourceName) || !bridge.Reconciles)
                    throw new InvalidDataException($"{company.StockNo} 獲利橋接未勾稽。");
            }
        }
        if (document.Companies.Select(x => x.StockNo).Distinct().Count() != document.Companies.Count)
            throw new InvalidDataException("公司研究代號重複。");
        return document;
    }

    private static bool ValidSource(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    private static bool ValidPeriod(string? period, DateOnly date)
    {
        var match = Regex.Match(period ?? "", @"^(\d{4})(FY|H1|Q[1-4](YTD)?)$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var year) || year != date.Year) return false;
        var suffix = match.Groups[2].Value;
        var month = suffix == "FY" ? 12 : suffix == "H1" ? 6 : (suffix[1] - '0') * 3;
        return date == new DateOnly(year, month, DateTime.DaysInMonth(year, month));
    }

    private static bool ValidObservationDate(FinanceResearchObservation item)
    {
        var match = Regex.Match(item.Period ?? "", @"^(\d{4})(FY|H1|Q[1-4](YTD)?)$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var year) || year is < 1900 or > 9999) return false;
        var suffix = match.Groups[2].Value;
        var month = suffix == "FY" ? 12 : suffix == "H1" ? 6 : (suffix[1] - '0') * 3;
        var periodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return item.DateBasis switch
        {
            "periodEnd" => item.AsOfDate == periodEnd,
            "sourceDate" => item.AsOfDate >= periodEnd,
            "unknown" => item.AsOfDate is null && item.Value is null,
            _ => false
        };
    }
}
