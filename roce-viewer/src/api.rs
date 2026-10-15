use axum::{
    extract::{Path, Query, State},
    http::StatusCode,
    response::{IntoResponse, Response},
    Json,
};
use serde::Deserialize;
use serde_json::json;
use std::sync::Arc;
use tokio::sync::Mutex;

use crate::{db, fetch, models::*};

// ---------------------------------------------------------------------------
// Shared state
// ---------------------------------------------------------------------------

#[derive(Clone)]
pub struct AppState {
    pub db: Arc<Mutex<rusqlite::Connection>>,
    pub client: Arc<reqwest::Client>,
    /// Shared score rules (updated via PUT /api/score-template).
    pub score_rules: Arc<Mutex<Vec<ScoreRule>>>,
}

// ---------------------------------------------------------------------------
// Error adapter: anyhow::Error → HTTP 500
// ---------------------------------------------------------------------------

pub struct AppError(anyhow::Error);

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        (StatusCode::INTERNAL_SERVER_ERROR, self.0.to_string()).into_response()
    }
}

impl<E: Into<anyhow::Error>> From<E> for AppError {
    fn from(e: E) -> Self { AppError(e.into()) }
}

type ApiResult<T> = Result<Json<T>, AppError>;

// ---------------------------------------------------------------------------
// Static frontend
// ---------------------------------------------------------------------------

pub async fn serve_index() -> impl IntoResponse {
    (
        StatusCode::OK,
        [("content-type", "text/html; charset=utf-8")],
        include_str!("../assets/index.html"),
    )
}

// ---------------------------------------------------------------------------
// Stock search
// ---------------------------------------------------------------------------

#[derive(Deserialize)]
pub struct SearchQuery {
    q: Option<String>,
}

pub async fn search_stocks(
    State(s): State<AppState>,
    Query(p): Query<SearchQuery>,
) -> ApiResult<Vec<StockSummary>> {
    let q = p.q.unwrap_or_default();
    let conn = s.db.lock().await;
    let stocks = db::search_stocks(&conn, &q)?;
    let result = stocks
        .into_iter()
        .map(|st| StockSummary {
            stock_no: st.stock_no,
            name: st.name,
            market: st.market,
            industry_name: st.industry_name,
            close_price: st.close_price,
            price_change_percent: st.price_change_percent,
            pe_ratio: st.pe_ratio,
            pb_ratio: st.pb_ratio,
            dividend_yield: st.dividend_yield,
        })
        .collect();
    Ok(Json(result))
}

// ---------------------------------------------------------------------------
// Individual stock analysis
// ---------------------------------------------------------------------------

pub async fn get_analysis(
    State(s): State<AppState>,
    Path(stock_no): Path<String>,
) -> ApiResult<StockAnalysis> {
    let conn = s.db.lock().await;
    let rules = s.score_rules.lock().await;
    let stock = db::get_stock(&conn, &stock_no)?
        .ok_or_else(|| anyhow::anyhow!("股票 {stock_no} 不存在"))?;
    let stmts = db::get_statements(&conn, &stock_no)?;
    let on_wl = db::is_on_watchlist(&conn, &stock_no)?;
    Ok(Json(db::build_analysis(&conn, stock, stmts, on_wl, &rules)))
}

// ---------------------------------------------------------------------------
// Focus metrics (rich summary for watchlist cards / detail header)
// ---------------------------------------------------------------------------

pub async fn get_focus_metrics(
    State(s): State<AppState>,
    Path(stock_no): Path<String>,
) -> ApiResult<FocusMetrics> {
    let conn = s.db.lock().await;
    let rules = s.score_rules.lock().await;
    let stock = db::get_stock(&conn, &stock_no)?
        .ok_or_else(|| anyhow::anyhow!("股票 {stock_no} 不存在"))?;
    let stmts = db::get_statements(&conn, &stock_no)?;
    let on_wl = db::is_on_watchlist(&conn, &stock_no)?;
    let analysis = db::build_analysis(&conn, stock.clone(), stmts, on_wl, &rules);

    let latest = analysis.annual_history.first();
    let daily = db::get_daily_prices_cached(&conn, &stock_no)?;
    let moving_averages = db::compute_moving_averages(&daily);
    let annual_prices = db::get_annual_prices(&conn, &stock_no)?;

    Ok(Json(FocusMetrics {
        stock_no: stock.stock_no,
        name: stock.name,
        close_price: stock.close_price,
        price_change_percent: stock.price_change_percent,
        pe_ratio: stock.pe_ratio,
        pb_ratio: stock.pb_ratio,
        dividend_yield: stock.dividend_yield,
        fiscal_year: latest.map(|r| r.fiscal_year),
        latest_quarter_label: analysis.latest_quarter.as_ref().map(|q| format!("{} {}", q.fiscal_year, q.period)),
        roce_percent: latest.and_then(|r| r.roce_percent),
        roce_with_interest_percent: latest.and_then(|r| r.roce_with_interest_percent),
        roe_percent: latest.and_then(|r| r.roe_percent),
        operating_margin_percent: latest.and_then(|r| r.operating_margin_percent),
        debt_ratio_percent: latest.and_then(|r| r.debt_ratio_percent),
        current_ratio_percent: latest.and_then(|r| r.current_ratio_percent),
        roa_percent: latest.and_then(|r| r.roa_percent),
        fcf_per_share: latest.and_then(|r| r.fcf_per_share),
        eps: latest.and_then(|r| r.eps),
        score: analysis.score,
        peer_comparison: analysis.peer_comparison,
        moving_averages,
        annual_prices,
    }))
}

// ---------------------------------------------------------------------------
// Watchlist
// ---------------------------------------------------------------------------

pub async fn get_watchlist(
    State(s): State<AppState>,
) -> ApiResult<Vec<WatchlistCard>> {
    let conn = s.db.lock().await;
    let rules = s.score_rules.lock().await;
    Ok(Json(db::build_watchlist_cards(&conn, &rules)?))
}

pub async fn add_to_watchlist(
    State(s): State<AppState>,
    Path(stock_no): Path<String>,
) -> Result<StatusCode, AppError> {
    let conn = s.db.lock().await;
    db::add_watchlist(&conn, &stock_no)?;
    Ok(StatusCode::NO_CONTENT)
}

pub async fn remove_from_watchlist(
    State(s): State<AppState>,
    Path(stock_no): Path<String>,
) -> Result<StatusCode, AppError> {
    let conn = s.db.lock().await;
    db::remove_watchlist(&conn, &stock_no)?;
    Ok(StatusCode::NO_CONTENT)
}

// ---------------------------------------------------------------------------
// Market screen
// ---------------------------------------------------------------------------

#[derive(Deserialize)]
pub struct ScreenQuery {
    min_roce: Option<f64>,
    min_score: Option<f64>,
    min_coverage: Option<f64>,
    year: Option<i64>,
    industry: Option<String>,
    size_band: Option<String>,
    min_assets_billion: Option<f64>,
}

pub async fn screen(
    State(s): State<AppState>,
    Query(p): Query<ScreenQuery>,
) -> ApiResult<Vec<ScreenItem>> {
    let size_band = p.size_band.as_deref().and_then(|b| match b.to_lowercase().as_str() {
        "small" | "小型" => Some(CompanySizeBand::Small),
        "medium" | "中型" => Some(CompanySizeBand::Medium),
        "large" | "大型" => Some(CompanySizeBand::Large),
        _ => None,
    });
    let params = db::ScreenParams {
        min_roce: p.min_roce.unwrap_or(15.0),
        min_score: p.min_score.unwrap_or(0.0),
        min_coverage: p.min_coverage.unwrap_or(0.0),
        year: p.year,
        industry_name: p.industry,
        size_band,
        min_total_assets_billion: p.min_assets_billion,
    };
    let conn = s.db.lock().await;
    let rules = s.score_rules.lock().await;
    Ok(Json(db::screen_stocks(&conn, &params, &rules)?))
}

// ---------------------------------------------------------------------------
// Catalog refresh (TWSE / TPEx)
// ---------------------------------------------------------------------------

pub async fn refresh_catalog(State(s): State<AppState>) -> ApiResult<serde_json::Value> {
    // Fetch without holding the DB lock
    let client = s.client.clone();
    let (stocks, stmts) = fetch::fetch_catalog_and_statements(&client).await?;
    let sc = stocks.len();
    let mc = stmts.len();
    let conn = s.db.lock().await;
    for st in &stocks { let _ = db::upsert_stock(&conn, st); }
    for st in &stmts  { let _ = db::upsert_statement(&conn, st); }
    Ok(Json(json!({
        "stocks": sc,
        "statements": mc,
        "message": format!("已更新 {sc} 檔股票、{mc} 筆財報")
    })))
}

// ---------------------------------------------------------------------------
// Single-stock MOPS refresh (annual + quarterly)
// ---------------------------------------------------------------------------

pub async fn refresh_stock(
    State(s): State<AppState>,
    Path(stock_no): Path<String>,
) -> ApiResult<serde_json::Value> {
    let client = s.client.clone();
    let current_year = current_year();

    let mut new_stmts: Vec<FinancialStatement> = Vec::new();
    let mut fetched = 0usize;

    // Annual: last 7 years
    for year in (current_year - 6)..=current_year {
        if let Ok(Some(stmt)) = fetch::fetch_mops_annual(&client, &stock_no, year as i32).await {
            new_stmts.push(stmt);
            fetched += 1;
        }
    }

    // Quarters for current and previous year (Q1–Q3 YTD)
    for year in [current_year, current_year - 1] {
        for quarter in 1..=3i32 {
            if let Ok(Some(stmt)) = fetch::fetch_mops_quarter(&client, &stock_no, year as i32, quarter).await {
                new_stmts.push(stmt);
            }
        }
    }

    let conn = s.db.lock().await;
    for stmt in &new_stmts { let _ = db::upsert_statement(&conn, stmt); }

    Ok(Json(json!({
        "fetched": fetched,
        "message": format!("已從 MOPS 取得 {fetched} 筆年報及季報")
    })))
}

// ---------------------------------------------------------------------------
// Price refresh (daily + annual)
// ---------------------------------------------------------------------------

pub async fn refresh_prices(
    State(s): State<AppState>,
    Path(stock_no): Path<String>,
) -> ApiResult<serde_json::Value> {
    let client = s.client.clone();
    let current_year = current_year();

    // Fetch daily prices
    let daily = fetch::fetch_daily_prices(&client, &stock_no).await.unwrap_or_default();
    let daily_count = daily.len();

    // Fetch annual last-close for last 7 years
    let mut annual_prices: Vec<crate::models::AnnualPriceReference> = Vec::new();
    for year in (current_year - 6)..=current_year {
        if let Ok(Some(p)) = fetch::fetch_annual_last_close(&client, &stock_no, year as i32).await {
            annual_prices.push(p);
        }
    }
    let annual_count = annual_prices.len();

    let conn = s.db.lock().await;
    db::upsert_daily_prices(&conn, &stock_no, &daily)?;
    for p in &annual_prices { db::upsert_annual_price(&conn, &stock_no, p)?; }

    Ok(Json(json!({
        "daily": daily_count,
        "annual": annual_count,
        "message": format!("已更新 {daily_count} 筆日線、{annual_count} 筆年度收盤價")
    })))
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn current_year() -> i64 {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    1970 + (secs / 31_557_600) as i64
}

// ---------------------------------------------------------------------------
// Score template
// ---------------------------------------------------------------------------

pub async fn get_score_template(State(s): State<AppState>) -> ApiResult<ScoreTemplateDto> {
    let rules = s.score_rules.lock().await;
    Ok(Json(ScoreTemplateDto { name: "品質／資本效率".to_string(), rules: rules.clone() }))
}

pub async fn put_score_template(
    State(s): State<AppState>,
    Json(dto): Json<ScoreTemplateDto>,
) -> ApiResult<serde_json::Value> {
    let enabled_total: f64 = dto.rules.iter().filter(|r| r.is_enabled).map(|r| r.weight).sum();
    if (enabled_total - 100.0).abs() > 0.01 {
        return Err(AppError(anyhow::anyhow!("啟用指標的權重合計必須等於 100（目前 {enabled_total:.1}）")));
    }
    for r in dto.rules.iter().filter(|r| r.is_enabled) {
        if r.points.len() < 2 { return Err(AppError(anyhow::anyhow!("每個指標至少需要兩個門檻點"))); }
    }
    let conn = s.db.lock().await;
    db::save_score_rules(&conn, &dto.name, &dto.rules)?;
    drop(conn);
    *s.score_rules.lock().await = dto.rules;
    Ok(Json(json!({"message": "評分範本已更新"})))
}

pub async fn reset_score_template(State(s): State<AppState>) -> ApiResult<ScoreTemplateDto> {
    let defaults = crate::calc::default_rules();
    let conn = s.db.lock().await;
    db::save_score_rules(&conn, "品質／資本效率", &defaults)?;
    drop(conn);
    *s.score_rules.lock().await = defaults.clone();
    Ok(Json(ScoreTemplateDto { name: "品質／資本效率".to_string(), rules: defaults }))
}

// ---------------------------------------------------------------------------
// Market scanner
// ---------------------------------------------------------------------------

pub async fn market_scan_status(State(s): State<AppState>) -> ApiResult<MarketScanProgress> {
    let conn = s.db.lock().await;
    Ok(Json(db::get_scan_state(&conn)?))
}

pub async fn market_scan_start(
    State(s): State<AppState>,
    Json(payload): Json<StartScanPayload>,
) -> ApiResult<MarketScanProgress> {
    if payload.fiscal_year < 1990 || payload.fiscal_year > 2200 {
        return Err(AppError(anyhow::anyhow!("掃描年度格式錯誤")));
    }
    let years = payload.years.unwrap_or(1).clamp(1, 20);
    let retry = payload.retry_failed.unwrap_or(false);

    let mut progress = {
        let conn = s.db.lock().await;
        db::ensure_scan_tables(&conn)?;
        let total = conn.query_row("SELECT COUNT(*) FROM Stocks WHERE StatementKind=0 AND IsSupported=1", [], |r| r.get::<_,i64>(0)).unwrap_or(0) as usize;
        let p = MarketScanProgress {
            status: MarketScanStatus::Running,
            fiscal_year: payload.fiscal_year,
            total: total * years as usize,
            completed: 0, success: 0, failed: 0,
            current_stock_no: None,
            range_start_fiscal_year: Some(payload.fiscal_year - years + 1),
            range_end_fiscal_year: Some(payload.fiscal_year),
        };
        db::upsert_scan_state(&conn, &p)?;
        p
    };

    // Background scan task
    let state = s.clone();
    let range_start = payload.fiscal_year - years + 1;
    let range_end = payload.fiscal_year;
    let _ = retry; // consumed by task below
    tokio::spawn(async move {
        for year in (range_start..=range_end).rev() {
            loop {
                let pending = {
                    let conn = state.db.lock().await;
                    // Check if paused
                    match db::get_scan_state(&conn) {
                        Ok(p) if p.status == MarketScanStatus::Paused => break,
                        _ => {}
                    }
                    db::list_pending_scan_stocks(&conn, year, 3).unwrap_or_default()
                };
                if pending.is_empty() { break; }
                for sno in &pending {
                    let client = state.client.clone();
                    let sno = sno.clone();
                    let result = fetch::fetch_mops_annual(&client, &sno, year as i32).await;
                    let mut conn = state.db.lock().await;
                    match result {
                        Ok(Some(stmt)) => {
                            let _ = db::upsert_statement(&conn, &stmt);
                            let _ = db::record_scan_item(&conn, &sno, year, true, None);
                        }
                        Ok(None) => { let _ = db::record_scan_item(&conn, &sno, year, false, Some("無資料")); }
                        Err(e) => { let _ = db::record_scan_item(&conn, &sno, year, false, Some(&e.to_string())); }
                    }
                    // Update progress
                    if let Ok(mut p) = db::get_scan_state(&conn) {
                        p.completed += 1;
                        let _ = db::upsert_scan_state(&conn, &p);
                    }
                }
                tokio::time::sleep(std::time::Duration::from_millis(300)).await;
            }
        }
        // Mark completed
        let conn = state.db.lock().await;
        if let Ok(mut p) = db::get_scan_state(&conn) {
            if p.status == MarketScanStatus::Running {
                p.status = MarketScanStatus::Completed;
                let _ = db::upsert_scan_state(&conn, &p);
            }
        }
    });

    progress.status = MarketScanStatus::Running;
    Ok(Json(progress))
}

pub async fn market_scan_pause(State(s): State<AppState>) -> ApiResult<MarketScanProgress> {
    let conn = s.db.lock().await;
    let mut p = db::get_scan_state(&conn)?;
    if p.status == MarketScanStatus::Running { p.status = MarketScanStatus::Paused; }
    db::upsert_scan_state(&conn, &p)?;
    Ok(Json(p))
}
