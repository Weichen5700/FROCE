use serde::{Deserialize, Serialize};

// ---------------------------------------------------------------------------
// Score engine types
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum MetricKey { Roce, OperatingMargin, Roe, DebtRatio, CurrentRatio }

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ThresholdPoint {
    pub value: f64,
    pub score: f64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScoreRule {
    pub metric_key: MetricKey,
    pub is_enabled: bool,
    pub weight: f64,
    pub points: Vec<ThresholdPoint>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MetricResult {
    pub key: MetricKey,
    pub label: &'static str,
    pub value: Option<f64>,
    pub score: Option<f64>,
    pub weight: f64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ScoreBreakdown {
    pub total_score: Option<f64>,
    pub coverage_percent: f64,
    pub metrics: Vec<MetricResult>,
}

// ---------------------------------------------------------------------------
// Peer comparison
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum CompanySizeBand { Small, Medium, Large }

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PeerComparison {
    pub industry_name: String,
    pub roce_rank: usize,
    pub sample_size: usize,
    pub median_roce: f64,
    pub capital_employed: f64,
    pub size_band: CompanySizeBand,
    pub size_roce_rank: usize,
    pub size_sample_size: usize,
    pub size_median_roce: f64,
}

// ---------------------------------------------------------------------------
// Quarterly EPS
// ---------------------------------------------------------------------------

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct QuarterlyEpsObservation {
    pub fiscal_year: i64,
    pub quarter: i64,
    pub ytd_eps: Option<f64>,
    pub standalone_eps: Option<f64>,
    pub label: String,
}

// ---------------------------------------------------------------------------
// Price types
// ---------------------------------------------------------------------------

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AnnualPriceReference {
    pub fiscal_year: i64,
    pub trading_date: Option<String>,
    pub close: Option<f64>,
    pub source: String,
}

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DailyClosingPrice {
    pub date: String,
    pub close: f64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MovingAverage {
    pub trading_days: usize,
    pub label: String,
    pub value: Option<f64>,
}

// ---------------------------------------------------------------------------
// Market scan
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum MarketScanStatus { Idle, Running, Paused, Completed }

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MarketScanProgress {
    pub status: MarketScanStatus,
    pub fiscal_year: i64,
    pub total: usize,
    pub completed: usize,
    pub success: usize,
    pub failed: usize,
    pub current_stock_no: Option<String>,
    pub range_start_fiscal_year: Option<i64>,
    pub range_end_fiscal_year: Option<i64>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StartScanPayload {
    pub fiscal_year: i64,
    pub years: Option<i64>,
    pub retry_failed: Option<bool>,
}

// ---------------------------------------------------------------------------
// Stock entities
// ---------------------------------------------------------------------------

/// Maps directly to the `Stocks` SQLite table (EF Core PascalCase columns).
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Stock {
    pub stock_no: String,
    pub name: String,
    pub market: i64,
    pub industry_code: String,
    pub industry_name: String,
    pub statement_kind: i64, // 0=General, 1=Financial
    pub is_supported: bool,
    pub pe_ratio: Option<f64>,
    pub pb_ratio: Option<f64>,
    pub dividend_yield: Option<f64>,
    pub close_price: Option<f64>,
    pub price_change: Option<f64>,
    pub price_change_percent: Option<f64>,
    pub day_high: Option<f64>,
    pub day_low: Option<f64>,
    pub trade_volume: Option<i64>,
    pub quote_date: String,
    pub quote_source: String,
}

/// Maps directly to the `FinancialStatements` table.
/// Amounts stored in thousands TWD; EPS in TWD; shares in individual shares.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FinancialStatement {
    pub id: i64,
    pub stock_no: String,
    pub fiscal_year: i64,
    pub period: String,
    pub is_annual: bool,
    pub operating_revenue: Option<f64>,
    pub operating_income: Option<f64>,
    pub interest_income: Option<f64>,
    pub net_income: Option<f64>,
    pub current_assets: Option<f64>,
    pub total_assets: Option<f64>,
    pub current_liabilities: Option<f64>,
    pub total_liabilities: Option<f64>,
    pub total_equity: Option<f64>,
    pub eps: Option<f64>,
    pub operating_cash_flow: Option<f64>,
    pub capex_ppe: Option<f64>,
    pub capex_intangibles: Option<f64>,
    pub weighted_average_shares: Option<f64>,
    pub weighted_average_shares_source: String,
    pub source_kind: i64, // 0=Twse,1=Tpex,2=Mops,3=Csv,4=FinMind
    pub source_name: String,
    pub source_url: String,
    pub is_preferred: bool,
    pub fetched_at: String,
}

/// Slim summary returned by the stock search endpoint.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StockSummary {
    pub stock_no: String,
    pub name: String,
    pub market: i64,
    pub industry_name: String,
    pub close_price: Option<f64>,
    pub price_change_percent: Option<f64>,
    pub pe_ratio: Option<f64>,
    pub pb_ratio: Option<f64>,
    pub dividend_yield: Option<f64>,
}

/// One row in the FCF / ROCE history table (annual granularity).
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AnnualRow {
    pub fiscal_year: i64,
    pub period: String,
    pub operating_revenue: Option<f64>,
    pub operating_income: Option<f64>,
    pub interest_income: Option<f64>,
    pub net_income: Option<f64>,
    pub total_assets: Option<f64>,
    pub current_assets: Option<f64>,
    pub current_liabilities: Option<f64>,
    pub total_liabilities: Option<f64>,
    pub total_equity: Option<f64>,
    pub eps: Option<f64>,
    pub operating_cash_flow: Option<f64>,
    pub capex_ppe: Option<f64>,
    pub capex_intangibles: Option<f64>,
    pub weighted_average_shares: Option<f64>,
    pub weighted_average_shares_source: String,
    pub source_name: String,
    // Computed metrics
    pub roce_percent: Option<f64>,
    pub roce_with_interest_percent: Option<f64>,
    pub roe_percent: Option<f64>,
    pub operating_margin_percent: Option<f64>,
    pub debt_ratio_percent: Option<f64>,
    pub current_ratio_percent: Option<f64>,
    pub roa_percent: Option<f64>,
    pub fcf: Option<f64>,
    pub fcf_per_share: Option<f64>,
    pub score: Option<ScoreBreakdown>,
    pub review_note: Option<String>,
}

/// Full analysis bundle for one stock.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StockAnalysis {
    pub stock: Stock,
    pub annual_history: Vec<AnnualRow>,
    pub latest_quarter: Option<FinancialStatement>,
    pub quarterly_eps_observations: Vec<QuarterlyEpsObservation>,
    pub is_on_watchlist: bool,
    pub score: Option<ScoreBreakdown>,
    pub peer_comparison: Option<PeerComparison>,
}

/// Focus metrics – rich summary for the watchlist / detail header.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FocusMetrics {
    pub stock_no: String,
    pub name: String,
    pub close_price: Option<f64>,
    pub price_change_percent: Option<f64>,
    pub pe_ratio: Option<f64>,
    pub pb_ratio: Option<f64>,
    pub dividend_yield: Option<f64>,
    pub fiscal_year: Option<i64>,
    pub latest_quarter_label: Option<String>,
    pub roce_percent: Option<f64>,
    pub roce_with_interest_percent: Option<f64>,
    pub roe_percent: Option<f64>,
    pub operating_margin_percent: Option<f64>,
    pub debt_ratio_percent: Option<f64>,
    pub current_ratio_percent: Option<f64>,
    pub roa_percent: Option<f64>,
    pub fcf_per_share: Option<f64>,
    pub eps: Option<f64>,
    pub score: Option<ScoreBreakdown>,
    pub peer_comparison: Option<PeerComparison>,
    pub moving_averages: Vec<MovingAverage>,
    pub annual_prices: Vec<AnnualPriceReference>,
}

/// One row in the market screen results.
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ScreenItem {
    pub stock_no: String,
    pub name: String,
    pub industry_name: String,
    pub fiscal_year: i64,
    pub roce_percent: Option<f64>,
    pub operating_margin: Option<f64>,
    pub roe: Option<f64>,
    pub debt_ratio: Option<f64>,
    pub current_ratio: Option<f64>,
    pub eps: Option<f64>,
    pub total_assets: Option<f64>,
    pub score: Option<f64>,
    pub coverage: Option<f64>,
    pub industry_rank: Option<usize>,
    pub industry_count: Option<usize>,
    pub industry_median_roce: Option<f64>,
    pub size_band: Option<CompanySizeBand>,
    pub size_rank: Option<usize>,
    pub size_count: Option<usize>,
    pub size_median_roce: Option<f64>,
}

/// Lightweight watchlist card (stock + latest annual key metrics).
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WatchlistCard {
    pub stock_no: String,
    pub name: String,
    pub industry_name: String,
    pub close_price: Option<f64>,
    pub price_change_percent: Option<f64>,
    pub fiscal_year: Option<i64>,
    pub roce_percent: Option<f64>,
    pub roce_with_interest_percent: Option<f64>,
    pub fcf_per_share: Option<f64>,
    pub eps: Option<f64>,
    pub score: Option<f64>,
    pub debt_ratio_percent: Option<f64>,
}

/// Incoming payload for adding a watchlist item.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AddWatchlistPayload {
    pub stock_no: String,
}

/// Score template DTO for GET/PUT /api/score-template.
#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScoreTemplateDto {
    pub name: String,
    pub rules: Vec<ScoreRule>,
}
