/// HTTP fetching from TWSE / TPEx OpenAPI and MOPS iXBRL HTML.
/// Mirrors the logic in OfficialDataProvider.cs + MopsAnnualReportProvider.cs.
use anyhow::{anyhow, Result};
use once_cell::sync::Lazy;
use regex::Regex;
use serde_json::Value;

use crate::models::{FinancialStatement, Stock};

// ---------------------------------------------------------------------------
// URL constants
// ---------------------------------------------------------------------------

const TWSE: &str = "https://openapi.twse.com.tw/v1";
const TPEX: &str = "https://www.tpex.org.tw/openapi/v1";
const MOPS_BASE: &str = "https://mopsov.twse.com.tw/server-java/t164sb01";

// ---------------------------------------------------------------------------
// Catalog & latest-statements fetch (TWSE / TPEx OpenAPI)
// ---------------------------------------------------------------------------

pub async fn fetch_catalog_and_statements(
    client: &reqwest::Client,
) -> Result<(Vec<Stock>, Vec<FinancialStatement>)> {
    // Pre-build URLs to avoid format!() temporaries inside try_join!
    let u_listed_info    = format!("{TWSE}/opendata/t187ap03_L");
    let u_otc_info       = format!("{TPEX}/mopsfin_t187ap03_O");
    let u_listed_ind     = format!("{TWSE}/opendata/t187ap14_L");
    let u_otc_ind        = format!("{TPEX}/mopsfin_t187ap14_O");
    let u_listed_val     = format!("{TWSE}/exchangeReport/BWIBBU_ALL");
    let u_otc_val        = format!("{TPEX}/tpex_mainboard_peratio_analysis");
    let u_listed_income  = format!("{TWSE}/opendata/t187ap06_L_ci");
    let u_otc_income     = format!("{TPEX}/mopsfin_t187ap06_O_ci");
    let u_listed_balance = format!("{TWSE}/opendata/t187ap07_L_ci");
    let u_otc_balance    = format!("{TPEX}/mopsfin_t187ap07_O_ci");

    let (listed_info, otc_info, listed_industry, otc_industry, listed_val, otc_val,
         listed_income, otc_income, listed_balance, otc_balance) = tokio::try_join!(
        get_json(client, &u_listed_info),
        get_json(client, &u_otc_info),
        get_json(client, &u_listed_ind),
        get_json(client, &u_otc_ind),
        get_json(client, &u_listed_val),
        get_json(client, &u_otc_val),
        get_json(client, &u_listed_income),
        get_json(client, &u_otc_income),
        get_json(client, &u_listed_balance),
        get_json(client, &u_otc_balance),
    )?;

    // Build industry name lookup
    let industry_names: std::collections::HashMap<String, String> = listed_industry
        .iter()
        .chain(otc_industry.iter())
        .filter_map(|v| {
            let code = str_field(v, &["公司代號", "SecuritiesCompanyCode"]);
            let name = str_field(v, &["產業別"]).trim().to_string();
            if code.is_empty() || name.is_empty() { None } else { Some((code, name)) }
        })
        .collect();

    // Build valuation lookup keyed by stock code
    let mut pe: std::collections::HashMap<String, (f64, f64, f64)> = Default::default();
    for v in &listed_val {
        let code = str_field(v, &["Code"]);
        if code.is_empty() { continue; }
        pe.insert(code, (
            parse_f64(v, "PEratio").unwrap_or(0.0),
            parse_f64(v, "PBratio").unwrap_or(0.0),
            parse_f64(v, "DividendYield").unwrap_or(0.0),
        ));
    }
    for v in &otc_val {
        let code = str_field(v, &["SecuritiesCompanyCode"]);
        if code.is_empty() { continue; }
        pe.insert(code, (
            parse_f64(v, "PriceEarningRatio").unwrap_or(0.0),
            parse_f64(v, "PriceBookRatio").unwrap_or(0.0),
            parse_f64(v, "YieldRatio").unwrap_or(0.0),
        ));
    }

    // Build stock list
    let mut stocks: Vec<Stock> = Vec::new();
    for (arr, market) in [(&listed_info, 0i64), (&otc_info, 1i64)] {
        for v in arr {
            let sno = str_field(v, &["公司代號", "SecuritiesCompanyCode"]);
            if sno.is_empty() { continue; }
            let ind_code = str_field(v, &["產業別", "IndustryCode"]);
            let ind_name = industry_names.get(&sno).cloned().unwrap_or_default();
            let val = pe.get(&sno);
            stocks.push(Stock {
                stock_no: sno.clone(),
                name: str_field(v, &["公司名稱", "CompanyName", "公司簡稱"]),
                market,
                industry_code: ind_code,
                industry_name: ind_name,
                statement_kind: 0, // General
                is_supported: true,
                pe_ratio: val.map(|t| if t.0 > 0.0 { Some(t.0) } else { None }).flatten(),
                pb_ratio: val.map(|t| if t.1 > 0.0 { Some(t.1) } else { None }).flatten(),
                dividend_yield: val.map(|t| if t.2 > 0.0 { Some(t.2) } else { None }).flatten(),
                close_price: None,
                price_change: None,
                price_change_percent: None,
                day_high: None,
                day_low: None,
                trade_volume: None,
                quote_date: String::new(),
                quote_source: "TWSE/TPEx OpenAPI".to_string(),
            });
        }
    }

    // Build financial statements from income + balance sheet arrays
    let mut stmts: Vec<FinancialStatement> = Vec::new();
    let url_listed_income = format!("{TWSE}/opendata/t187ap06_L_ci");
    let url_otc_income    = format!("{TPEX}/mopsfin_t187ap06_O_ci");
    stmts.extend(join_statements(
        &listed_income, &listed_balance, 0,
        "證交所 OpenAPI", &url_listed_income,
    ));
    stmts.extend(join_statements(
        &otc_income, &otc_balance, 1,
        "櫃買中心 OpenAPI", &url_otc_income,
    ));

    Ok((stocks, stmts))
}

fn join_statements(
    income: &[Value],
    balance: &[Value],
    source_kind: i64,
    source_name: &str,
    source_url: &str,
) -> Vec<FinancialStatement> {
    let balance_map: std::collections::HashMap<String, &Value> = balance
        .iter()
        .filter_map(|v| {
            let k = str_field(v, &["公司代號", "SecuritiesCompanyCode"]);
            if k.is_empty() { None } else { Some((k, v)) }
        })
        .collect();

    income
        .iter()
        .filter_map(|inc| {
            let sno = str_field(inc, &["公司代號", "SecuritiesCompanyCode"]);
            if sno.is_empty() { return None; }
            let bal = balance_map.get(&sno)?;
            let fy = parse_roc_year(inc)?;
            Some(FinancialStatement {
                id: 0,
                stock_no: sno,
                fiscal_year: fy,
                period: "FY".to_string(),
                is_annual: true,
                operating_revenue: parse_thousands(inc, &["營業收入", "OperatingRevenue"]),
                operating_income: parse_thousands(inc, &["營業利益", "OperatingIncome"]),
                interest_income: None,
                net_income: parse_thousands(inc, &["本期淨利", "NetIncome", "本期稅後淨利"]),
                current_assets: parse_thousands(bal, &["流動資產", "CurrentAssets"]),
                total_assets: parse_thousands(bal, &["資產總額", "TotalAssets"]),
                current_liabilities: parse_thousands(bal, &["流動負債", "CurrentLiabilities"]),
                total_liabilities: parse_thousands(bal, &["負債總額", "TotalLiabilities"]),
                total_equity: parse_thousands(bal, &["權益總額", "TotalEquity"]),
                eps: parse_f64(inc, "每股盈餘")
                    .or_else(|| parse_f64(inc, "EPS")),
                operating_cash_flow: None,
                capex_ppe: None,
                capex_intangibles: None,
                weighted_average_shares: None,
                weighted_average_shares_source: String::new(),
                source_kind,
                source_name: source_name.to_string(),
                source_url: source_url.to_string(),
                is_preferred: false,
                fetched_at: utc_now(),
            })
        })
        .collect()
}

// ---------------------------------------------------------------------------
// MOPS iXBRL fetch
// ---------------------------------------------------------------------------

/// Fetch MOPS annual report (quarter=4).
pub async fn fetch_mops_annual(
    client: &reqwest::Client,
    stock_no: &str,
    fiscal_year: i32,
) -> Result<Option<FinancialStatement>> {
    fetch_mops(client, stock_no, fiscal_year, 4, true).await
}

/// Fetch MOPS quarterly report (quarter 1–3 is YTD; 4 is annual).
pub async fn fetch_mops_quarter(
    client: &reqwest::Client,
    stock_no: &str,
    fiscal_year: i32,
    quarter: i32,
) -> Result<Option<FinancialStatement>> {
    if quarter < 1 || quarter > 4 { return Ok(None); }
    fetch_mops(client, stock_no, fiscal_year, quarter, false).await
}

pub async fn fetch_mops(
    client: &reqwest::Client,
    stock_no: &str,
    fiscal_year: i32,
    quarter: i32,
    annual: bool,
) -> Result<Option<FinancialStatement>> {
    for report_id in ["C", "A"] {
        let url = format!(
            "{MOPS_BASE}?step=1&CO_ID={}&SYEAR={}&SSEASON={}&REPORT_ID={}",
            urlenc(stock_no), fiscal_year, quarter, report_id
        );
        if let Ok(html) = get_text(client, &url).await {
            if let Some(stmt) = parse_mops_html(&html, stock_no, fiscal_year, quarter, annual, &url) {
                return Ok(Some(stmt));
            }
        }
    }
    Ok(None)
}

// ---------------------------------------------------------------------------
// MOPS iXBRL HTML parser
// ---------------------------------------------------------------------------

static NON_FRACTION_RE: Lazy<Regex> = Lazy::new(|| {
    Regex::new(r"(?s)<ix:nonFraction(?P<attrs>[^>]*)>(?P<value>.*?)</ix:nonFraction>").unwrap()
});
static TAG_RE: Lazy<Regex> = Lazy::new(|| Regex::new(r"<[^>]+>").unwrap());
static ATTR_RE: Lazy<Regex> = Lazy::new(|| Regex::new(r#"(\w+)="([^"]*)"#).unwrap());

fn get_attr(attrs: &str, name: &str) -> String {
    ATTR_RE
        .captures_iter(attrs)
        .find(|c| c[1].eq_ignore_ascii_case(name))
        .map(|c| c[2].to_string())
        .unwrap_or_default()
}

fn extract_xbrl(html: &str, context: &str, normalize_to_thousands: bool, concepts: &[&str]) -> Option<f64> {
    for cap in NON_FRACTION_RE.captures_iter(html) {
        let attrs = &cap["attrs"];
        let name = get_attr(attrs, "name");
        if !concepts.iter().any(|c| c.eq_ignore_ascii_case(&name)) { continue; }
        if !get_attr(attrs, "contextRef").eq_ignore_ascii_case(context) { continue; }

        let raw = TAG_RE.replace_all(&cap["value"], "").to_string();
        let text = html_unescape(&raw)
            .replace(',', "")
            .replace('(', "-")
            .replace(')', "")
            .trim()
            .to_string();

        let mut val: f64 = text.parse().ok()?;

        // sign="-" attribute takes priority over parenthesis notation
        if get_attr(attrs, "sign") == "-" { val = -val.abs(); }

        let scale: i32 = get_attr(attrs, "scale").parse().unwrap_or(0);
        if scale != 0 { val *= 10f64.powi(scale); }
        return if normalize_to_thousands { Some(val / 1000.0) } else { Some(val) };
    }
    None
}

fn days_in_month(year: i32, month: i32) -> i32 {
    match month {
        1 | 3 | 5 | 7 | 8 | 10 | 12 => 31,
        4 | 6 | 9 | 11 => 30,
        2 => if year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) { 29 } else { 28 },
        _ => 30,
    }
}

pub fn parse_mops_html(
    html: &str,
    stock_no: &str,
    fiscal_year: i32,
    quarter: i32,
    annual: bool,
    source_url: &str,
) -> Option<FinancialStatement> {
    let end_month = quarter * 3;
    let end_day = days_in_month(fiscal_year, end_month);
    let end = format!("{fiscal_year:04}{end_month:02}{end_day:02}");
    let duration = format!("From{fiscal_year:04}0101To{end}");
    let instant = format!("AsOf{end}");

    let wa_shares = extract_xbrl(html, &duration, false, &[
        "ifrs-full:WeightedAverageNumberOfOrdinarySharesOutstandingBasic",
        "ifrs-full:WeightedAverageNumberOfOrdinarySharesOutstandingDiluted",
        "tifrs-notes:WeightedAverageNumberOfOrdinarySharesOutstandingBasic",
    ]);

    let mut stmt = FinancialStatement {
        id: 0,
        stock_no: stock_no.to_string(),
        fiscal_year: fiscal_year as i64,
        period: if annual { "FY".to_string() } else { format!("Q{quarter}YTD") },
        is_annual: annual,
        operating_revenue: extract_xbrl(html, &duration, true, &["ifrs-full:Revenue"]),
        operating_income: extract_xbrl(html, &duration, true, &[
            "ifrs-full:ProfitLossFromOperatingActivities",
            "tifrs-bsci-ci:OperatingIncomeLoss",
        ]),
        interest_income: extract_xbrl(html, &duration, true, &[
            "ifrs-full:RevenueFromInterest",
            "ifrs-full:InterestIncome",
            "ifrs-full:InterestRevenueCalculatedUsingEffectiveInterestMethod",
            "tifrs-bsci-ci:InterestIncome",
        ]),
        net_income: extract_xbrl(html, &duration, true, &["ifrs-full:ProfitLoss"]),
        current_assets: extract_xbrl(html, &instant, true, &["ifrs-full:CurrentAssets"]),
        total_assets: extract_xbrl(html, &instant, true, &["ifrs-full:Assets"]),
        current_liabilities: extract_xbrl(html, &instant, true, &["ifrs-full:CurrentLiabilities"]),
        total_liabilities: extract_xbrl(html, &instant, true, &["ifrs-full:Liabilities"]),
        total_equity: extract_xbrl(html, &instant, true, &["ifrs-full:Equity"]),
        eps: extract_xbrl(html, &duration, false, &["ifrs-full:BasicEarningsLossPerShare"]),
        operating_cash_flow: extract_xbrl(html, &duration, true, &[
            "ifrs-full:CashFlowsFromUsedInOperatingActivities",
            "tifrs-SCF:CashFlowsFromUsedInOperatingActivities",
        ]),
        capex_ppe: extract_xbrl(html, &duration, true, &[
            "ifrs-full:PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities",
        ]),
        capex_intangibles: extract_xbrl(html, &duration, true, &[
            "ifrs-full:PurchaseOfIntangibleAssetsClassifiedAsInvestingActivities",
        ]),
        weighted_average_shares: wa_shares,
        weighted_average_shares_source: if wa_shares.is_some() {
            "MOPS iXBRL 加權平均股數".to_string()
        } else {
            String::new()
        },
        source_kind: 2, // Mops
        source_name: "MOPS iXBRL".to_string(),
        source_url: source_url.to_string(),
        is_preferred: false,
        fetched_at: utc_now(),
    };

    // Estimate shares from NetIncome/EPS if not declared
    if stmt.weighted_average_shares.is_none() {
        if let (Some(ni), Some(eps)) = (stmt.net_income, stmt.eps) {
            if eps != 0.0 {
                let est = ni * 1000.0 / eps;
                if est > 0.0 {
                    stmt.weighted_average_shares = Some(est);
                    stmt.weighted_average_shares_source = "NetIncome/EPS 推估".to_string();
                }
            }
        }
    }

    if stmt.total_assets.is_none() && stmt.operating_income.is_none() {
        return None;
    }
    Some(stmt)
}

// ---------------------------------------------------------------------------
// HTTP helpers
// ---------------------------------------------------------------------------

async fn get_json(client: &reqwest::Client, url: &str) -> Result<Vec<Value>> {
    let text = get_text(client, url).await?;
    serde_json::from_str::<Vec<Value>>(&text).map_err(|e| anyhow!("{url}: {e}"))
}

async fn get_text(client: &reqwest::Client, url: &str) -> Result<String> {
    let resp = client.get(url).send().await?;
    if !resp.status().is_success() {
        return Err(anyhow!("HTTP {} from {url}", resp.status()));
    }
    Ok(resp.text().await?)
}

// ---------------------------------------------------------------------------
// Field-parsing utilities
// ---------------------------------------------------------------------------

fn str_field(v: &Value, keys: &[&str]) -> String {
    for k in keys {
        if let Some(s) = v.get(k).and_then(|x| x.as_str()) {
            let s = s.trim();
            if !s.is_empty() { return s.to_string(); }
        }
    }
    String::new()
}

fn parse_f64(v: &Value, key: &str) -> Option<f64> {
    let s = v.get(key)?.as_str()?.trim().replace(',', "");
    s.parse().ok().filter(|&n: &f64| n.is_finite())
}

/// Parse thousands from a JSON field (value already in thousands from the API).
fn parse_thousands(v: &Value, keys: &[&str]) -> Option<f64> {
    for k in keys {
        if let Some(n) = parse_f64(v, k) {
            return Some(n);
        }
    }
    None
}

/// Parse ROC year (民國年) or CE year from a JSON row.
fn parse_roc_year(v: &Value) -> Option<i64> {
    for key in ["年度", "Year", "FiscalYear"] {
        if let Some(s) = v.get(key).and_then(|x| x.as_str()) {
            let n: i64 = s.trim().parse().ok()?;
            // ROC year < 200 → add 1911
            return Some(if n < 200 { n + 1911 } else { n });
        }
    }
    None
}

fn urlenc(s: &str) -> String {
    s.chars()
        .map(|c| if c.is_ascii_alphanumeric() || c == '-' || c == '_' { c.to_string() } else {
            format!("%{:02X}", c as u8)
        })
        .collect()
}

fn html_unescape(s: &str) -> String {
    s.replace("&amp;", "&")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&quot;", "\"")
        .replace("&#39;", "'")
        .replace("&nbsp;", " ")
}

/// Simple UTC timestamp string for SQLite TEXT columns.
fn utc_now() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let s = secs % 60;
    let m = (secs / 60) % 60;
    let h = (secs / 3600) % 24;
    let days = secs / 86400;
    // Very rough date from epoch (good enough for a sorting key)
    let year = 1970 + days / 365;
    format!("{year:04}-01-01 {h:02}:{m:02}:{s:02}")
}

// ---------------------------------------------------------------------------
// TWSE daily closing price (STOCK_DAY per-month endpoint)
// ---------------------------------------------------------------------------

use crate::models::{AnnualPriceReference, DailyClosingPrice};

/// Fetch all available daily closing prices for a stock via STOCK_DAY month-by-month.
/// Returns up to ~30 months of data going backwards from today.
pub async fn fetch_daily_prices(
    client: &reqwest::Client,
    stock_no: &str,
) -> Result<Vec<DailyClosingPrice>> {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs();
    let days = secs / 86400;
    let today_year = (1970 + days / 365) as i32;
    let today_month = ((days % 365) / 30 + 1).min(12) as i32;

    let mut result: Vec<DailyClosingPrice> = Vec::new();
    let mut year = today_year;
    let mut month = today_month;

    for _ in 0..30 {
        let date_str = format!("{year}{month:02}01");
        let url = format!("{TWSE}/exchangeReport/STOCK_DAY?response=json&date={date_str}&stockNo={stock_no}");
        if let Ok(text) = get_text(client, &url).await {
            if let Ok(json) = serde_json::from_str::<serde_json::Value>(&text) {
                if let Some(rows) = json.get("data").and_then(|d| d.as_array()) {
                    for row in rows {
                        if let Some(arr) = row.as_array() {
                            // columns: 日期, 成交股數, 成交金額, 開盤價, 最高價, 最低價, 收盤價, 漲跌價差, 成交筆數
                            let date_roc = arr.get(0).and_then(|v| v.as_str()).unwrap_or("").trim().to_string();
                            let close_str = arr.get(6).and_then(|v| v.as_str()).unwrap_or("").replace(',', "");
                            if let (Some(date_ce), Ok(close)) = (roc_date_to_ce(&date_roc), close_str.parse::<f64>()) {
                                result.push(DailyClosingPrice { date: date_ce, close });
                            }
                        }
                    }
                }
            }
        }
        month -= 1;
        if month == 0 { month = 12; year -= 1; }
        if year < today_year - 2 { break; }
    }

    result.sort_unstable_by(|a, b| a.date.cmp(&b.date));
    Ok(result)
}

/// Fetch the last available trading day close price for a given fiscal year.
pub async fn fetch_annual_last_close(
    client: &reqwest::Client,
    stock_no: &str,
    fiscal_year: i32,
) -> Result<Option<AnnualPriceReference>> {
    // Try December first, then November, then October
    for month in [12i32, 11, 10] {
        let date_str = format!("{fiscal_year}{month:02}01");
        let url = format!("{TWSE}/exchangeReport/STOCK_DAY?response=json&date={date_str}&stockNo={stock_no}");
        if let Ok(text) = get_text(client, &url).await {
            if let Ok(json) = serde_json::from_str::<serde_json::Value>(&text) {
                if let Some(rows) = json.get("data").and_then(|d| d.as_array()) {
                    // Find last row in this month with a valid close
                    let last = rows.iter().rev().find_map(|row| {
                        let arr = row.as_array()?;
                        let date_roc = arr.get(0)?.as_str()?.trim().to_string();
                        let close_str = arr.get(6)?.as_str()?.replace(',', "");
                        let close: f64 = close_str.parse().ok()?;
                        let date_ce = roc_date_to_ce(&date_roc)?;
                        Some((date_ce, close))
                    });
                    if let Some((date, close)) = last {
                        return Ok(Some(AnnualPriceReference {
                            fiscal_year: fiscal_year as i64,
                            trading_date: Some(date),
                            close: Some(close),
                            source: "TWSE STOCK_DAY".to_string(),
                        }));
                    }
                }
            }
        }
    }
    Ok(None)
}

/// Convert ROC date string "112/12/29" → "2023-12-29" (CE ISO date).
fn roc_date_to_ce(roc: &str) -> Option<String> {
    let parts: Vec<&str> = roc.split('/').collect();
    if parts.len() != 3 { return None; }
    let roc_year: i32 = parts[0].trim().parse().ok()?;
    let ce_year = roc_year + 1911;
    let m: u32 = parts[1].trim().parse().ok()?;
    let d: u32 = parts[2].trim().parse().ok()?;
    Some(format!("{ce_year:04}-{m:02}-{d:02}"))
}
