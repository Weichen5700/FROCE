namespace RoceTool.Web;

public sealed record FocusCompany(string StockNo, string Name, string Note);

public static class FocusCompanies
{
    public static IReadOnlyList<FocusCompany> Default { get; } =
    [
        new("2330", "台積電", "半導體權值"),
        new("2454", "聯發科", "IC 設計"),
        new("2308", "台達電", "電源與自動化"),
        new("2317", "鴻海", "電子製造"),
        new("6488", "環球晶", "半導體材料")
    ];
}
