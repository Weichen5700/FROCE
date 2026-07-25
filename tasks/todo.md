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
