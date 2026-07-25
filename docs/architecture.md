# ROCE 觀察站專案架構視覺化

本頁以目前 workspace 的程式碼為依據，整理出四組可切換的視覺化：

1. **系統架構圖**：使用者端、Web、Core、Infrastructure、SQLite、TWSE／TPEx、MOPS、背景掃描、測試與價格提醒工具。
2. **Sequence Diagram**：股票詳情查詢、ROCE／評分計算、個股刷新，以及背景市場掃描的主要時間順序。
3. **模組依賴圖**：四個 .NET 專案、Infrastructure 的資料邊界、外部資料 provider、SQLite 與 Python CLI 的依賴方向。
4. **啟動流程圖**：從 `dotnet run` 到 `RunAsync`，包含資料目錄、DI、資料庫初始化、路由註冊與 hosted worker。

## 主要架構判讀

- `RoceTool.Web` 是應用程式入口，使用 Razor Pages 與 Minimal API。
- `RoceTool.Core` 定義領域模型、服務介面、ROCE 計算與品質評分規則，不直接處理外部 I/O。
- `RoceTool.Infrastructure` 實作 SQLite、官方資料抓取、MOPS iXBRL、個股分析、CSV 匯入與全市場掃描。
- `MarketScanWorker` 以 `BackgroundService` 逐檔取得年度 MOPS 財報，並將結果與進度保存到 SQLite。
- `tools/price_roce_watch.py` 是獨立的 Python CLI：從本機 SQLite 讀取觀察清單／年度財報，再依 TWSE／TPEx 行情判斷價格回落提醒。
- `RoceTool.Tests` 覆蓋財務計算、資料 provider、評分引擎與股票篩選。

## 程式碼依據

| 邊界 | 主要檔案 | 觀察到的責任 |
| --- | --- | --- |
| Web 啟動與端點 | [`RoceTool.Web/Program.cs`](../RoceTool.Web/Program.cs) | URL、logging、資料庫初始化、Razor Pages、Minimal API、瀏覽器自動開啟 |
| DI 組裝 | [`DependencyInjection.cs`](../RoceTool.Infrastructure/DependencyInjection.cs) | DbContext、HttpClient、分析服務、CSV 服務與 `MarketScanWorker` |
| 領域契約 | [`Interfaces.cs`](../RoceTool.Core/Interfaces.cs) | 官方資料、年報、分析、掃描與 CSV 服務介面 |
| 財務模型 | [`DomainModels.cs`](../RoceTool.Core/DomainModels.cs) | 股票、財報、評分、掃描狀態與均線模型 |
| 計算規則 | [`Calculators.cs`](../RoceTool.Core/Calculators.cs) | ROCE、含利息 ROCE、五項指標、分段評分 |
| 分析協調 | [`StockAnalysisService.cs`](../RoceTool.Infrastructure/StockAnalysisService.cs) | 目錄同步、選源、個股分析、刷新、篩選與觀察清單 |
| 外部資料 | [`OfficialDataProvider.cs`](../RoceTool.Infrastructure/OfficialDataProvider.cs)、[`MopsAnnualReportProvider.cs`](../RoceTool.Infrastructure/MopsAnnualReportProvider.cs) | TWSE／TPEx OpenAPI、行情與 MOPS iXBRL 解析 |
| SQLite 與 schema | [`RoceDbContext.cs`](../RoceTool.Infrastructure/RoceDbContext.cs) | 實體映射、索引、初始化與相容欄位 |
| 背景掃描 | [`MarketScanService.cs`](../RoceTool.Infrastructure/MarketScanService.cs) | 掃描狀態、逐檔抓年報、成功／失敗進度 |
| CSV 匯入 | [`CsvImportService.cs`](../RoceTool.Infrastructure/CsvImportService.cs) | 預覽、欄位驗證、單位正規化與匯入 |
| 價格提醒 | [`tools/price_roce_watch.py`](../tools/price_roce_watch.py) | SQLite fundamentals、官方行情、回落計算與 state file |

## 如何檢視

直接開啟 [`architecture.html`](./architecture.html)。頁面內的四個按鈕會切換不同視圖；每個視圖下方都列出圖中節點的中文責任說明。

## 驗證紀錄

- 已由 Visualize 的渲染腳本將內嵌視覺化來源輸出為 `docs/architecture.html`。
- HTML 使用內嵌 SVG 與原生 JavaScript，不依賴 Mermaid runtime 或外部資料 API；渲染外框的通用 UI utility 由允許的 CDN 提供。
- 已檢查四個視圖均有 `role="img"`、`title`、`desc` 與中文節點說明；切換按鈕會更新 `hidden` 狀態、`aria-pressed` 與目前檢視文字。
- 產品程式碼未修改；本次新增只包含視覺化頁面與本說明文件。
