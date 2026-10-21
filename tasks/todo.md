# 2026-10-15 中信金三年研究

## Acceptance criteria

- [x] 中信金（2891）研究涵蓋最近三個完整年度2023／2024／2025，年度各有業務、品質、資本觀察，數字逐筆附官方來源與版本。
- [x] 分開呈現金控、銀行、台灣人壽；不加總子公司、不混原公告與追溯EPS、不把RBC或合併淨利當可配現金。
- [x] 金融首頁有中信金三年研究入口，公司頁並列三年度，七年財報與配息情境繼續可用；缺資料明示。
- [x] 驗證來源／發行資料契約、三年覆蓋、建置及桌面／手機頁面；記錄實際限制。

## Checklist

- [x] A 核對現有研究契約與使用者偏好，定義年度2023–2025。
- [x] B 查證官方三年度獲利、銀行／壽險業務與資本配息資料（部分簡報頁碼及台壽2024投報率留缺口）。
- [x] C 加入版本化研究來源、首頁入口與三年覆蓋回歸測試。
- [x] D 建置／測試／瀏覽器驗證，更新本機服務與文件。

## Risk & Rollback

- Risk: low；延伸既有研究資料與入口，不新增依賴、資料庫schema或自動追蹤。
- 回復：移除中信金研究JSON與載入檔名／入口文案；既有股票、財報、股利與觀察清單保留。

## Dependencies & Environment

- .NET10／Razor Pages／SQLite；官網IR、年報與MOPS。2026未結束，近三年採2023–2025完整年度。
- 重建前確認並停止自己啟動的5050服務，避免DLL鎖定；不停止未知程序。

## Working Notes

- 使用者指定中信金為必要標的，不能因既有四家公司試點而略過；本輪以中信金為主。
- 官方法說中不同年度EPS可能追溯配股，需保留口徑；壽險2026制度轉換不混入完整年度比較。

## Results

- 新增中信金 2023–2025 三年度 58 筆官方來源觀察，分成業務 18、獲利品質 22、資本與配息 18；金控、銀行、台灣人壽分層，逐項保留期間、口徑、來源與限制。
- 金融首頁將中信金列為研究入口；中信金公司頁一次可看七年 EPS／配息、股價與 5% 參考、股利情境，以及三年研究對照，不需再切進另一個年度頁。
- `dotnet build RoceTool.Tests\RoceTool.Tests.csproj -c CtbcResearch --no-restore -m:1 -v:minimal` 成功，0 個錯誤；NuGet 弱點資料服務無法連線，產生 `NU1900` 警告。`dotnet test RoceTool.Tests\RoceTool.Tests.csproj -c CtbcResearch --no-build --no-restore -v:minimal`：71 通過、0 失敗。
- 瀏覽器實際檢視金融首頁與中信金公司頁：41 檔金融股、排序入口仍在；中信金研究摘要與三類資料表正確載入。390px 手機寬度無整頁水平溢出；資料表可在其容器內閱讀。瀏覽器主控台 0 錯誤、0 警告。
- 未填補台灣人壽 2024 投資報酬率與 2023–2024 FXVR 餘額等未確認資料；母公司現金／子公司上繳勾稽、完整八季重編鏈仍未完成。此研究資料不構成合理價或配息安全判斷。

# 2026-10-15 金融研究試點實作

## Acceptance criteria

- [x] 富邦、國泰、玉山、元大原公司頁增加業務來源、獲利品質、資本與配息證據；至少兩年度資料逐筆附官方來源、期間、口徑、單位與查核日。
- [x] 研究資料與既有七年財報分開保存；原公告／重編及會計制度未知時明示，不生成 Core EPS 或自行判定安全配息。
- [x] 配息情境採同盈餘年度現金股利，可調整股利及目標殖利率；歷史申報、官方證據、使用者假設分列。
- [x] 保留外層排序、一次進入即看七年、搜尋／公司切換條件；非試點公司明示研究尚未收錄。
- [x] 建置、針對性測試、來源檢查及桌面／手機操作驗證，記錄實際覆蓋與未完成的八季／資本鏈。

## Checklist

- [x] A 對照既有頁面、公式、經驗記錄與研究規格。
- [x] B 定義研究資料契約與配息情境計算。
- [x] C 取得四家公司兩年度官方證據，實作同頁研究與口徑提示。
- [x] D 執行測試／建置及瀏覽器驗證，更新文件與本機服務。

## Risk & Rollback

- Risk: medium；新增金融研究顯示與純計算。研究採版本化 JSON 隨程式發行，載入失敗明示；不覆寫既有財報、股利或追蹤。
- 回復：移除研究區塊、資料載入註冊與發行檔即可，無破壞性 schema 或資料移轉。
- 八季完整鏈、重編歷史、普通股權益與股數、母公司可匯入現金、掛牌目錄核對仍需後續補證據；不以兩年度樣本宣稱已完成。

## Dependencies & Environment

- .NET 10／Razor Pages／SQLite；既有股利為本機資料，新研究資料來自官方 IR。未知日期或口徑不補造。
- 本機發布前確認 5050 程序歸屬；保留使用者未提交修改，不自動加入觀察清單。

## Working Notes

- 子公司自身獲利不當母公司貢獻百分比；CSM、FVOCI、報表 EPS 不混算。
- 研究資料保留各期制度、文件版本及來源；不同期間／制度不自動算成長。

## Results

- 新增 Core 研究契約／配息純計算、Web JSON 載入器、公司頁研究／情境 partial 及發行檔設定。資料：富邦36、國泰45、玉山16、元大16，共113筆；四家皆有2024／2025三類證據，壽險兩家另有部分2026H1。國泰橋接76516+89350=165866（新臺幣百萬元），不換算Core EPS或核定股利。
- 來源逐筆保留版本／制度／頁碼；未知資料為null，期末日、文件基準日與未知日期分別驗證。初次整合因股利事件日與null日期契約不符失敗，已保留原始日期、修正契約並補回歸測試；不捏造12/31日期。
- `dotnet test RoceTool.Tests\RoceTool.Tests.csproj -c Release --no-restore -m:1 -v:minimal`：70通過、0失敗／略過；新增研究測試21項。建置曾因自啟服務鎖定DLL失敗，停止自己session64044後成功。
- 後續工作目錄有同步的一般股功能修改；額外Verification建置一度遇StockAnalysisService的nullable型別暫態錯誤，原檔隨後已修正，本輪未覆寫該程式。重建已成功，0錯誤；NU1900為無法連NuGet弱點服務，未完成線上套件弱點資料查詢，本輪未新增套件。
- 桌面1365／手機390檢查：四家公司各筆觀察完整載入、七年主表保留、來源版本可展開、各區定位約100px、頁面無水平溢出。研究表手機逐指標逐期排列；原年度／情境表可獨立橫捲。
- 實際操作情境年度2024、股利−30%、目標6%，富邦→國泰→返回及行情頁往返皆保留；輸入−100%正確為0股利且無正股利參考價。非法數字abc、目標0、年度2010均不試算、明示錯誤；非試點彰銀明示缺研究；台積電金融入口HTTP404。
- 原列表股價升降冪已操作核對，研究入口4家，搜尋／排序保留。正常頁面無主控台錯誤；故意abc測例有原生number輸入警告、故意404測例有預期資源錯誤，未誤當正常頁面錯誤。
- 行情往返以SQLite online backup副本驗證（5057）；正式DB五表逐列SHA256前後相同：Stocks1992、Watchlist22、FinancialStatements9971、AnnualDividends287、FinancialSubsidiaryIncomes14，quick_check=ok。證據 `.playwright-mcp/finance-research-data-audit-20261015.json`。
- 畫面：`.playwright-mcp/finance-research-cathay-20261015.png`、`finance-research-esun-20261015.png`、`finance-research-mobile-20261015.png`；README／roadmap同步。來源檔不是自動更新，重新建置／重啟後載入。
- 最新Verification建置成功，`dotnet test RoceTool.Tests\RoceTool.Tests.csproj -c Verification --no-build --no-restore -v:minimal`再次70通過、0失敗／略過。已停止5057驗證服務、檢查絕對路徑後刪除自己的副本；5050確認無監聽才啟動最新Verification版，session65167。
- 5050實際GET確認：4個研究入口、富邦36筆、三個研究區及七年主表；POST無新增寫入。實際情境GET變更−20%：現金3.4、5%價68、原5%買價殖利率4%；定位約100px。正常主站頁面主控台0錯誤／警告。最後情境畫面 `finance-dividend-scenarios-20261015.png`，本機入口已開啟／排入Codex側欄。

# 2026-07-22 專案流程循序圖

## Acceptance criteria

- [x] 以目前程式碼為依據，呈現應用程式啟動、個股分析／刷新與全市場掃描的主要循序。
- [x] 圖中標示 Web、核心服務、資料庫、官方資料源與背景工作者的責任邊界。
- [x] 圖表可讀，且沒有把未在程式碼中確認的行為當成既定流程。

## Checklist

- [x] 檢視 README、入口與核心服務，確認專案實際流程
- [x] 整理主要循序與分支，建立流程圖
- [x] 檢查圖表語法與流程對應，完成呈現

## Working Notes

- 應用程式入口為 `RoceTool.Web/Program.cs`；啟動時建立 SQLite 目錄、初始化資料庫並映射 Razor Pages 與 API。
- `StockAnalysisService` 負責目錄同步、個股分析、評分、觀察清單與 MOPS 年報刷新。
- `MarketScanWorker` 由背景服務逐檔抓取 MOPS 年報，並將進度與結果保存到 SQLite。
- 官方資料分為 TWSE／TPEx OpenAPI（目錄、最新季、估值、行情）與 MOPS iXBRL（完整年度／最新季補充）。

## Results

- 以 Mermaid sequence diagram 呈現啟動、個股分析／刷新、全市場掃描與篩選三條主流程。
- 圖中參與者與分支均對應目前 `Program.cs`、`StockAnalysisService`、`MarketScanService`、`MarketScanWorker`、官方資料提供者與 SQLite 實作。
- 驗證方式：逐一對照入口、服務方法與資料表寫入路徑；未修改產品程式碼。

## 2026-07-22 專案視覺化頁面

### Acceptance criteria

- [x] 建立 `docs/architecture.html`，包含系統架構圖、Sequence Diagram、模組依賴圖、啟動流程圖。
- [x] 每個圖中的節點都有中文說明，並能在瀏覽器正常顯示。
- [x] 建立 `docs/architecture.md`，記錄圖表範圍、程式碼依據與驗證結果。

### Checklist

- [x] 盤點 workspace 的專案入口、模組、資料來源與背景工作流程
- [x] 設計四種視覺化並建立 architecture.html 與 architecture.md
- [x] 渲染、檢查 DOM/腳本與瀏覽器實際顯示

### Results

- 新增 `docs/architecture.html` 與 `docs/architecture.md`；HTML 以四個原生 SVG 視圖和按鈕切換呈現。
- 靜態驗證：輸出內含 4 個 SVG、4 個圖表 panel、4 個切換按鈕與 4 個可存取圖片描述；內嵌 JavaScript 通過 Node 語法檢查。
- 瀏覽器驗證：本機 HTTP 載入成功，四個視圖均完成切換與快照檢查；Playwright console 為 0 errors、0 warnings。

### Working Notes

- HTML 需能離線開啟，因此圖表使用內嵌 SVG 與原生 JavaScript，不依賴外部 Mermaid runtime。
- 來源分層：Web（Razor Pages／Minimal API）→ Core（介面、模型、計算）→ Infrastructure（SQLite、官方資料、背景掃描）。
- 啟動時由 `Program.cs` 建立資料目錄、初始化資料庫、註冊 DI 與 hosted `MarketScanWorker`。

## 2026-07-26 Git v0.1 基準與 Spec／VMe 功能盤點

### Acceptance criteria

- [x] 目前可執行的程式、測試、設定、範例與文件可由第一個 Git commit 還原。
- [x] 產生 `v0.1.0` 第一版規格基線，逐項列出目前可觀察功能與程式碼依據。
- [x] 明確確認 workspace 內是否存在原有 Spec／VMe；沒有來源的部分不得假裝已覆蓋。
- [x] `dotnet test`、`dotnet build`、Python 語法檢查與 Git 差異檢查有可追溯結果。

### Checklist

- [x] 盤點 Git、檔案範圍、README、架構文件與 Spec／VMe 候選檔案
- [x] 盤點 Web 頁面、API、核心計算、資料來源、背景掃描、CSV 與價格提醒功能
- [x] 建立 `docs/spec-v0.1.md` 並更新 README 導覽
- [x] 執行測試、建置與靜態檢查
- [x] 建立第一個 commit 與 `v0.1.0` tag

### Working Notes

- 目前 `.git` 存在但 `master` 尚無 commit；沒有設定 remote，因此本次只建立本地基準，不推送外部 Git。
- workspace 內沒有檔名或內容明確標示為 Spec／VMe 的既有文件；README 是目前最完整的功能清單，`docs/architecture.md` 是架構與流程依據。
- 第一版採目前程式碼可觀察行為，不擴張產品功能；外部資料需網路，資料庫與 Data Protection keys 仍為本機執行時產物並由 `.gitignore` 排除。

### Results

- `docs/spec-v0.1.md` 已建立 18 類功能的實作對照，並明確記錄 workspace 沒有既有 Spec／VMe。
- `dotnet test RoceTool.slnx`：12 通過、0 失敗、0 略過。
- `dotnet build RoceTool.slnx --no-restore`：0 警告、0 錯誤。
- `python -m py_compile tools/price_roce_watch.py`：通過；產生的 `__pycache__` 已排除且未納入 Git。
- Web 煙霧測試：`GET /` 與 `GET /api/score-template` 均回傳 200。
- Git stage 已納入 56 個原始碼、測試、文件、設定與範例檔；未納入 logs、bin/obj、SQLite、keys、狀態檔與 Python 快取。
- 已建立本地 root commit 與 annotated tag `v0.1.0`；本次未設定 remote，也未推送。

## 2026-07-26 自由現金流每股趨勢

### Acceptance criteria

- [x] 詳情頁在既有五年 ROCE 旁新增五年 FCF/股趨勢，必要時顯示五年中位數。
- [x] 每年可回溯 `OperatingCashFlow`、`CapitalExpenditure`、`Fcf`、`WeightedAverageShares`、`FcfPerShare`。
- [x] FCF 採簡化定義：營業活動現金流量 - PPE 資本支出絕對值；無形資產支出暫不納入，並在 UI/文件明確標示。
- [x] MOPS iXBRL 年報解析、CSV 匯入與 SQLite schema backfill 都支援新增欄位。
- [x] 沒有現金流或股數資料時安全顯示缺資料，不影響既有 ROCE 評分與篩選。

### Checklist

- [x] Locate existing implementation / patterns
- [x] Design minimal approach + key decisions
- [x] Implement schema/model/calculator changes
- [x] Implement MOPS parser and CSV import support
- [x] Add details-page FCF/股 trend and statement fields
- [x] Add/adjust tests
- [x] Run verification: tests, build, targeted parser checks
- [x] Summarize changes + verification story
- [x] Record lessons if a new correction or mistake is discovered

### Risk & Rollback

- Risk level: medium.
- Affected components: `FinancialStatement` model, SQLite schema initializer, MOPS parser, CSV import, stock details UI, tests.
- Rollback strategy: revert this additive change; existing databases only receive nullable columns, so older code can ignore them if reverted.
- Monitoring signals: parser tests for cash-flow concepts, UI build, missing-data display on detail page.

### Dependencies & Environment

- Runtime: .NET 10 project with SQLite via EF Core.
- Required services for live data verification: MOPS iXBRL / official filing HTML; local tests use fixtures and do not require network.
- Seed data: existing `RoceTool.Web/App_Data/roce.db` may lack new nullable columns until `DatabaseInitializer.InitializeAsync` runs.

### Working Notes

- Existing yearly statements already live in `FinancialStatement`; keep FCF fields there to match ROCE annual-history selection and source-priority behavior.
- Existing units store monetary fields as TWD thousand; `WeightedAverageShares` should stay as shares, so `FcfPerShare` must convert FCF thousand TWD back to TWD before division.
- Do not make FCF/股 a hard score gate in this slice; it is explanatory quality evidence next to ROCE.
- MOPS samples confirmed stable cash-flow concepts for operating cash flow, PPE purchase, and intangible purchases. Weighted-average-share concepts were not stable in checked samples, so V1 estimates from `NetIncome / Eps` when no explicit shares field is available and labels the source.
- TWSE/TPEx annual cash-flow OpenAPI endpoints were not wired in this slice because checked `t187ap08` endpoints did not return the needed cash-flow JSON shape; MOPS annual filings and CSV are the supported paths for the new fields.

### Results

- Added FCF/股 fields to the annual statement model, SQLite schema backfill, MOPS parser, CSV preview/import, details UI, focus-metrics API, sample CSV, and README.
- FCF calculation uses `OperatingCashFlow - abs(CapexPpe)` and converts stored thousand-TWD FCF back to TWD before dividing by shares.
- Details page now shows five-year FCF/股 median plus per-year OCF, PPE capex, FCF, weighted shares, share source, and manual-review notes for large jumps.
- `dotnet test RoceTool.slnx --no-restore`: 14 passed, 0 failed.
- `dotnet build RoceTool.slnx -c Release --no-restore`: 0 warnings, 0 errors.

## 2026-07-26 年度 ROCE 股價參考與懸停提示

### Acceptance criteria

- [x] 最新行情仍取官方最新交易日收盤價；5、10、20、60、120、240 日均線行為不被改壞。
- [x] 七年年度資料各自取得該年度最後一個有效交易日收盤價，沒有資料時安全顯示缺資料。
- [x] 年度切換後，選定年度 ROCE 與七年 ROCE 趨勢數值都能用滑鼠／鍵盤焦點查看該年度股價參考。
- [x] 年度股價參考有快取，避免每次畫面刷新重複抓取同一年度行情。
- [x] 測試、建置、Web smoke check 通過，且不覆蓋本工作樹既有的七年／FinMind 變更。

### Checklist

- [x] 盤點目前最新收盤價、240 日均線、七年年度切換與資料來源
- [x] 設計以年度最後交易日收盤價作為歷史參考，缺資料不回退到最新價
- [x] 新增年度股價參考抓取、30 分鐘快取與核心選價規則
- [x] 在選定年度 ROCE 與七年趨勢 ROCE 加入 hover／focus tooltip
- [x] 執行測試、建置與 Web smoke check

### Working Notes

- 年度股價參考定義為曆年最後一個有效交易日的未還原收盤價；這是「該年度股價」的可重現單點，不使用平均價或最新價代替。
- TWSE／TPEx 個股歷史行情以月份查詢；每年度先查 12 月，沒有有效收盤時由 11 月往前回退。
- 年度資料是財報年度，股價參考先採同一曆年；若日後要改成財報公布日前股價，需要另訂事件日期規則。

### Results

- 新增 `AnnualPriceReference` 與核心選價規則：每年選最後一個有效交易日收盤，跨年資料不會誤用。
- TWSE 歷史月行情改用現有 Python 工具已驗證的 `/rwd/zh/afterTrading/STOCK_DAY` 路徑；TPEx 維持個股月行情端點。
- 年度價格採每年 12 月往前回退查詢，並以股票／市場／年度為 key 快取 30 分鐘；缺資料不回退到最新收盤價。
- 個股頁選定年度 ROCE 與七年 ROCE 趨勢共輸出 8 個可滑鼠懸停／鍵盤 focus 的 tooltip，顯示年度末收盤價與交易日。
- `dotnet test RoceTool.slnx --no-restore`：21 通過、0 失敗。
- `dotnet build RoceTool.slnx -c Release --no-restore`：0 警告、0 錯誤。
- Web smoke check：`GET /` 與 `/Stocks/Details/2330?year=2024` 回傳 200；頁面包含年度股價提示與 8 個 `roce-hover` 元素。
- 目前受限執行環境無法直接連線 TWSE／TPEx，因此 live page smoke 會安全顯示缺資料；provider stub test 已確認 TWSE URL 與年度末收盤選取邏輯。
- `dotnet build RoceTool.slnx --no-restore`: blocked by the existing 5050 Debug server locking DLLs, not by compile errors.
- `python -m py_compile tools\price_roce_watch.py`: passed; generated `tools/__pycache__` was removed.
- Temporary local preview on `http://127.0.0.1:5051`: `/`, `/api/score-template`, `/api/stocks?query=2330`, and `/Stocks/Details/2330` returned 200; the 2330 details HTML contained the new FCF section and `FCF/股` column.

## 2026-07-26 七年全市場資料補齊

### Acceptance criteria

- [x] 個股刷新改為最近 7 個完整年度。
- [x] 高品質篩選頁可啟動連續 7 年全市場掃描，不再只支援單一年度。
- [x] 全市場掃描會把缺 FCF 基礎欄位的既有 MOPS 年報視為待補資料。
- [x] 既有失敗掃描可在七年補齊時重新嘗試，不被舊失敗紀錄永久卡住。
- [x] 文件與 UI 的「五年」語意更新為「七年」。
- [x] 啟動目前 2025 至 2019 的補資料流程，並回報背景掃描狀態。

### Checklist

- [x] Inspect current scan/year selection behavior and local database coverage
- [x] Implement seven-year refresh and range scan state
- [x] Update screen UI, details labels, README/spec wording
- [x] Add or adjust tests for range scan and FCF completeness
- [x] Run verification: tests, build, smoke check
- [x] Start seven-year backfill and record current status

### Risk & Rollback

- Risk level: medium.
- Affected components: `MarketScanState`, scan worker, screen page, individual stock refresh, SQLite schema backfill, documentation.
- Rollback strategy: revert code; added nullable scan range columns can be ignored by older code.
- Monitoring signals: scan status row, `MarketScanItems` counts by year, annual statement counts by year, data errors.

### Working Notes

- Current local annual coverage: 2025 and 2024 are nearly complete; 2023 and older are sparse.
- Existing `MarketScanItems` has many 2023 failures; a seven-year fill needs a retry path or those failures will keep 2023 marked completed.
- Existing 2025/2024 MOPS records were fetched before FCF fields existed, so the scanner must treat missing `OperatingCashFlow` or `CapexPpe` as incomplete for the new backfill.
- An old `5050` worker was still running pre-change code and briefly marked the new range scan complete; stopping it and restarting the new Release worker on `5050` fixed the race.

### Results

- Individual stock refresh now fetches the latest 7 complete fiscal years.
- Market scan state now stores `RangeStartFiscalYear` / `RangeEndFiscalYear`, and the scan page defaults to latest year + 7 continuous years.
- `/api/market-scan/start-range/{latestYear}/{years}` can start the same multi-year backfill programmatically.
- The scanner treats MOPS annual reports without `OperatingCashFlow` or `CapexPpe` as incomplete, so old reports are revisited to fill FCF data.
- Seven-year backfill was started for 2025 through 2019 on `http://127.0.0.1:5050`; latest checked status was running with total 12,418 work items and progress continuing.
- `dotnet test RoceTool.slnx --no-restore`: 15 passed, 0 failed.
- `dotnet build RoceTool.slnx -c Release --no-restore`: 0 warnings, 0 errors.
- `python -m py_compile tools\price_roce_watch.py`: passed; generated `tools/__pycache__` was removed.
- Smoke check: `GET /api/market-scan/status` on `5050` returned running range status for 2025 to 2019; `GET /Screen` loaded after restart.

## 2026-07-26 全市場掃描小批次加速

### Acceptance criteria

- [x] 掃描可用受控批次一次抓多檔 MOPS 年報。
- [x] SQLite 寫入仍集中在單一流程，避免多 worker 搶寫同一筆。
- [x] 批次大小可由設定調整，預設保守，不做無限制並發。
- [x] 重新啟動服務後 2025 至 2019 七年補資料可從現有進度續跑。

### Checklist

- [x] Inspect current running status and failure pattern
- [x] Add configurable scan batch size and delay
- [x] Refactor worker to fetch a small batch concurrently and write results sequentially
- [x] Run tests/build
- [x] Restart local service and confirm progress resumes

### Risk & Rollback

- Risk level: medium.
- Affected components: `MarketScanWorker`, appsettings, seven-year backfill runtime behavior.
- Rollback strategy: set `MarketScan:BatchSize` back to `1`, or revert the batch worker change.
- Monitoring signals: scan status progress, failed count, MOPS/network errors, SQLite write errors.

### Results

- Added `MarketScan:BatchSize` and `MarketScan:DelayMilliseconds`; current defaults are batch size 3 and 1 second between batches.
- Worker now selects a small pending batch, fetches MOPS reports concurrently, then writes DB updates sequentially through one `DbContext`.
- Restarted `5050` with the batch scanner; 7-year scan resumed from SQLite state.
- Status check after restart: progress moved from 393 to 477 completed items in about 30 seconds, with no observed SQLite write errors.
- `dotnet test RoceTool.slnx --no-restore`: 15 passed, 0 failed.
- `dotnet build RoceTool.slnx -c Release --no-restore`: 0 warnings, 0 errors.

## 2026-07-26 FinMind 七年 fallback 補資料

### Acceptance criteria

- [x] 全市場掃描完成判斷改為年度資料同時具備 ROCE 與 FCF 所需欄位，不限 MOPS 單一來源。
- [x] 新增 FinMind 逐檔七年補資料工具，能從損益表、資產負債表、現金流量表合併成可用年度資料。
- [x] 補資料工具每檔只抓一次 2019-2025 區間，支援速率限制、狀態檔與斷點續跑。
- [x] FinMind 補充資料不覆蓋 MOPS 原始列，並以 `SourceName` 清楚標記資料來源。
- [x] 用少量樣本驗證 API 欄位解析、SQLite 寫入與掃描狀態計算。
- [x] 重新啟動本機服務並啟動長時間 fallback 補資料。

### Risk & Rollback

- Risk level: medium.
- Affected components: scan completeness rules, annual source selection, local SQLite data backfill, background runtime.
- Rollback strategy: stop fallback process, remove or ignore `SourceKind=FinMind` rows, revert source-selection and scan-completeness changes.
- Monitoring signals: `/api/market-scan/status`, `MarketScanItems` success/failure counts, FinMind state/log files, DB rows with `SourceName='FinMind 合併補充'`.

### Working Notes

- MOPS worker is still progressing but success rate is low because many requests return security pages or lack cash-flow concepts.
- FinMind free tier requires `data_id` for these datasets, so fallback must run per stock. Without token, default rate limit should be conservative.
- `python tools\finmind_statement_backfill.py --max-stocks 1 --sleep-seconds 0.2` confirmed one stock can upsert 7 annual rows and mark 7 scan years complete.

### Results

- Added `SourceKind.FinMind` and changed scan completeness to require annual ROCE + FCF inputs rather than MOPS-only source.
- Annual source selection now prefers complete ROCE/FCF rows, so `FinMind 合併補充` can be selected when MOPS lacks cash-flow fields.
- Added `tools/finmind_statement_backfill.py`; it fetches FinMind income, balance sheet, and cash flow per stock for the selected year range, merges with existing annual rows, writes state to `RoceTool.Web/App_Data/finmind_backfill_state.json`, and logs to `RoceTool.Web/App_Data/finmind_backfill.log`.
- Local service was restarted on `http://127.0.0.1:5050` during verification; the final restart attempt was blocked by Codex approval usage limits after the service was stopped.
- MOPS seven-year scan reached database status completed: total 12,418, completed 12,418, success 625, failed 11,793.
- FinMind backfill is running with PID 32984; latest checked state was 63 processed stocks, 439 upserted years, 427 complete years, 0 errors.
- A legacy `MarketScanItems.Success=1` row for `1409`/2025 lacked the new required fields; it was reclassified as attempted failed, and code now counts any attempted incomplete row as failed.
- `python -m py_compile tools\finmind_statement_backfill.py`: passed.
- `dotnet test RoceTool.slnx --no-restore`: 18 passed, 0 failed.
- `dotnet build RoceTool.slnx -c Release --no-restore`: 0 warnings, 0 errors.
## 2026-10-13 Razor 頁面切換效能與 AJAX 載入

### Acceptance criteria

- [x] 頁面間導覽以 AJAX 軟切換，保留 URL、返回／前進、表單與一般重新載入 fallback。
- [x] 頁面 GET 不等待可延後的外部行情／年報請求；初始內容先顯示，資料再非同步補上。
- [x] 不改動既有財報計算、篩選條件或資料來源優先序。
- [x] 完成建置檢查；本機伺服器無法連線時記錄未完成的瀏覽器實測。

### Checklist

- [x] Locate page handlers and identify blocking network work
- [x] Move stale catalog refresh off ordinary page requests
- [x] Move watchlist/detail price retrieval to AJAX after initial render
- [x] Add same-origin soft navigation with history and fallback behavior
- [x] Run build and inspect diff for compatibility

### Working Notes

- `Stocks/Details` GET currently may fetch seven annual MOPS reports and several quarter reports before responding, then fetches daily and annual price histories.
- `Index` GET fetches annual price history for every watchlist item; ordinary pages may also synchronously refresh the catalog after its one-day TTL.
- AJAX navigation alone does not shorten server work, so defer those external-data calls from the HTML response as part of this change.

### Results

- `_Layout` 導覽連結與一般表單改用同源 `fetch` 載入，更新主內容與標題，支援瀏覽器上一頁／下一頁；失敗時回退完整頁面導覽。
- 過期公司清單改在背景刷新；首頁年度行情改為展開區塊時載入，個股均線與年度估值在頁面顯示後載入。
- 個股詳情 GET 不再同步抓行情或補 MOPS 年報；缺少欄位時由頁面載入後透過 AJAX 背景補抓，原有手動刷新仍保留。
- `dotnet build RoceTool.Web/RoceTool.Web.csproj -c Release --no-restore -m:1 -v:minimal`：成功，0 警告、0 錯誤；`node --check RoceTool.Web/wwwroot/js/site.js` 通過；`git diff --check` 無差異錯誤。
- 尚未完成瀏覽器實測：此執行環境連線本機 `127.0.0.1:5050` 被 sandbox 拒絕（socket access denied）。

## 2026-10-13 高品質篩選查看連結中斷

### Acceptance criteria

- [x] 掃描進行時，點擊結果列「查看」不再被舊頁完整重載取消。
- [x] 掃描進度改為輪詢狀態 API，不重載整個高品質篩選頁。
- [x] 離開高品質篩選頁後，中止舊頁的進度輪詢。

### Checklist

- [x] 在本機掃描進行中重現「查看」請求被中斷
- [x] 找到掃描頁每五秒觸發完整 AJAX 重載，會 abort 前一個導航請求
- [x] 改為只輪詢掃描狀態並更新進度區
- [ ] 在更新後的執行中網站驗證掃描中「查看」可打開個股詳情
- [x] 建置並檢查差異

### Working Notes

- 實測第一列 `3529` 的詳情 GET 有送出，但頁面仍停留在 `/Screen`；掃描頁的 5 秒 `roceNavigate` 會用相同全域導航器取代並 abort 當前請求。
- 本機掃描狀態 API 可正常回應；每次整頁重載會重跑整個 `ScreenAsync` 與結果表渲染。
- 使用者確認要新增金融業專區頁，顯示金融／特殊業公司清單與財報摘要，不套用一般業 ROCE 分數。

### Finance section checklist

- [x] 新增金融業專區導覽項目與頁面
- [x] 顯示可搜尋的金融／特殊業公司、最新年度與財報摘要
- [x] 每筆摘要可連到個股詳情；金融詳情頁顯示 ROE、EPS、淨利、利息收入、資產與權益，不顯示一般業 ROCE／季度評分區

### Results

- 新增「金融專區」導覽與 `/Finance` 頁，列出支援財報的金融／特殊業公司，可依代號、名稱或產業搜尋；摘要顯示年度、EPS、ROE、淨利、資產與來源。
- 金融公司詳情頁補上年度財報摘要，並略過一般業 ROCE 與品質分數區塊。
- 高品質篩選頁改為每五秒只更新掃描狀態與進度；點擊其他頁面時會取消狀態輪詢，不再讓舊頁的計時器 abort 查看連結。
- `dotnet build RoceTool.Web/RoceTool.Web.csproj -c Release --no-restore -m:1 -v:minimal`：成功，0 警告、0 錯誤。
- `node --check RoceTool.Web/wwwroot/js/site.js` 與 `git diff --check` 通過。
- 尚未在更新後的執行中網站完成瀏覽器確認；目前 `127.0.0.1:5050` 仍載入舊版本，為保留進行中的掃描未重啟該服務。重啟後即可看到新選單與頁面。

## 2026-10-13 背景單次資料收集工具

### Acceptance criteria

- [x] 一次啟動最近七年全市場年報掃描，不必開啟瀏覽器或等待頁面。
- [x] 掃描進度與結果寫入既有 SQLite，背景工作完成後工具可結束。
- [x] 已有掃描執行時不重複啟動，改為監看既有進度。
- [x] README 記載一鍵啟動、前景監看、日誌位置與資料範圍。

### Checklist

- [x] 檢查既有掃描 API、HostedService 與續跑狀態
- [x] 建立背景啟動器，必要時隱藏啟動網站 Host，完成後只停止自己啟動的 Host
- [x] 更新 README 使用方式與增量掃描範圍
- [x] 驗證 PowerShell 語法、建置與安全的啟動流程檢查

### Working Notes

- `MarketScanWorker` 已獨立於瀏覽器頁面執行，但需要網站 Host 存活；掃描狀態與每檔結果存於 SQLite。
- 掃描只補齊指定年度範圍缺少或不完整的年報，不會強制覆寫已完整年度，也不更新股價快取。
- 同一個 SQLite 資料庫不可同時啟動第二個掃描器；背景啟動器先讀狀態，已有 Running 時只監看。

### Risk & Rollback

- Risk level: low；新增本機 PowerShell／CMD 工具與文件，不變更資料表或既有 API。
- Rollback: 移除新增工具檔並還原 README 對應段落。
- Dependencies: Windows PowerShell、.NET 10 SDK、同一個本機 SQLite 資料庫與 MOPS 網路連線。

### Results

- 新增 `tools/start_market_scan_background.cmd` 一鍵入口與 PowerShell 背景監看器；預設掃描最近七年，全程不必開啟瀏覽器。
- 若網站已執行，工具透過本機掃描 API啟動或監看既有工作；若網站未執行，建置並隱藏啟動 Host，掃描完成後停止該 Host。
- 進度寫入 `RoceTool.Web/App_Data/market-scan-background.log`；工具使用獨佔鎖，避免多個工具實例同時啟動掃描。
- PowerShell Parser：語法通過；外部 URL 防護檢查：拒絕非本機 HTTPS 網址。
- `dotnet build RoceTool.Web/RoceTool.Web.csproj -c Release --no-restore -m:1 -v:minimal`：成功，0 警告、0 錯誤；README 與工作紀錄 `git diff --check` 通過。
- 未實際啟動全市場收集：該流程會改寫本機財報資料並可能執行數小時；啟動方式與狀態處理已檢查，第一次執行由使用者的一鍵操作開始。

## 2026-10-13 金融股年度財報盤點與補齊

### Acceptance criteria

- [x] 依金融專區實際呈現欄位定義年度核心完整度，逐股票與年度盤點。
- [x] 以 MOPS 年報補缺，保留一般股掃描範圍與金融業不計 ROCE 的規則。
- [x] 寫入前建立可還原的 SQLite 備份；補資料採逐筆 upsert，避免覆蓋其他來源。
- [ ] 完成後回查年度涵蓋率、核心欄位與明細欄位，列出仍缺與抓取失敗的股票。

### Checklist

- [x] 確認金融專區欄位、既有資料來源與掃描工作狀態
- [x] 盤點 2019–2025 支援金融股年度資料缺口
- [x] 建立可續跑的金融年報補資料工具與單檔 dry-run
- [x] 備份 SQLite 並啟動缺漏金融股年報收集
- [ ] 【進行中】完成全部金融股歷史年報收集
- [ ] 回查寫入筆數、年度完整度及失敗清單，重試暫時性失敗
- [x] 更新 README 與金融專區核心完整度提示

### Working Notes

- 目前資料庫支援金融／特殊業股票 209 家；2019–2025 年度財報均未有任何一筆。2026 年已有 169 家的 Q1/Q2 資料，但不屬於年度報告。
- `/Finance` 依年度列出 EPS、ROE、淨利與資產；核心完整度以 NetIncome、TotalEquity、Eps、TotalAssets 同列非空為準。營收、利息收入、負債及現金流另列欄位覆蓋率，避免金融業未提供特定指標時誤判核心摘要。
- `RefreshStockAsync` 明確略過非一般股；`MarketScanWorker` 只掃描一般股。本工作使用獨立資料路徑，不改變通用 ROCE 評分或掃描狀態。
- SQLite `roce.db` 有 WAL 檔且可能仍由網站開啟；寫入前用 SQLite 線上備份 API 建立一致性備份，確認全市場掃描狀態非 Running，並以獨佔鎖避免重複金融回填。
- 官方 MOPS 的 2882／2025 年報唯讀探測回應 HTTP 200，HTML 約 818 KB；單檔 dry-run 解析出 10/14 欄位，淨利、權益、EPS、總資產及利息收入皆有值。
- 正式掃描每批抓取 3 筆並逐筆儲存；截至 2026-10-13 16:05:23，已處理 99/1,463 個股票年度，資料庫有 94 個核心完整年度；MOPS timeout／無法解析項目待後續重試。
- 一般股掃描仍是 Paused；一般股 2019–2025 年 ROCE/FCF 五欄完整度分別為 532、108、107、110、111、112、207 / 1,783。既有 12,451/12,481 次嘗試中，11,080 次訊息為「找不到完整年度年報」，不可把嘗試完成數當成資料完整數。

### Risk & Rollback

- Risk level: medium；逐筆新增／更新金融股年度財報至既有 SQLite，來源為官方 MOPS，無刪除與 schema 變更。
- Rollback: 以補資料前建立的 `RoceTool.Web/App_Data/roce-finance-backup-<timestamp>.db` 還原 `roce.db`（需先停止網站 Host），或逐筆移除本次新增且 `SourceName = 'MOPS iXBRL'` 的指定年度列。
- Dependencies: .NET 10 SDK、SQLite WAL、官方 MOPS HTTPS 連線；預設範圍 2019–2025，逐筆執行並可從資料庫現況續跑。

### Current Results

- `tools/FinanceBackfill` 已建置；2882／2025 dry-run 核心四欄完整，解析 10/14 欄位。
- 背景回填已開始；寫入前備份 `roce-finance-backup-20261013-155138.db` 通過 SQLite `quick_check`。
- 截至 2026-10-13 16:05:23，已新增 94 個核心完整金融／特殊業股票年度；全數完成與最後覆蓋率待背景工作結束後回查。

## 2026-10-14 使用流程調整：年度匯出、評分解釋、金融名單與月度發現

### Acceptance criteria

- [x] 資料管理可選擇財報年度並匯出該年度 CSV，欄位可供閱讀與沿用現有匯入格式。
- [x] 評分設定逐項說明指標意義、計算公式、權重比例代表的影響與預設比例設計理由。
- [x] 金融專區只列出金融保險業，不把其他特殊編製類型公司當金融股顯示。
- [x] 月度發現改成名稱與操作清楚的外部名單驗證流程，說明資料由使用者提供、結果如何使用。
- [x] Web 建置通過；檢查 CSV 年度篩選與公式／名單邊界，保留目前資料匯入、評分與篩選行為。

### Checklist

- [x] 閱讀網站修改指引、現有工作紀錄及四個功能頁的程式流程
- [x] 核對金融產業代碼／標籤與目前金融專區的分類依據
- [x] 實作年度 CSV 匯出與明確的欄位／單位標示
- [x] 加入指標定義、公式與權重解讀
- [x] 修正金融專區篩選並釐清「月度發現」的實際用途
- [x] 執行 Release 建置與必要的靜態查核
- [x] 更新 README／規格文件及本節 Results

### Working Notes

- 使用者要的是既有本機網站調整；不涉及 Sites 註冊或部署。
- CSV 匯入以 ThousandTWD 保存金額；匯出應選擇單一 FiscalYear，包含 FY 與 YTD 列及來源名稱。
- 目前月度發現實際是驗證使用者從外部收集的股票清單，不會自動搜尋媒體或產生新候選名單；導覽名稱與功能不符。
- 財報編製型態分類會把部分非金融產業標成 Financial；需以官方金融保險產業代碼收窄 Finance 頁顯示範圍，不能無意間改變其他頁既有篩選規則。
- 外部產業代碼 `17` 是現有官方目錄映射中判斷金融保險業的代碼；Finance 頁只收窄顯示條件，保留其他頁面依 `StatementKind` 排除一般 ROCE 的既有規則。

### Risk & Rollback

- Risk level: low to medium；影響資料管理匯出、設定說明與 Finance／Discover 顯示，不改動評分計算或財報資料。
- Rollback: 還原本次涉及的 Razor Pages、頁面模型、導覽、樣式及文件變更。

### Results

- 資料管理新增年度選單；匯出該年度所有 FY／YTD 與不同來源資料，沿用現有 CSV 匯入欄位，金額標示為 `ThousandTWD`。
- 評分設定新增五項指標的簡易意義、公式、預設權重設計考量，以及加權總分／缺漏覆蓋率說明；評分公式與預設權重未改動。
- 金融專區加上官方產業代碼 `17` 條件，特殊財報編製型態不再單獨使非金融產業出現在該頁。
- 導覽與頁面改稱「外部名單驗證」，明說名單由使用者提供、不會自動搜尋或加入觀察；結果顯示公司清單未命中的數量，只讓通過門檻的股票提供手動加入操作。
- 更新 README 與規格基線的對應功能描述。
- `dotnet build RoceTool.Web/RoceTool.Web.csproj -c Release --no-restore -m:1 -v:minimal -p:RestorePackagesPath='C:\Users\zx304\.nuget\packages'`：成功，0 錯誤；1 個 `NU1900` 警告，因目前網路無法取得 NuGet 弱點資料。
- `git diff --check`：通過，沒有空白錯誤。
- `dotnet test RoceTool.slnx -c Release --no-restore`：未能進入測試；舊 `RoceTool.Tests/obj/project.assets.json` 指向 `C:\Users\weichen\.nuget\packages`，目前環境的套件位於 `C:\Users\zx304\.nuget\packages`。測試專案的 restore graph 也未成功完成，因此測試結果尚未驗證；網站 Release 建置已涵蓋本次 Razor 與頁面模型編譯。

## 2026-10-14 金融專區排序與年度 EPS 比較

> 此版將七年 EPS 放在公司列表的設計已由使用者退回；後續以「可排序公司摘要與公司年度詳情」取代。此節保留實作紀錄，不代表使用者接受。

### Acceptance criteria

- [x] 金融專區可依股票代號或指定年度 EPS 排序，搜尋條件可保留。
- [x] 金融專區表格以最近七個年度 EPS 橫向比較為主，缺漏年度清楚顯示，並保留公司詳情入口。
- [x] 其他個股頁與 ROCE 評分流程不受影響。

### Checklist

- [x] 確認現有金融專區只顯示最新年度摘要且沒有排序控制。
- [x] 確認資料庫年度 EPS 範圍為 2019–2025，存在部分年份缺漏。
- [x] 建立金融專區年度 EPS 比較表與排序選項。
- [x] 建置網站並檢查差異與頁面查詢參數。

### Working Notes

- 年度資料須按股票與年度分組，沿用金融專區現有 `ChooseSource` 優先順序選擇同年度來源。
- 預設依最新可用的完整年度 EPS 由高至低排序，並提供逐年度 EPS 與股票代號排序。
- 以資料庫最新完整年度為終點並列最近 7 個年度；無該年度資料使用「—」，不可把缺漏當成 0。

### Risk & Rollback

- Risk level: low；僅變更金融專區頁面模型、Razor 表格、局部 CSS 與功能文件，不改資料、不動一般股評分。
- Rollback: 還原 `Finance.cshtml`、`Finance.cshtml.cs`、`site.css`、README、功能規格及本節紀錄。

### Results

- 金融專區新增股票代號與逐年度 EPS 高至低排序；搜尋與排序共用 GET 表單，套用排序時保留搜尋字串。
- 表格改為最近七年 EPS 對照，依股票／年度沿用來源優先順序取值，空白年度標示「—」，並顯示 EPS 資料年數、最新年報及個股詳情連結。
- README 與功能規格同步更新。
- `dotnet build RoceTool.Web\RoceTool.Web.csproj -c Release --no-restore -m:1 -v:minimal -p:RestorePackagesPath='C:\Users\zx304\.nuget\packages'`：成功，0 警告、0 錯誤。
- `git diff --check`：通過；Git 僅提示既有 LF 檔案未來轉換 CRLF。
- 未執行測試。

## 2026-10-14 金融專區：可排序公司摘要與公司年度詳情

### Acceptance criteria

- [x] 外層每家公司一列摘要，使用全金融名單的同一比較年度；缺該年度不回退到其他年份。
- [x] 可點表頭對公司、市場、EPS、簡化 ROE、淨利及 EPS 資料年數切換升冪／降冪；缺值置底且保留搜尋條件。
- [x] 公司連結進入金融專用詳情，內層並列七個年度的 EPS、連續年度差額與來源，可切換年度財報。
- [x] 無年報、缺 EPS、缺中間年度及不存在公司皆有明確處理；從詳情返回保留原列表條件。
- [x] 金融詳情以盈利與年度報表為主，不展示一般股 ROCE 評分；現有個股頁提供金融詳情入口。

### Checklist

- [x] 閱讀金融列表、個股金融分支、來源選擇與導覽流程，記錄上一版誤解。
- [x] 實作摘要排序及金融股年度詳情。
- [x] 更新樣式、README、規格文件與 lessons。
- [x] 驗證來源選擇、同年度排序、缺值、返回條件及年度切換；建置與桌面／窄螢幕檢查。

### Working Notes

- 原金融列表七年 EPS 欄位過寬；原個股金融分支只有最新年度摘要，沒有年份切換，且均線先於財報。
- 外層的比較年度與七年視窗應取全部金融名單的年度上限，不因搜尋結果改變。
- 簡化 ROE 沿用淨利／期末權益，清楚標示口徑；EPS 缺漏維持 null，不當成 0。
- 金融頁來源優先保留使用者指定，再看 EPS／金融核心欄位與官方來源；兩層共用選擇規則。
- 本次不抓外部資料、不改財報或股票目錄；UI 驗證以 SQLite 副本執行。

### Risk & Rollback

- Risk level: low to medium；新增金融詳情路由，修正列表排序與比較年度顯示，無 schema 或資料寫入。
- Rollback: 還原本節涉及的金融 Pages、共享金融資料選擇、個股金融入口、CSS、文件與驗證程式。

### Dependencies & Environment

- .NET 10、ASP.NET Razor Pages、EF Core SQLite；頁面使用本機年度財報，不要求即時官方 API。
- 測試專案新增 Web 專案參照以驗證 Razor Page 模型與金融資料選擇；以本機 NuGet 快取完成離線 restore，修正舊 assets 的其他使用者路徑。
- 沙箱測試主機初次因 VSTest／testhost 本機通訊逾時（90 秒）中止；改用可進行本機通訊的環境後通過。預覽改放工作區內，避免不同沙箱暫存資料夾的存取限制。

### Results

- 外層 `Finance.cshtml` 改為同年度摘要，六個表頭支援雙向排序、缺值置底；預設 EPS 降冪。比較年度與七年視窗由整個金融名單決定，搜尋不改變基準。
- 新增 `/Finance/Details/{stockNo}`；歷年 EPS 以七列呈現，可選有資料或缺資料的年度，查看財報明細、原始來源與取得時間；返回保留 Query／Sort／Direction。現有金融個股頁補上專用詳情入口。
- `FinancePageData.cs` 共用金融範圍、來源與排序；保留使用者指定來源，再優先 EPS／金融核心欄位、MOPS 與來源時效。EPS 差額只比較相鄰曆年，零 EPS 仍計為有效資料。
- 新增 `FinancePageTests.cs`：金融頁面 8 個案例通過；全測試 `dotnet test RoceTool.Tests\RoceTool.Tests.csproj -c Release --no-build --no-restore -v:minimal`：32 通過、0 失敗、0 略過。
- `dotnet build RoceTool.Web\RoceTool.Web.csproj -c Release --no-restore -m:1 -v:minimal`：成功，0 警告、0 錯誤；本次追蹤檔案 `git diff --check` 通過。
- Playwright 以 SQLite 副本檢查：六欄共 12 次升／降冪排序符合數值順序；搜尋 288 → ROE 排序 → 富邦金 → 缺漏 2024 → 已收錄 2022 → 返回，原查詢／排序完全保留。
- 桌面 1365px 與手機 390px：頁面本體無橫向溢出、表格獨立橫捲、公司欄固定；手機切到國泰金 2024 年 EPS 顯示 7.29。鍵盤 Enter 可排序、方向鍵可橫捲；瀏覽器主控台 0 錯誤／警告。
- 宏遠證無年度資料時仍顯示 2019–2025 七個缺漏年度與資料管理入口，未回退或假造數值。
- 畫面證據：`.playwright-mcp/finance-overview-20261014.png`、`.playwright-mcp/finance-company-20261014.png`。README、規格與需求誤解 lessons 已同步。
- 驗證預覽 Host 已停止，暫存 SQLite 副本與執行檔已清除，僅保留畫面證據。

## 2026-10-14 金融年報補漏、股利與獲利口徑

### Acceptance criteria

- [x] 富邦金等金融股缺年度先確認官方來源，補入實際取得的年報，不把暫時抓取失敗當作不存在。
- [x] 外層保留可排序公司摘要，加入有年度口徑的普通股配息；現金／股票股利與殖利率區分。
- [x] 點一次公司即可並列年度 EPS、股利及財報核心欄位；同頁切公司／返回列表保留原條件。
- [x] 金控分析標示合併與歸屬母公司淨利，納入已取得的子公司獲利來源，不重複加總。
- [x] 缺漏、資料口徑、來源與驗證範圍明確；其他股評分與使用者追蹤資料不受影響。

### Checklist

- [x] A 理解現有資料缺漏、官方資料來源及上下層導航。
- [x] B 修正年報取得與來源口徑，取得股利／子公司獲利並安全回填。
- [x] C 改善公司摘要與單層年度工作頁，加入回歸測試。
- [x] D 建置、測試與瀏覽器確認，記錄結果及限制。

### Working Notes

- 先前回填已結束，120 筆無法解析、113 筆請求例外；須區分官方無資料與請求失敗。
- 股利以盈餘年度對照 EPS，並註明實際發放年度；不得用目前殖利率推算歷史配息。
- 金控合併獲利已包含子公司，子公司數據只用於組成分析，不再加進合併淨利。
- 年度比較放在公司主頁直接展示，選年度只調整同頁補充明細。

### Risk & Rollback

- Risk: medium；年報解析、金融頁面與新增資料表，既有來源及使用者指定資料保留。
- 資料寫入前以 SQLite online backup 保存一致快照；按公司／年度／來源 upsert，無刪除或追蹤變更。
- Rollback: 還原本次頁面／解析程式；新增表可保留不用，回填資料按來源與取得時間辨識；完整 DB 回復需停止程式後使用備份，避免覆蓋後續使用者資料。

### Dependencies & Environment

- .NET 10、EF Core SQLite、MOPS／TWSE／TPEx與公司投資人關係公開資料；外部請求需保留 TLS 驗證、限時與重試上限。

### Results

- 年報缺漏來源：富邦 2021／2023／2024 前次逾時；四家上櫃券商前次 TLS 連線失敗。現有 iXBRL 標籤／context 正常，保留 TLS 驗證，增加 65 秒逾時及一次有界重試。
- 實際回填缺漏 62 筆：首輪 61 成功，上海商銀 2022 再試成功。現有產業代碼 17 名單 41 家的 2019–2025 共 287 個年度核心欄位已收錄；此為本機名單覆蓋，仍含先前已說明的下市三商壽舊目錄列，不等於 41 家均仍掛牌。
- 股利取自 MOPS 新版 `ajax_t05st09_2`、`qryType=2`，普通股共 287 筆（41 家 × 7 年）。保留盈餘／公積、現金／股票分項及擬議狀態；不推算除息日或入帳日。富邦 2025 盈餘年度現金 4.25，股票 0；國票金 2025、康和證 2024 仍標示含擬議。
- 富邦與國泰七年 MOPS 歸母淨利已補齊；原 EPS／來源／使用者偏好保留。新增欄位與原財報來源口徑核對一致；新舊核心數值或報表口徑不同時拒絕混入。合併本期淨利與歸母淨利並列，不將子公司淨利再加進母公司。
- 子公司獲利候選與實際入庫 14 筆：富邦／國泰 2024、2025 的主要子公司官方法說來源，逐筆保存損益口徑。未宣稱全部金融公司／七年獲利組成都完整；不做未知匯率換算、加總差額或貢獻百分比。
- 公司頁直接顯示七年 EPS／股利／淨利／權益與其他財報欄位，無年度第二層頁面；列表九個表頭可雙向排序；上一家／下一家依原排序及搜尋切換。行情頁來回也保留條件。原個股金融入口直接導向專用分析。
- 修正 `site.js`：相同頁面的 native hash `popstate` 不再重新抓頁並重設捲動；真正換公司／回上一頁仍更新正確內容。
- 完整股利不會被缺分項的新回應覆寫，官方查詢失敗保留舊資料；公司頁的股利更新已用資料庫副本實際 POST 成功，7 年更新並保留原搜尋／排序。
- `dotnet build RoceTool.Tests\RoceTool.Tests.csproj -c Release --no-restore -m:1 -v:minimal`：網站、Infrastructure、Core 與測試建置成功，0 警告／錯誤；兩個回填工具也已成功建置。
- `dotnet test RoceTool.Tests\RoceTool.Tests.csproj -c Release --no-build --no-restore -v:minimal`：42 通過，0 失敗、0 略過；包含普通／特別股、年度、分期去重、null、配發率、雙向排序、來源口徑、相容 schema、資料保留。
- 瀏覽器：桌面 1365px 與手機 390px，九欄共 18 次排序、單次進公司看到七年、富邦→國泰→返回、行情來回及 native hash／上一頁皆已驗證。表格獨立橫捲、鍵盤 Enter 排序、方向鍵橫捲；頁面本體無水平溢出。區塊頂端約 100px、導覽底緣約 79px，無遮擋；主控台 0 錯誤／警告。
- SQLite `quick_check=ok`；線上備份後寫入。對照首個備份：Stocks、Watchlist、範圍外財報差異皆 0；資料稽核 `.playwright-mcp/finance-data-audit-20261014.json`。畫面證據 `.playwright-mcp/finance-overview-income-20261014.png`、`.playwright-mcp/finance-company-income-20261014.png`。
- 驗證使用 SQLite 副本；不代表正式部署。預覽服務與暫存副本收尾清除，保留官方診斷回應與畫面證據。
- 已另外啟動本機主站 `http://127.0.0.1:5050/Finance` 供使用者查看（exec session 85411）；實際主站 GET 確認 41 家七年 EPS 齊全、富邦七年資料與現金股利 4.25，無額外年度第二層。資料副本的驗證服務已清除。

## 2026-10-14 金融股價與 5% 配息參考

### Acceptance criteria

- [x] 公司列表直接顯示官方收盤股價、交易日期與來源，股價可排序。
- [x] 顯示現金殖利率與達到 5% 的現金股利參考價，可排序／篩選；計算採同盈餘年度股利。
- [x] 股利面額合計另作參考，現金／股票分開說明；不把官方不同口徑殖利率直接拿來判斷現金 5%。
- [x] 無股價、交易日期未知、零／負值、股利缺漏／擬議都有明確處理；舊價格不冒充即時行情。
- [x] 公司切換、返回列表、行情往返仍保留查詢與排序條件；測試、建置及瀏覽器驗證通過。

### Checklist

- [x] A 確認股價資料覆蓋與官方交易日來源，定義殖利率口徑。
- [x] B 實作計算與列表／詳情價格區塊，補入最新官方價格。
- [x] C 驗證數值、排序、缺值、篩選及手機／桌面操作。
- [x] D 重啟自己啟動的本機服務，交付可直接檢視的版本與限制。

### Working Notes

- 使用者更正：股利與 EPS 已展示，但股價藏在行情頁，無法在金融比較頁判斷配息／價格關係。
- 現金殖利率 = 普通股現金股利（含公積現金）÷ 最新可用官方收盤價 × 100%；5% 參考價 = 現金股利 ÷ 0.05。
- 股票股利另列；「現金＋股票配發面額」僅為股利合計參考，不能當實際現金收益或總報酬。
- 5% 為使用者觀察門檻；已申報年度股利不保證未來維持，來源不提供實際入帳日。

### Risk & Rollback

- Risk: medium；金融顯示與價格更新。沿用既有官方價格來源，保留來源／交易日，不改財報、追蹤或評分。
- 寫入前 online backup，僅更新金融股票的行情欄位；官方錯誤保留舊資料。還原新增頁面／計算程式即可回復 UI，行情備份保留。

### Results（2026-10-15）

- 新增收盤、現金殖利率、5% 現金參考價三個排序欄與只看達5%篩選；公司頁將股價、交易日、現金股利、試算與公式並列。篩選依最近7日內有效收盤與未四捨五入值；擬議仍明示。
- 既有官方來源有各股票交易日期，不更換來源或用取得日期冒充交易日。實際金融行情更新40家，未取得新價1家保留。富邦資料：2026-10-14收盤160.50、2025盈餘年度現金4.25、試算2.65%、5%現金參考價85.00。
- 用online backup後更新，行情服務只複製金融17／支援股票的報價欄位；PE/PB/官方殖利率、一般股、財報、追蹤不變。修正既有錯誤未來日期造成新官方有效資料無法修復的問題。
- `dotnet build RoceTool.Tests\RoceTool.Tests.csproj -c Release --no-restore -m:1 -v:minimal`：成功，0警告、0錯誤；`dotnet test ... -c Release --no-build --no-restore -v:minimal`：49通過，0失敗／略過。行情工具建置成功。
- 瀏覽器驗證：股價／殖利率／參考價共6次雙向排序正確；checkbox篩選6家公司、進公司、返回仍保留條件；公司頁七年資料保留。1365px／390px無頁面水平溢出，價格區塊定位約100px；主控台0錯誤／警告。股價更新POST實際成功40家、保留1家並保持OnlyTarget與排序。
- 本機原有服務期間被關閉；一次重啟遇5050已占用，因此先檢查既有服務並驗證其頁面。之後確認已釋放才啟動最後版；現行自己的exec session15683、本機5050，可供使用者檢視。未停止未知程序。
- 畫面證據：`.playwright-mcp/finance-price-overview-20261015.png`、`.playwright-mcp/finance-price-company-20261015.png`。README與功能規格同步；`git diff --check`通過。

## 2026-10-15 專業金融分析調整方向

- [x] 閱讀使用者附件，對照現有財報／股利／行情模型。
- [x] 查證IFRS17／TW-ICS、FVOCI與CSM官方定義；未將附件中的固定影響分數或數值當成已驗證模型。
- [x] 整理六項優先順序及分階段驗收至 `docs/finance-analysis-roadmap.md`。
- 此節為研究與建議；資料版本、業務KPI、配息能力、正常化估值及總體事件映射尚未實作。

## 2026-10-15 個股 ROCE 核心分析架構建議

### Acceptance criteria

- 依目前程式碼與測試盤點一般個股分析，明確區分既有功能與建議。
- 以少數指標、ROCE 核心為原則提出優先方向，不推定未提供的書籍內容或作者門檻。
- 建議涵蓋期間／來源／缺資料、品質與估值的角色，保留金融業獨立分析。
- 本輪為架構諮詢，不變更產品程式、財報、評分設定或觀察清單。

### Checklist

- [x] A 閱讀既有規格、經驗記錄與目前分析入口。
- [x] B 核對公式、評分、篩選及個股呈現，整理少指標方案。
- [x] C 完成既有測試執行嘗試及限制記錄，查證必要財務概念的一手來源。
- [x] D 提供具體優化順序與驗收方式，記錄證據及限制。

### Dependencies & Environment

- .NET 10、Razor Pages、Core 計算與 SQLite 分析服務；本輪不啟動資料回填或外部財報更新。
- 工作目錄有既有未提交變更，以目前工作目錄為準，不還原其他修改。

### Working Notes

- 目前已有七年 ROCE、FCF、年度末 PE、TTM EPS 與同業／規模排名；優化應先整理判斷層級，避免重複新增指標。
- 風險低：僅追加本輪諮詢與驗證紀錄；無產品或資料庫異動。

### Results

- 新增 `docs/stock-analysis-roadmap.md`：五項方向、現況證據、建議流程、實作順序與驗收條件。保留現有 Core／Infrastructure／Web 分層。
- 優先缺口：YTD／FY 分數差、篩選同業混年度、TTM 未檢查相鄰季度、部分資料仍能滿分與 FCF 核對 null 顯示 OK。未量測本機資料實際受影響公司數。
- 程式的預設權重已核對；未查詢資料庫目前的自訂範本，因此不宣稱使用者目前一定採預設設定。
- 一手財務資料：ACCA Financial statements interpretation；IFRS IAS 7。建議與書中方法分開，未假定未提供的作者門檻。
- .NET SDK 10.0.402；執行 `dotnet test RoceTool.Tests\RoceTool.Tests.csproj -c Audit --no-restore -m:1 --filter 'FullyQualifiedName~FinancialCalculatorTests|FullyQualifiedName~ScoreEngineTests|FullyQualifiedName~StockScreenTests' -v:minimal`。Core、Infrastructure、Web、Tests 已建置成功；vstest.console 在 90 秒後無法連線 testhost，測試回合中止，exit code 1，不能宣稱測試通過。
- 未為架構諮詢變更測試執行環境；之後可在可用的測試環境用相同命令重跑。無瀏覽器、實際供應商或投資績效回測驗證。
- `git diff --check -- docs/stock-analysis-roadmap.md tasks/todo.md` 未發現空白錯誤；只有既有行尾轉換提示。本輪沒有修改產品程式或金融資料。

## 2026-10-15 ROCE 失效情境與漏網機會研究題綱

### Acceptance criteria

- 將使用者的報表造假、結構性風險及 Apple／NVIDIA 機會遺漏疑問，整理為可直接交給其他 AI 的研究題目。
- 提供可獨立理解的專案基準，區分程式預設、使用者實際策略未知項目及尚未實作提案。
- 要求案例使用當時可取得資訊、失敗對照及防護規則的誤殺成本，不預設策略必然獲利或一定漏掉贏家。

### Checklist

- [x] A 核對現行公式／預設與前輪建議邊界。
- [x] B 查閱監管案例與回測偏誤的一手參考。
- [x] C 製作共用背景、獨立研究問題及統一交付格式。
- [x] D 檢查題目完整性、來源與文件，交付研究題綱。

### Working Notes

- 本輪是準備研究問題，不先執行完整歷史回測或修改投資篩選規則。
- Apple／NVIDIA 為使用者指定的案例；需先檢驗是否被實際公式排除，不能以今日成功預設過去一定可識別。
- 風險低；僅新增文件與本輪稽核紀錄，無產品或資料異動。

### Results

- 新增 `docs/roce-research-questions.md`，整理 7 個可獨立研究的題目：舞弊、結構性衰退、投入資本口徑、Apple／NVIDIA 是否漏選、估值、投資組合風險，以及最小防護取捨。
- 文件包含可直接貼給其他 AI 的共用背景與總提示，列明專案公式、程式預設權重與篩選條件；清楚標記實際使用設定尚未確認、歷史買賣策略尚未定義。
- 規定使用事件發生時已公告的資料重算，成功案例要有相似失敗對照，補強提案要同時計算錯殺代價；未把事後股價表現當作當時可知資訊。
- 第一手研究入口包含 SEC 的 WorldCom、Luckin Coffee 文件及 CFA Institute 的回測偏誤說明；案例只作研究起點，須由研究者核實策略是否曾選入。
- `git diff --check -- docs/roce-research-questions.md tasks/todo.md` 無空白錯誤；只有 `tasks/todo.md` 行尾轉換提示。未執行測試，本輪只製作文檔，沒有修改產品程式與財務資料。

## 2026-10-15 ROCE 產品調整順序

### Acceptance criteria

- [x] 將使用者提出的翻車風險和錯過機會疑慮轉為分階段產品調整建議。
- [x] 先保留嚴格 ROCE 篩選規則，另提供人工例外研究空間；不把回顧案例直接變成買入規則。
- [x] 每階段列出實作重點、使用者可見效果與驗收方式。
- [x] 本輪僅提供產品路線建議，不更動程式、資料庫及評分設定。

### Results

- 在 `docs/stock-analysis-roadmap.md` 新增四階段實作建議與最小首版：資料狀態及期間正確性、個股 ROCE 主流程、嚴格篩選／例外研究分流、舞弊警訊／估值／績效邊界。
- 特別保留人工例外研究清單，不將其視為買入建議，也不默默降低原篩選門檻。
- 確認目前財報模型沒有標準化應收款、存貨及關係人交易等欄位，因此暫不能直接聲稱產品可偵測完整舞弊風險。
- `git diff --check -- docs/stock-analysis-roadmap.md tasks/todo.md` 無空白錯誤；產品程式與資料未修改，本輪未執行建置或測試。

## 2026-10-15 ROCE 個股分析第一階段

### Acceptance criteria

- 修正季報 YTD 與年度分數直接相比的畫面訊息。
- TTM EPS 只在四季連續且每季數值完整時標示完整。
- Screen 預設使用共同年度，逐項顯示選用年度與完整性；缺值仍保留未知。
- 現金流沒有足夠比較資料時明確顯示「資料不足」，不以 OK 代表已核實。
- 建置通過、文件與程式差異檢查完成；不改寫工作樹上先前未提交的財務研究功能。
- 將完成版本推至使用者指定的 GitHub 儲存庫 `https://github.com/Weichen5700/FROCE`。

### Checklist

- [x] A 確認共享工作樹與既有改動，定位第一階段最小變更。
- [x] B 實作同期間呈現、TTM 連續性及資料狀態。
- [x] C 執行建置和差異檢查，確認敏感資料與生成檔不進版控。
- [ ] D 提交目前可版本化的專案變更並推送至 GitHub。

### Risk & Rollback

- Risk: medium；修改財報判讀與篩選呈現，不調整既有 ROCE 門檻、評分權重、財報或追蹤資料。
- Rollback: 推送的變更以單一 commit 提交；必要時可 revert 該 commit。

### Dependencies & Environment

- .NET 10、Razor Pages、Core／Infrastructure／Tests 專案。
- 當前 `master` 沒有 Git remote，且工作樹包含大量既有未提交的金融功能與本機資料；僅納入相關版本化程式／文件，不納入資料庫、金鑰、個人清單、快取及瀏覽器證據。

### Working Notes

- 使用者指示先做建議的第一階段並推送此版本。
- 推送目的地為使用者指定的 `https://github.com/Weichen5700/FROCE`；遠端目前沒有 refs，預計推送 `master`。

## 2026-10-16 ROCE 風險提示與例外研究佇列

### Acceptance criteria

- 個股頁提供可追溯的財報風險提示；觸發時只標示待核對，不判定舞弊；缺資料與未觸發明確分開。
- 初版只使用現有年度營收、淨利、營業利益與營業現金流欄位，呈現透明規則及期間／數值證據。
- 使用者可從個股進入例外研究佇列，記錄未通過 ROCE 的原因、研究筆記、狀態與下次檢視日。
- 例外佇列與正式篩選／觀察清單分開，資料存於忽略的本機 App_Data。
- Release 建置與差異檢查完成；不覆蓋工作樹上既有的金融研究變更。

### Checklist

- [x] A 核對現有財報欄位及外部名單／狀態儲存模式。
- [x] B 實作保守的年度風險提示與本機例外研究佇列。
- [x] C 執行 Release 建置、diff check，檢查個人資料不會進版控。
- [x] D 只提交本輪檔案並推送至 FROCE。

### Risk & Rollback

- Risk: medium；新增本機研究狀態與可解釋提示，不變更 ROCE 門檻、評分或持倉。
- Rollback: 單一 commit，可用 `git revert <commit>` 回復；佇列 JSON 為本機狀態，不進版控。

### Working Notes

- 使用者希望把討論的舞弊／結構性風險及錯過高成長公司的問題轉成產品改善。
- 推送後已存在未提交的金融研究變更；本輪需逐檔 stage，避免混入其他工作。

### Results

- 個股頁新增近三年現金流／淨利差異與營收／營業利益連續下滑提示，使用三個連續年度及一致財報口徑；逐張卡顯示數值、來源、資料不足狀態及限制，不判定舞弊。
- 新增本機例外研究佇列，可記錄一般業股票、研究理由、狀態及下次檢視日；與正式 ROCE 篩選及觀察清單分開，JSON 保存在被忽略的 `RoceTool.Web/App_Data`。
- `dotnet build RoceTool.slnx -c Release -m:1 -v:minimal -p:BaseOutputPath=tmp/phase2-build/` 建置成功，0 個錯誤；只有 NuGet 弱點資料服務無法連線警告。未執行測試。
- `git diff --cached --check` 通過；本輪提交 `18476dc` 已推送至 FROCE `master`。未提交既有 CTBC／金融研究工作樹變更。
