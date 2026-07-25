# ROCE 觀察站

本機繁體中文台股基本面觀察工具。輸入上市或上櫃股票代號後，工具會從證交所、櫃買中心與公開資訊觀測站取得財報，計算 ROCE、五項品質指標及可自訂權重的總分。

第一版規格基線請參考 [`docs/spec-v0.1.md`](docs/spec-v0.1.md)；系統架構與主要流程請參考 [`docs/architecture.md`](docs/architecture.md)。

## 啟動

需求：.NET 10 SDK。

```powershell
dotnet run --project RoceTool.Web/RoceTool.Web.csproj
```

瀏覽器會自動開啟 [http://127.0.0.1:5050](http://127.0.0.1:5050)。資料庫建立於 `RoceTool.Web/App_Data/roce.db`，網站只監聽本機位址。

若不希望自動開啟瀏覽器：

```powershell
$env:OpenBrowser = "false"
dotnet run --project RoceTool.Web/RoceTool.Web.csproj
```

## 功能

- 上市／上櫃公司代號與名稱搜尋
- 本機觀察清單
- MOPS 最近五個完整年度年報
- 最新季 YTD 財務快照
- ROCE、營業利益率、ROE、負債比、流動比率
- 年度與最新季同步顯示營收、營業利益、利息收入、淨利、EPS 與六項資產負債欄位
- 同時顯示標準 ROCE 與「營業利益＋利息收入」ROCE；正式評分、排名與篩選維持採標準 ROCE
- 最新季顯示未年化快照分數及相對年度差異，僅供觀察異動
- 首頁、詳情與篩選頁集中顯示 ROCE、EPS、最新官方收盤價、本益比與 ROE
- 股票詳情顯示最新官方收盤價、漲跌、漲跌幅、當日高低、成交量與行情日期
- 股票詳情顯示 5、10、20、60、120、240 個交易日的簡單移動平均（20／60／120／240 日分別為月／季／半年／年線）
- 每條均線同步顯示最新收盤價高於或低於該均線的百分比
- 均線使用交易所原始日收盤價；休市日不計，除權息未還原
- `/api/stocks/{stockNo}/focus-metrics` 可一次取得 ROCE、EPS、股價、估值與 ROE 摘要
- `tools/price_roce_watch.py` 可抓官方每日收盤價，檢查「ROCE 達標且價格自追蹤高點回落」的提醒條件
- 可編輯指標權重與五段門檻
- 五年 ROCE 趨勢與估值摘要
- 高品質反向篩選：預設 ROCE ≥ 20%、品質分數 ≥ 80、資料覆蓋率 100%
- 官方中文產業名稱、產業篩選、同業 ROCE 排名與產業中位數
- 依同產業投入資本三等分規模，提供同規模 ROCE 排名與篩選
- 全市場背景掃描：逐檔抓最新完整年度，可暫停、關閉並續跑
- CSV 預覽、單位正規化、匯入及來源優先設定
- 資料來源、抓取時間與錯誤紀錄

金融、金控、保險與證券公司可加入觀察，但不套用一般業 ROCE 評分。ETF、ETN 與權證不屬於財報公司清單。

## 價格回落提醒

`price-roce-alerts.json` 控制提醒條件，預設為 ROCE ≥ 20%、收盤價自今年以來高點回落達門檻。若 `stockNos` 為空，腳本會優先使用觀察清單；觀察清單為空時使用預設重點企業。

`highReferenceMode` 預設為 `yearToDate`，會抓今年 1 月到目前月份的個股日成交資訊來計算今年以來高點；若改成 `trackedFromMonitor`，才會回到「從監控開始後累積高點」的模式。

```powershell
python tools/price_roce_watch.py
```

資料源：網站更新及提醒皆使用上市 TWSE `STOCK_DAY_ALL` 與上櫃 TPEx `tpex_mainboard_quotes` 的日收盤行情；這不是盤中即時報價。提醒只代表「值得回來檢查」，不構成投資建議。

## CSV 格式

```csv
StockNo,FiscalYear,Period,Unit,OperatingRevenue,OperatingIncome,InterestIncome,NetIncome,CurrentAssets,TotalAssets,CurrentLiabilities,TotalLiabilities,TotalEquity,SourceName
2330,2025,FY,ThousandTWD,3809054272,1936091677,105739081,1715396780,3817130817,7933023878,1458019289,2472228595,5460795283,手動匯入
```

- `Period`：`FY`、`Q1YTD`、`Q2YTD`、`Q3YTD` 或 `Q4YTD`
- `Unit`：`TWD`、`ThousandTWD` 或 `MillionTWD`
- 所有金額匯入後統一轉為新台幣千元
- `InterestIncome` 為選填欄位；提供後才會計算「營業利益＋利息收入」ROCE
- CSV 預設只在官方資料缺漏時使用；勾選「優先來源」可覆蓋同期間的來源選擇

## 驗證

```powershell
dotnet test RoceTool.slnx
dotnet build RoceTool.slnx
```

分數與指標只供研究，不構成投資建議。季報損益是年初累計數，工具不會拿季報直接替代完整年度評分。




