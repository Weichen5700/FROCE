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
- MOPS 最近七個完整年度年報
- 最新季 YTD 財務快照
- ROCE、營業利益率、ROE、負債比、流動比率
- 年度與最新季同步顯示營收、營業利益、利息收入、淨利、EPS 與六項資產負債欄位
- 同時顯示標準 ROCE 與「營業利益＋利息收入」ROCE；正式評分、排名與篩選維持採標準 ROCE
- 股票詳情顯示七年自由現金流每股趨勢，並攤開營業現金流、PPE 資本支出、FCF、股數與股數來源
- 最新季顯示未年化快照分數及相對年度差異，僅供觀察異動
- 首頁、詳情與篩選頁集中顯示 ROCE、EPS、最新官方收盤價、本益比與 ROE
- 股票詳情顯示最新官方收盤價、漲跌、漲跌幅、當日高低、成交量與行情日期
- 股票詳情顯示 5、10、20、60、120、240 個交易日的簡單移動平均（20／60／120／240 日分別為月／季／半年／年線）
- 每條均線同步顯示最新收盤價高於或低於該均線的百分比
- 均線使用交易所原始日收盤價；休市日不計，除權息未還原
- 年度切換時同步取得該年度最後一個交易日的官方收盤價；七年 ROCE 數值可懸停查看年度股價參考
- 觀察清單可展開查看各年度 EPS、年度末收盤價與年度靜態本益比（PE）
- 個股季度區塊會把年初累積 EPS 還原為單季 EPS，並補足可取得資料的近四季合計（TTM）
- `/api/stocks/{stockNo}/focus-metrics` 可一次取得 ROCE、EPS、股價、估值與 ROE 摘要
- `tools/price_roce_watch.py` 可抓官方每日收盤價，檢查「ROCE 達標且價格自今年以來高點回落」的提醒條件
- 可編輯指標權重與五段門檻
- 評分設定逐項說明指標用途、公式、預設權重的設計考量與缺資料時的總分算法
- 七年 ROCE 趨勢與估值摘要
- 高品質反向篩選：預設 ROCE ≥ 20%、品質分數 ≥ 80、資料覆蓋率 100%
- 金融專區只列官方產業代碼 17；公司列表可排序 EPS、現金／股票股利、現金盈餘配發率、簡化 ROE、淨利與資料年數；點一次公司即並列七年資料與官方子公司獲利
- 外部名單驗證：貼上自己整理的股票代號，以本機財報和目前評分設定比對門檻；通過後由使用者自行選擇是否加入觀察
- 個股頁提供近三年現金流／淨利落差及營收／營業利益連續下滑提示，附採用期間與來源；資料不足獨立標示。例外研究佇列可記錄未通過 ROCE 條件仍想追蹤的原因、狀態與下次檢視日，與正式篩選及觀察清單分開保存在本機
- 官方中文產業名稱、產業篩選、同業 ROCE 排名與產業中位數
- 依同產業投入資本三等分規模，提供同規模 ROCE 排名與篩選
- 全市場背景掃描：逐檔抓最新完整年度，可暫停、關閉並續跑
- `tools/finmind_statement_backfill.py` 可在 MOPS 大量失敗時，以 FinMind 逐檔補齊七年 ROCE 與 FCF 基礎欄位
- CSV 預覽、單位正規化、匯入及來源優先設定；可按年度匯出 FY 與 YTD 財報列
- 資料來源、抓取時間與錯誤紀錄

「金融專區」的外層是公司比較清單：EPS、普通股現金／股票股利、現金盈餘配發率、簡化 ROE 與淨利採同一盈餘年度，點表頭切換升冪／降冪，缺值置底；搜尋不改變比較年度。點一次公司即可看七年 EPS、配息、淨利、資產負債與來源，不需另進年度頁。上一家／下一家依原列表搜尋和排序切換，返回也保留條件；從既有個股入口進金融股時直接到這個頁面，行情／觀察設定保留獨立入口。

股利使用 MOPS `ajax_t05st09_2`、`qryType=2`（股利所屬年度）；普通股與特別股分開，現金及股票股利各含盈餘／公積分項。股票股利不是現金收入；現金盈餘配發率 = 盈餘現金 ÷ 同年 EPS，EPS ≤ 0 不計。保留董事會擬議／股東會確認狀態，決議日期不當作除息或實際發放日。公司頁可按「更新官方股利」重新查詢，失敗保留既有資料。

金融列表直接顯示官方收盤價、各公司交易日、現金殖利率及 5% 現金參考價，可排序價格／殖利率／參考價，也可只看達 5% 的公司。現金殖利率 = 指定盈餘年度普通股現金股利 ÷ 顯示收盤價；5% 參考價 = 同筆現金股利 ÷ 0.05。採未四捨五入值判定門檻；無股價、日期未知、未來日期或超過 7 日價格不納入篩選。公司頁另列現金＋股票配發面額合計參考，與現金殖利率分開，面額合計不等於現金收入／總報酬。

「更新官方股價」限金融名單，只更新行情欄位，PE/PB/官方殖利率、財報與觀察資料不變；無效或較舊官方回應保留既有有效行情。使用交易日期，取得時間不替代交易日。已申報股利不保證未來維持，也不代表買進即可領取過去已除息股利。專業分析後續調整順序見 `docs/finance-analysis-roadmap.md`。

合併本期淨利與歸屬母公司淨利分開；金控獲利已包含子公司，不再加總子公司獲利或推算貢獻比例。目前子公司獲利收錄富邦金、國泰金 2024／2025 官方法說主要子公司，口徑及來源逐筆保存（`samples/finance-earnings-sources.json`）；未涵蓋所有公司／七年，頁面會明示。

金融研究試點已收錄富邦、國泰、玉山、元大 2024／2025 官方指標，富邦與國泰另有部分 2026 上半年資料，共 113 筆觀察及一組國泰獲利橋接。公司頁依業務、獲利品質、資本與配息三區並列各期數字，保留個體、單位、制度、文件版本、頁碼與來源；缺值、未知公布日與期末／文件基準日分別處理，不自動計跨制度或跨版本成長。桌面按年度並列，手機按指標逐期排列。

配息情境可選七年中的基準盈餘年度，自訂 −100% 至 +100% 現金股利變化、0.1% 至 30% 目標殖利率，並列減 20%／維持／增 20% 與自訂情境。依已申報現金試算，未預測股利；不改列表固定 5% 門檻。情境在前後公司、更新股利與行情往返保留。完整八季、重編鏈、母公司可配現金、子公司上繳與特別股調節尚未完成，不評定配息安全。

研究資料為隨版本發行的 `samples/finance-research-*.json`，建置／發行複製至 `ResearchData`；網站啟動後首次使用載入，不寫回既有財報或觀察清單。更新來源檔後需重新建置及重啟。缺檔或資料契約未通過時明示錯誤，既有七年資料仍可看。

未收錄顯示「—」，明示 0 才顯示 0。簡化 ROE 採本期淨利／期末權益；金融頁不套用一般業 ROCE 評分。特殊財報公司不會因此出現在金融專區，瀏覽不會自動加入觀察清單。ETF、ETN 與權證不屬於財報公司清單。

「外部名單驗證」不會自動搜尋媒體或產生股票推薦；它用來檢查使用者提供的名單是否符合目前的 ROCE／評分門檻。未通過或公司清單找不到的項目不會加入觀察清單。

## 背景單次年報收集

可直接執行 `tools/start_market_scan_background.cmd`，以背景方式收集最近七個年度的全市場年報。命令會立即返回，不必開啟瀏覽器等待；預設年度為去年，可用參數指定範圍：

```powershell
tools\start_market_scan_background.cmd -LatestYear 2025 -Years 7
```

若要在目前視窗看進度：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\start_market_scan_background.ps1 -Wait
```

工具會沿用目前執行中的本機網站；若網站尚未啟動，會建置並暫時以隱藏視窗啟動，完成後只停止自己啟動的 Host。若資料庫已有掃描進行中，工具會接上既有進度，不重複開新掃描。狀態與資料仍寫入 `RoceTool.Web/App_Data/roce.db`，進度記錄在 `RoceTool.Web/App_Data/market-scan-background.log`。

這是一次性的增量年報掃描：會補抓指定年度中缺少或不完整的資料，並重試失敗項目；已完整的年報不會強制重抓，也不更新股價快取。MOPS 來源需要網路，耗時依待抓公司數與來源回應而異。

## 金融股年度財報補齊

一般業全市場掃描不包含金融／特殊業。可執行 `tools/start_finance_backfill_background.cmd`，收集支援中且產業代碼 17 的金融股最近七個完整年度。MOPS 請求逾時上限 65 秒，暫時性請求錯誤最多重試一次；只略過已具淨利、權益、EPS、資產總額的年度，其他來源資料保留，失敗不當作官方不存在。

```powershell
tools\start_finance_backfill_background.cmd -LatestYear 2025 -Years 7
```

命令會在背景執行，進度與缺漏記錄在 `RoceTool.Web/App_Data/finance-backfill.log`。要在目前視窗等候並查看進度，可執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\start_finance_backfill_background.ps1 -Wait
```

回填前會建立並檢查 `RoceTool.Web/App_Data/roce-finance-backup-<時間>.db`，每筆成功資料立即寫入 SQLite；中斷後重新執行會依資料庫現況跳過已完整的年度。工具會拒絕與執行中的一般股全市場掃描同時寫入。金融業現金流等個別欄位若 MOPS 未提供，會保留空值並列入欄位覆蓋結果，不代表資料已完整。

股利與已核實獲利組成回填：`dotnet run --project tools/FinanceIncomeBackfill -c Release`（可追加股票代號）。更新已收錄 MOPS 年報的歸母淨利欄位：追加 `2881 --parent`。工具先建立 SQLite 線上備份，再按公司／年度 upsert，不刪除財報或修改觀察清單。新增資料表為 `AnnualDividends`、`FinancialSubsidiaryIncomes`，既有財報以加欄方式相容。

## 價格回落提醒

`price-roce-alerts.json` 控制提醒條件，預設為 ROCE ≥ 20%、收盤價自今年以來高點回落達門檻。若 `stockNos` 為空，腳本會優先使用觀察清單；觀察清單為空時使用預設重點企業。

`highReferenceMode` 預設為 `yearToDate`，會抓今年 1 月到目前月份的個股日成交資訊來計算今年以來高點；若改成 `trackedFromMonitor`，才會回到「從監控開始後累積高點」的模式。

```powershell
python tools/price_roce_watch.py
```

資料源：網站更新及提醒皆使用上市 TWSE `STOCK_DAY_ALL` 與上櫃 TPEx `tpex_mainboard_quotes` 的日收盤行情；這不是盤中即時報價。提醒只代表「值得回來檢查」，不構成投資建議。

## FinMind 七年補資料

若 MOPS 年報端點回安全頁或缺少現金流欄位，可用 FinMind fallback 逐檔補齊 2019-2025 的損益表、資產負債表與現金流量表，再合併成 `FinMind 合併補充` 年度資料。金額會轉成資料庫既有的新台幣千元單位；MOPS 原始列仍保留，畫面會優先選欄位較完整的年度資料。

```powershell
python tools/finmind_statement_backfill.py --start-year 2019 --end-year 2025
```

沒有 `FINMIND_TOKEN` 時，預設每個 API request 等 12.2 秒，降低觸發免費額度限制的機率；若有 token，可先設定 `$env:FINMIND_TOKEN = "..."`，工具會改用較快的保守節奏。進度會寫入 `RoceTool.Web/App_Data/finmind_backfill_state.json`，可中斷後重跑，已完整的公司年度會自動跳過。

## CSV 格式

```csv
StockNo,FiscalYear,Period,Unit,OperatingRevenue,OperatingIncome,InterestIncome,NetIncome,CurrentAssets,TotalAssets,CurrentLiabilities,TotalLiabilities,TotalEquity,Eps,OperatingCashFlow,CapexPpe,CapexIntangibles,WeightedAverageShares,WeightedAverageSharesSource,SourceName
2330,2025,FY,ThousandTWD,3809054272,1936091677,105739081,1715396780,3817130817,7933023878,1458019289,2472228595,5460795283,66.15,2754537827,-1212174401,-28753470,25930380458,基本股數,手動匯入
```

- `Period`：`FY`、`Q1YTD`、`Q2YTD`、`Q3YTD` 或 `Q4YTD`
- `Unit`：`TWD`、`ThousandTWD` 或 `MillionTWD`
- 所有金額匯入後統一轉為新台幣千元
- `InterestIncome` 為選填欄位；提供後才會計算「營業利益＋利息收入」ROCE
- `OperatingCashFlow`、`CapexPpe`、`CapexIntangibles` 為金額欄位，會依 `Unit` 正規化；`CapexPpe` 通常為負數，FCF 計算時取絕對值扣除
- `WeightedAverageShares` 維持股數，不套用 `Unit`；若 MOPS 沒有穩定揭露股數，系統會以 `NetIncome / Eps` 推估並標示來源
- V1 的 FCF 定義為「營業活動現金流量 − |取得不動產、廠房及設備|」；無形資產支出先顯示但不自動納入資本支出
- CSV 預設只在官方資料缺漏時使用；勾選「優先來源」可覆蓋同期間的來源選擇

## 驗證

```powershell
dotnet test RoceTool.slnx
dotnet build RoceTool.slnx
```

分數與指標只供研究，不構成投資建議。季報損益是年初累計數，工具不會拿季報直接替代完整年度評分。




