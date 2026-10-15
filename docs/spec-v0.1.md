# ROCE 觀察站 v0.1 規格基線

> 狀態：目前可行版本的第一版切點
>
> 基線盤點日期：2026-07-26；已實作功能補記：2026-10-14
>
> 來源：目前 workspace 內的程式碼、測試、README 與架構文件。這份文件是實作對照規格，不代表尚未實作的未來功能。

## 1. 版本目標

ROCE 觀察站是一個只在本機執行的繁體中文台股基本面觀察工具。使用者可以搜尋上市／上櫃公司、取得官方財報與行情、計算 ROCE 及品質分數、建立觀察清單，並用篩選與同業比較找出值得進一步研究的標的。

本版本的判定原則如下：

- 以目前程式碼的可觀察行為為準，不以未實作的構想推導功能。
- 正式年度評分以標準 ROCE（營業利益／已動用資本）為準；含利息收入的 ROCE 只作補充觀察。
- 季報損益為年初累計數，季報分數是未年化快照，不取代完整年度評分。
- 外部資料來自官方公開來源；資料抓取失敗時保留錯誤紀錄，使用者可用 CSV 作為補充或優先來源。
- 結果只供研究，不構成投資建議。

## 2. 使用者功能範圍

| 編號 | 功能 | v0.1 行為與限制 | 主要實作依據 | 文件覆蓋 |
| --- | --- | --- | --- | --- |
| F-01 | 本機啟動與資料保存 | 以 .NET 10 啟動 Razor Pages；預設監聽 `127.0.0.1:5050`，可由設定修改；SQLite 與 Data Protection keys 建立在 `RoceTool.Web/App_Data`。Windows 預設自動開啟瀏覽器。 | `RoceTool.Web/Program.cs`、`RoceTool.Web/appsettings.json` | README、architecture、本文 |
| F-02 | 上市／上櫃公司目錄與搜尋 | 從官方目錄同步股票代號、名稱、上市櫃市場、產業與財報類型；查詢頁可用代號或名稱搜尋。 | `OfficialDataProvider.cs`、`StockAnalysisService.cs`、`Pages/Stocks/Search.*` | README、本文 |
| F-03 | 觀察清單 | 可從搜尋、首頁重點公司或個股詳情加入；首頁可排序、查看分析或移除。Web 與 API 均支援新增／刪除。 | `Pages/Index.*`、`Pages/Stocks/*`、`Program.cs` | README、本文 |
| F-04 | 個股分析與資料刷新 | 個股頁顯示選定年度、近七年完整年度、最新季快照、來源與抓取時間；可手動重新抓取 MOPS 年報與最新資料。 | `StockAnalysisService.cs`、`MopsAnnualReportProvider.cs`、`Pages/Stocks/Details.*` | README、architecture、本文 |
| F-05 | 財務指標計算 | 支援標準 ROCE、營業利益率、ROE、負債比、流動比率；必要欄位不足或已動用資本小於等於 0 時回傳不可計算狀態。 | `RoceTool.Core/Calculators.cs` | README、測試、本文 |
| F-06 | 含利息收入 ROCE | 在有 `InterestIncome` 時另算「營業利益＋利息收入」ROCE；正式評分、排名與篩選仍使用標準 ROCE。 | `Calculators.cs`、`Pages/Stocks/Details.cshtml` | README、測試、本文 |
| F-07 | 品質評分 | 五項指標依啟用狀態、權重與五段門檻計算總分；缺資料時依可用權重重算，並顯示資料覆蓋率。預設篩選門檻為 ROCE 20%、分數 80、覆蓋率 100%。 | `ScoreEngine`、`StockAnalysisService.cs`、`ScoreEngineTests.cs` | README、測試、本文 |
| F-08 | 評分模板設定 | 設定頁可逐項啟用／停用指標、修改權重與五段門檻，儲存或恢復預設模板；另列指標意義、計算公式、預設權重考量及缺資料時的總分算法。 | `Pages/Settings.*`、`StockAnalysisService.cs` | README、本文 |
| F-09 | 年度趨勢與季報觀察 | 個股頁顯示七年 ROCE 趨勢；最新季顯示 YTD 財務欄位、未年化 ROCE／品質快照及相對年度差異，不直接替代年度評分。 | `StockAnalysisService.cs`、`Pages/Stocks/Details.cshtml` | README、本文 |
| F-10 | 價格與估值摘要 | 首頁、詳情與篩選頁可顯示 ROCE、EPS、官方最新收盤價、本益比與 ROE；詳情頁另顯示漲跌、漲跌幅、當日高低、成交量、行情日期與來源。最新收盤價取官方最新交易日收盤，不使用盤中價。 | `OfficialDataProvider.cs`、`Pages/Index.cshtml`、`Pages/Stocks/Details.cshtml`、`Pages/Screen.cshtml` | README、本文 |
| F-11 | 技術均線與年度股價參考 | 個股詳情計算 5、10、20、60、120、240 個交易日簡單移動平均，並顯示最新收盤價相對均線的百分比；切換七年年度資料時，取得每年度最後一個交易日的官方收盤價，移到 ROCE 數值可懸停查看。均線與年度參考都使用交易所原始日收盤價，休市日不計且不做除權息還原。 | `MovingAverageCalculator`、`AnnualPriceReferenceCalculator`、`OfficialDataProvider.cs`、`Details.cshtml` | README、測試、本文 |
| F-12 | 同業與同規模比較 | 依官方中文產業名稱計算產業內 ROCE 排名／中位數，再依產業內投入資本三等分提供小型／中型／大型排名與中位數。 | `StockAnalysisService.cs`、`PeerComparison`、`Screen.cshtml` | README、architecture、本文 |
| F-13 | 高品質反向篩選 | 篩選頁與 `/api/screen` 支援最低 ROCE、品質分數、資料覆蓋率、年度、產業、公司規模與排序條件；排序支援品質分數、ROCE、EPS、本益比、ROE、股票代號。 | `Pages/Screen.*`、`Program.cs`、`StockScreenTests.cs` | README、測試、本文 |
| F-14 | 全市場背景掃描 | 逐檔抓取一般業公司的指定完整年度 MOPS 年報；可開始、暫停、關閉網站後再啟動續跑，保存總數、成功、失敗、目前股票與錯誤訊息。完成判斷以同年度資料具備 ROCE 與 FCF 基礎欄位為準，不限單一來源。 | `MarketScanService.cs`、`MarketScanWorker`、`Program.cs`、`Pages/Screen.*` | README、architecture、測試、本文 |
| F-15 | 官方資料更新與錯誤紀錄 | 可更新上市／上櫃目錄、最新季、日行情與估值；資料錯誤保存於 SQLite，資料管理頁顯示最近錯誤。資料來源與抓取時間隨資料保存。 | `Data.cshtml.cs`、`OfficialDataProvider.cs`、`RoceDbContext.cs` | README、architecture、本文 |
| F-16 | CSV 預覽、匯入與年度匯出 | 支援 CSV 預覽驗證、金額單位 `TWD`／`ThousandTWD`／`MillionTWD` 正規化、年度／YTD 期間與來源優先設定；可下載範例 CSV，並選擇單一財報年度匯出所有來源的 FY 與 YTD 資料，匯出欄位可沿用既有匯入格式。 | `CsvImportService.cs`、`Data.cshtml.cs`、`samples/financial-statements.csv` | README、測試、本文 |
| F-17 | 價格回落提醒 CLI | `tools/price_roce_watch.py` 讀取本機 SQLite 財報／觀察清單，依 ROCE 門檻與價格回落門檻產生提醒；支援今年以來高點或監控開始後高點、JSON 輸出與 `--no-write`。使用 TWSE／TPEx 官方日行情，不是盤中即時通知。 | `tools/price_roce_watch.py`、`price-roce-alerts.json` | README、本文 |
| F-18 | API 摘要與控制端點 | 提供股票搜尋、個股分析、focus metrics、刷新、觀察清單 CRUD、全市場掃描狀態／開始／暫停、篩選與評分模板查詢。 | `RoceTool.Web/Program.cs` | README（部分）、本文 |
| F-19 | FinMind 七年 fallback | `tools/finmind_statement_backfill.py` 可在 MOPS 大量失敗或缺欄位時，逐檔抓取 FinMind 損益表、資產負債表與現金流量表，合併為 `FinMind 合併補充` 年度資料；支援速率限制、狀態檔與斷點續跑。 | `tools/finmind_statement_backfill.py`、`StockAnalysisService.cs`、`MarketScanService.cs` | README、測試、本文 |
| F-20 | 金融公司比較與年度分析 | 只列官方產業代碼 `17`；公司摘要同盈餘年度 EPS、現金／股票股利、現金盈餘配發率、簡化 ROE、淨利及七年 EPS 資料年數皆可雙向排序、缺值置底。點一次公司即並列七年財報與配息，上一家／下一家及返回依原查詢／排序。金控合併本期淨利與歸母分開，子公司官方獲利附口徑、年度與來源，不重複加總；目前主要子公司覆蓋富邦／國泰 2024–2025。股利含公積分項、只取普通股、按所屬年度及期別去重並保留擬議狀態。未取得不當 0，不自動加入觀察。 | `Pages/Finance.*`、`Pages/Finance/Details.*`、`FinancePageData.cs`、`MopsDividendProvider.cs` | README、測試、本文 |
| F-21 | 金融股價與 5% 現金參考 | 列表顯示官方收盤與各公司交易日；可排序股價、現金殖利率與 5% 現金參考價，可篩選達 5%。現金（含公積）÷收盤計算，自行計算口徑與官方殖利率區分；面額合計不當現金或總報酬。未知／未來日期、無價、超過7日行情不入門檻；門檻用未四捨五入值。股票／股利／配息率來源和期間各自標示，行情更新只寫金融報價欄位，不覆寫更近有效收盤，能修復既有錯誤未來日期；篩選條件於公司往返保留。 | `DividendPriceCalculator`、`FinanceQuoteRefreshService.cs`、金融 Pages | README、49項測試、瀏覽器驗證 |
| F-21 | 外部名單驗證 | 使用者貼上自行整理的股票代號，系統以本機財報和目前評分模板比對年度 ROCE／品質分數；只對通過項目提供手動加入觀察的操作，不會自動搜尋媒體或新增觀察股票。 | `Pages/Discover.*`、`ExternalTargetStateFile.cs`、`StockAnalysisService.cs` | README、本文 |

## 3. 業務規則與資料邊界

### 3.1 ROCE

標準 ROCE 定義為：

```text
已動用資本 = 總資產 - 流動負債
標準 ROCE = 營業利益 / 已動用資本 × 100%
```

含利息收入版本只在利息收入存在時提供：

```text
含利息收入 ROCE = (營業利益 + 利息收入) / 已動用資本 × 100%
```

### 3.2 資料來源優先順序

- 股票目錄、最新季、估值與日行情：TWSE／TPEx 官方 OpenAPI。
- 完整年度與最新季補充：MOPS iXBRL。
- MOPS 年度資料缺漏時的批次補充：FinMind 損益表、資產負債表與現金流量表，來源標記為 `FinMind 合併補充`，正式結論仍應回查 MOPS。
- CSV：官方資料缺漏時使用；使用者勾選優先來源時，可覆蓋同期間的來源選擇。
- 價格提醒的資料庫來源是本機 SQLite；行情使用 TWSE `STOCK_DAY_ALL`、TPEx `tpex_mainboard_quotes` 或個股歷史日行情。
- 個股年度股價參考採該年度最後一個有效交易日收盤價；若該年度沒有可取得的官方日行情，顯示為缺資料，不以最新股價冒充歷史價格。

### 3.3 公司類型

金融保險業不套用一般業 ROCE 品質評分；金融專區依官方產業代碼 `17` 篩選。其他公司若因財報編製型態而被標為特殊財報，仍不因此顯示在金融專區，也不會自動加入觀察清單。ETF、ETN、權證不列入財報公司清單。這些限制是為避免把不同財報結構直接套用一般業指標。

### 3.4 本機與安全邊界

- v0.1 沒有登入、權限、多使用者、雲端同步或 remote push。
- 資料庫、狀態檔與 Data Protection keys 都是本機產物；不納入 Git。
- 外部官方資料抓取需要網路；取得失敗不應被解讀為零值。
- 價格提醒只是研究提示，不是投資建議，也不是已實作的排程推播服務。

## 4. 可驗證的第一版驗收條件

- `dotnet test RoceTool.slnx` 通過全部現有測試。
- `dotnet build RoceTool.slnx --no-restore` 通過 Web、Infrastructure、Core 與 Tests 建置。
- `python -m py_compile tools/price_roce_watch.py` 通過。
- `.gitignore` 排除 `bin/`、`obj/`、SQLite 資料庫、Data Protection keys、提醒狀態檔與 logs。
- 第一個 commit 包含可重建上述功能所需的原始碼、測試、README、設定範例、樣本資料、工具與規格文件；不包含執行時資料。

## 5. Spec／VMe 覆蓋結論

在本次盤點前，workspace 內沒有找到既有的 `Spec` 或 `VMe` 檔案，也沒有可直接引用的單一需求來源。現有文件狀態是：

- `README.md`：已列出大部分使用者可見功能、限制、CSV 格式、價格提醒與驗證命令。
- `docs/architecture.md`：已記錄模組、資料來源、主要流程與測試範圍，但不是完整產品規格。
- `RoceTool.Tests`：已覆蓋核心計算、評分、CSV、篩選與市場掃描的部分規則，但沒有取代產品規格。
- 本文件：將上述來源與目前程式碼盤點合併為 v0.1 功能基線，補上 API、技術均線、資料邊界、價格提醒限制與未實作範圍。

因此，對「目前所有功能是否已寫進 Spec 或 VMe」的結論是：原本不是；現在已透過本文件建立一份完整的 v0.1 Spec。若另有 workspace 外的 VMe 或原始 Spec，仍需在後續把它提供或放入 repo，再做一次逐項差異比對。
