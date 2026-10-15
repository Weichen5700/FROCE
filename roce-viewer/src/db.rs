use anyhow::Result;
use rusqlite::{Connection, Row, params};
use std::{collections::BTreeMap, path::Path};

use crate::{
    calc,
    models::{
        AnnualPriceReference, AnnualRow, CompanySizeBand, DailyClosingPrice, FinancialStatement,
        MarketScanProgress, MarketScanStatus, MovingAverage, PeerComparison,
        QuarterlyEpsObservation, ScoreRule, ScreenItem, Stock, StockAnalysis, WatchlistCard,
    },
};

// ---------------------------------------------------------------------------
// Connection helpers
// ---------------------------------------------------------------------------

pub fn open(path: &Path) -> Result<Connection> {
    let conn = Connection::open(path)?;
    conn.pragma_update(None, "busy_timeout", "5000")?;
    conn.pragma_update(None, "foreign_keys", "ON")?;
    Ok(conn)
}

// ---------------------------------------------------------------------------
// Row-mapping helpers
// EF Core stores `decimal` as TEXT in SQLite, so we must try TEXT → f64.
// ---------------------------------------------------------------------------

fn get_f64(row: &Row, col: &str) -> Option<f64> {
    use rusqlite::types::ValueRef;
    match row.get_ref(col) {
        Ok(ValueRef::Real(f)) => Some(f),
        Ok(ValueRef::Integer(i)) => Some(i as f64),
        Ok(ValueRef::Text(t)) => std::str::from_utf8(t).ok()?.parse().ok(),
        _ => None,
    }
}

fn get_str(row: &Row, col: &str) -> String {
    row.get::<_, String>(col).unwrap_or_default()
}

fn get_bool(row: &Row, col: &str) -> bool {
    row.get::<_, i64>(col).unwrap_or(0) != 0
}

fn map_stock(row: &Row) -> rusqlite::Result<Stock> {
    Ok(Stock {
        stock_no: get_str(row, "StockNo"),
        name: get_str(row, "Name"),
        market: row.get("Market").unwrap_or(0),
        industry_code: get_str(row, "IndustryCode"),
        industry_name: get_str(row, "IndustryName"),
        statement_kind: row.get("StatementKind").unwrap_or(0),
        is_supported: get_bool(row, "IsSupported"),
        pe_ratio: get_f64(row, "PeRatio"),
        pb_ratio: get_f64(row, "PbRatio"),
        dividend_yield: get_f64(row, "DividendYield"),
        close_price: get_f64(row, "ClosePrice"),
        price_change: get_f64(row, "PriceChange"),
        price_change_percent: get_f64(row, "PriceChangePercent"),
        day_high: get_f64(row, "DayHigh"),
        day_low: get_f64(row, "DayLow"),
        trade_volume: row.get("TradeVolume").ok(),
        quote_date: get_str(row, "QuoteDate"),
        quote_source: get_str(row, "QuoteSource"),
    })
}

fn map_stmt(row: &Row) -> rusqlite::Result<FinancialStatement> {
    Ok(FinancialStatement {
        id: row.get("Id").unwrap_or(0),
        stock_no: get_str(row, "StockNo"),
        fiscal_year: row.get("FiscalYear").unwrap_or(0),
        period: get_str(row, "Period"),
        is_annual: get_bool(row, "IsAnnual"),
        operating_revenue: get_f64(row, "OperatingRevenue"),
        operating_income: get_f64(row, "OperatingIncome"),
        interest_income: get_f64(row, "InterestIncome"),
        net_income: get_f64(row, "NetIncome"),
        current_assets: get_f64(row, "CurrentAssets"),
        total_assets: get_f64(row, "TotalAssets"),
        current_liabilities: get_f64(row, "CurrentLiabilities"),
        total_liabilities: get_f64(row, "TotalLiabilities"),
        total_equity: get_f64(row, "TotalEquity"),
        eps: get_f64(row, "Eps"),
        operating_cash_flow: get_f64(row, "OperatingCashFlow"),
        capex_ppe: get_f64(row, "CapexPpe"),
        capex_intangibles: get_f64(row, "CapexIntangibles"),
        weighted_average_shares: get_f64(row, "WeightedAverageShares"),
        weighted_average_shares_source: get_str(row, "WeightedAverageSharesSource"),
        source_kind: row.get("SourceKind").unwrap_or(0),
        source_name: get_str(row, "SourceName"),
        source_url: get_str(row, "SourceUrl"),
        is_preferred: get_bool(row, "IsPreferred"),
        fetched_at: get_str(row, "FetchedAt"),
    })
}

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

pub fn search_stocks(conn: &Connection, query: &str) -> Result<Vec<Stock>> {
    let pattern = format!("%{query}%");
    let mut stmt = conn.prepare(
        "SELECT * FROM Stocks WHERE IsSupported=1 \
         AND (StockNo LIKE ?1 OR Name LIKE ?1) ORDER BY StockNo LIMIT 50",
    )?;
    let rows: Vec<Stock> = stmt.query_map(params![pattern], map_stock)?.filter_map(|r| r.ok()).collect();
    Ok(rows)
}

pub fn get_stock(conn: &Connection, stock_no: &str) -> Result<Option<Stock>> {
    let mut stmt = conn.prepare("SELECT * FROM Stocks WHERE StockNo=?1")?;
    let mut rows = stmt.query_map(params![stock_no], map_stock)?;
    Ok(rows.next().transpose()?)
}

pub fn get_statements(conn: &Connection, stock_no: &str) -> Result<Vec<FinancialStatement>> {
    let mut stmt = conn.prepare(
        "SELECT * FROM FinancialStatements WHERE StockNo=?1 \
         ORDER BY FiscalYear DESC, FetchedAt DESC",
    )?;
    let rows: Vec<FinancialStatement> = stmt.query_map(params![stock_no], map_stmt)?.filter_map(|r| r.ok()).collect();
    Ok(rows)
}

pub fn get_watchlist_codes(conn: &Connection) -> Result<Vec<String>> {
    let mut stmt = conn.prepare("SELECT StockNo FROM Watchlist ORDER BY SortOrder, AddedAt")?;
    let rows: Vec<String> = stmt.query_map([], |r| r.get(0))?.filter_map(|r| r.ok()).collect();
    Ok(rows)
}

pub fn is_on_watchlist(conn: &Connection, stock_no: &str) -> Result<bool> {
    let n: i64 = conn.query_row(
        "SELECT COUNT(*) FROM Watchlist WHERE StockNo=?1",
        params![stock_no],
        |r| r.get(0),
    )?;
    Ok(n > 0)
}

pub fn add_watchlist(conn: &Connection, stock_no: &str) -> Result<()> {
    let max: i64 = conn
        .query_row("SELECT COALESCE(MAX(SortOrder),0) FROM Watchlist", [], |r| r.get(0))
        .unwrap_or(0);
    conn.execute(
        "INSERT OR IGNORE INTO Watchlist (StockNo, AddedAt, SortOrder) \
         VALUES (?1, datetime('now'), ?2)",
        params![stock_no, max + 1],
    )?;
    Ok(())
}

pub fn remove_watchlist(conn: &Connection, stock_no: &str) -> Result<()> {
    conn.execute("DELETE FROM Watchlist WHERE StockNo=?1", params![stock_no])?;
    Ok(())
}

pub fn upsert_stock(conn: &Connection, s: &Stock) -> Result<()> {
    conn.execute(
        "INSERT INTO Stocks \
         (StockNo,Name,Market,IndustryCode,IndustryName,StatementKind,IsSupported,\
          PeRatio,PbRatio,DividendYield,ClosePrice,PriceChange,PriceChangePercent,\
          QuoteDate,QuoteSource,QuoteFetchedAt,UpdatedAt) \
         VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,datetime('now'),datetime('now')) \
         ON CONFLICT(StockNo) DO UPDATE SET \
           Name=excluded.Name,Market=excluded.Market,IndustryCode=excluded.IndustryCode,\
           IndustryName=excluded.IndustryName,StatementKind=excluded.StatementKind,\
           IsSupported=excluded.IsSupported,PeRatio=excluded.PeRatio,PbRatio=excluded.PbRatio,\
           DividendYield=excluded.DividendYield,ClosePrice=excluded.ClosePrice,\
           PriceChange=excluded.PriceChange,PriceChangePercent=excluded.PriceChangePercent,\
           QuoteDate=excluded.QuoteDate,QuoteSource=excluded.QuoteSource,\
           QuoteFetchedAt=excluded.QuoteFetchedAt,UpdatedAt=excluded.UpdatedAt",
        params![
            s.stock_no, s.name, s.market, s.industry_code, s.industry_name,
            s.statement_kind, s.is_supported as i64,
            s.pe_ratio, s.pb_ratio, s.dividend_yield,
            s.close_price, s.price_change, s.price_change_percent,
            s.quote_date, s.quote_source
        ],
    )?;
    Ok(())
}

pub fn upsert_statement(conn: &Connection, s: &FinancialStatement) -> Result<()> {
    conn.execute(
        "INSERT INTO FinancialStatements \
         (StockNo,FiscalYear,Period,IsAnnual,OperatingRevenue,OperatingIncome,InterestIncome,\
          NetIncome,CurrentAssets,TotalAssets,CurrentLiabilities,TotalLiabilities,TotalEquity,\
          Eps,OperatingCashFlow,CapexPpe,CapexIntangibles,WeightedAverageShares,\
          WeightedAverageSharesSource,SourceKind,SourceName,SourceUrl,IsPreferred,FetchedAt) \
         VALUES \
         (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,?17,?18,?19,?20,?21,?22,?23,datetime('now')) \
         ON CONFLICT(StockNo,FiscalYear,Period,SourceName) DO UPDATE SET \
           OperatingRevenue=excluded.OperatingRevenue,OperatingIncome=excluded.OperatingIncome,\
           InterestIncome=excluded.InterestIncome,NetIncome=excluded.NetIncome,\
           CurrentAssets=excluded.CurrentAssets,TotalAssets=excluded.TotalAssets,\
           CurrentLiabilities=excluded.CurrentLiabilities,TotalLiabilities=excluded.TotalLiabilities,\
           TotalEquity=excluded.TotalEquity,Eps=excluded.Eps,\
           OperatingCashFlow=excluded.OperatingCashFlow,CapexPpe=excluded.CapexPpe,\
           CapexIntangibles=excluded.CapexIntangibles,\
           WeightedAverageShares=excluded.WeightedAverageShares,\
           WeightedAverageSharesSource=excluded.WeightedAverageSharesSource,\
           IsPreferred=excluded.IsPreferred,FetchedAt=excluded.FetchedAt",
        params![
            s.stock_no, s.fiscal_year, s.period, s.is_annual as i64,
            s.operating_revenue, s.operating_income, s.interest_income,
            s.net_income, s.current_assets, s.total_assets,
            s.current_liabilities, s.total_liabilities, s.total_equity,
            s.eps, s.operating_cash_flow, s.capex_ppe, s.capex_intangibles,
            s.weighted_average_shares, s.weighted_average_shares_source,
            s.source_kind, s.source_name, s.source_url, s.is_preferred as i64
        ],
    )?;
    Ok(())
}

// ---------------------------------------------------------------------------
// Business logic (assembles StockAnalysis from raw rows)
// ---------------------------------------------------------------------------

/// Pick the best FinancialStatement for each fiscal year.
/// Priority: IsPreferred > MOPS (source_kind=2) > latest FetchedAt.
fn choose_best(mut group: Vec<FinancialStatement>) -> FinancialStatement {
    group.sort_unstable_by(|a, b| {
        b.is_preferred
            .cmp(&a.is_preferred)
            .then_with(|| {
                let score = |s: &FinancialStatement| if s.source_kind == 2 { 1i64 } else { 0 };
                score(b).cmp(&score(a))
            })
            .then(b.fetched_at.cmp(&a.fetched_at))
    });
    group.into_iter().next().unwrap()
}

pub fn build_analysis(
    conn: &Connection,
    stock: Stock,
    statements: Vec<FinancialStatement>,
    on_watchlist: bool,
    rules: &[ScoreRule],
) -> StockAnalysis {
    let mut annual_map: BTreeMap<i64, Vec<FinancialStatement>> = Default::default();
    let mut quarter_map: std::collections::HashMap<(i64, String), Vec<FinancialStatement>> = Default::default();

    for s in statements {
        if s.is_annual {
            annual_map.entry(s.fiscal_year).or_default().push(s);
        } else {
            quarter_map.entry((s.fiscal_year, s.period.clone())).or_default().push(s);
        }
    }

    let mut annual_stmts: Vec<FinancialStatement> = annual_map.into_values().map(choose_best).collect();
    annual_stmts.sort_unstable_by(|a, b| b.fiscal_year.cmp(&a.fiscal_year));

    let mut quarters: Vec<FinancialStatement> = quarter_map.into_values().map(choose_best).collect();
    quarters.sort_unstable_by(|a, b| {
        b.fiscal_year.cmp(&a.fiscal_year).then(b.period.cmp(&a.period))
    });
    let latest_quarter = quarters.first().cloned();

    let annual_history: Vec<AnnualRow> = annual_stmts
        .iter()
        .enumerate()
        .map(|(i, s)| {
            let roce = calc::roce(s);
            let fcf_r = calc::fcf(s);
            let review = annual_stmts.get(i + 1).and_then(|p| calc::cash_flow_review_note(s, p));
            let score = calc::calculate_score(s, rules);
            AnnualRow {
                fiscal_year: s.fiscal_year,
                period: s.period.clone(),
                operating_revenue: s.operating_revenue,
                operating_income: s.operating_income,
                interest_income: s.interest_income,
                net_income: s.net_income,
                total_assets: s.total_assets,
                current_assets: s.current_assets,
                current_liabilities: s.current_liabilities,
                total_liabilities: s.total_liabilities,
                total_equity: s.total_equity,
                eps: s.eps,
                operating_cash_flow: s.operating_cash_flow,
                capex_ppe: s.capex_ppe,
                capex_intangibles: s.capex_intangibles,
                weighted_average_shares: fcf_r.shares,
                weighted_average_shares_source: fcf_r.shares_source.unwrap_or_default(),
                source_name: s.source_name.clone(),
                roce_percent: roce,
                roce_with_interest_percent: calc::roce_with_interest(s),
                roe_percent: calc::roe(s),
                operating_margin_percent: calc::operating_margin(s),
                debt_ratio_percent: calc::debt_ratio(s),
                current_ratio_percent: calc::current_ratio(s),
                roa_percent: calc::roa(s),
                fcf: fcf_r.fcf,
                fcf_per_share: fcf_r.fcf_per_share,
                score: Some(score),
                review_note: review,
            }
        })
        .collect();

    let quarterly_eps = build_quarterly_eps(&quarters);
    let score = annual_stmts.first().map(|s| calc::calculate_score(s, rules));
    let peer_comparison = if stock.statement_kind == 0 {
        annual_stmts.first().and_then(|s| build_peer_comparison(conn, &stock, s).ok().flatten())
    } else {
        None
    };

    StockAnalysis {
        stock,
        annual_history,
        latest_quarter,
        quarterly_eps_observations: quarterly_eps,
        is_on_watchlist: on_watchlist,
        score,
        peer_comparison,
    }
}

// Needs the connection — takes it as a parameter passed down from build_analysis_for_stock.
fn build_peer_comparison(conn: &Connection, stock: &Stock, selected: &FinancialStatement) -> Result<Option<PeerComparison>> {
    if stock.industry_name.is_empty() { return Ok(None); }

    let sql = "SELECT s.StockNo, \
        f.OperatingIncome, f.TotalAssets, f.CurrentLiabilities \
        FROM Stocks s \
        JOIN FinancialStatements f ON f.StockNo=s.StockNo \
        WHERE s.StatementKind=0 AND s.IndustryName=?1 AND f.IsAnnual=1 AND f.FiscalYear=?2 \
        ORDER BY s.StockNo, f.FetchedAt DESC";

    let mut stmt = conn.prepare(sql)?;
    let rows: Vec<(String, Option<f64>, Option<f64>, Option<f64>)> = stmt
        .query_map(params![stock.industry_name, selected.fiscal_year], |row| {
            Ok((
                row.get::<_, String>(0)?,
                get_f64(row, "OperatingIncome"),
                get_f64(row, "TotalAssets"),
                get_f64(row, "CurrentLiabilities"),
            ))
        })?
        .filter_map(|r| r.ok())
        .collect();

    // Dedup by stock_no, take first (already ordered by FetchedAt DESC)
    let mut seen = std::collections::HashSet::new();
    let mut peer_values: Vec<(String, f64, f64)> = rows.into_iter()
        .filter_map(|(sno, oi, ta, cl)| {
            if !seen.insert(sno.clone()) { return None; }
            let capital = ta? - cl?;
            if capital <= 0.0 { return None; }
            let roce = oi? / capital * 100.0;
            Some((sno, roce, capital))
        })
        .collect();

    if peer_values.is_empty() { return Ok(None); }

    let target_roce = match calc::roce(selected) { Some(v) => v, None => return Ok(None) };
    let target_capital = match (selected.total_assets, selected.current_liabilities) {
        (Some(ta), Some(cl)) => ta - cl,
        _ => return Ok(None),
    };

    let size_map = calc::assign_size_bands(peer_values.iter().map(|(sno, _, cap)| (sno.clone(), *cap)).collect());
    let band = *size_map.get(&stock.stock_no).unwrap_or(&CompanySizeBand::Small);

    let roce_rank = peer_values.iter().filter(|(_, r, _)| *r > target_roce).count() + 1;
    let medians: Vec<f64> = peer_values.iter().map(|(_, r, _)| *r).collect();
    let median_roce = calc::median(medians);

    let size_peers: Vec<f64> = peer_values.iter()
        .filter(|(sno, _, _)| size_map.get(sno).map_or(false, |b| *b == band))
        .map(|(_, r, _)| *r)
        .collect();
    let size_rank = size_peers.iter().filter(|&&r| r > target_roce).count() + 1;
    let size_median = calc::median(size_peers.clone());

    Ok(Some(PeerComparison {
        industry_name: stock.industry_name.clone(),
        roce_rank,
        sample_size: peer_values.len(),
        median_roce,
        capital_employed: target_capital,
        size_band: band,
        size_roce_rank: size_rank,
        size_sample_size: size_peers.len(),
        size_median_roce: size_median,
    }))
}

fn build_quarterly_eps(quarters: &[FinancialStatement]) -> Vec<QuarterlyEpsObservation> {
    // Group by fiscal year
    let mut by_year: BTreeMap<i64, Vec<&FinancialStatement>> = Default::default();
    for q in quarters { by_year.entry(q.fiscal_year).or_default().push(q); }

    let mut observations: Vec<QuarterlyEpsObservation> = Vec::new();
    for (fiscal_year, mut qs) in by_year.into_iter().rev() {
        qs.sort_unstable_by(|a, b| a.period.cmp(&b.period));
        let eps_for = |period: &str| -> Option<f64> {
            qs.iter().find(|q| q.period.eq_ignore_ascii_case(period)).and_then(|q| q.eps)
        };
        for quarter in 1i64..=4 {
            let period = if quarter == 4 { "FY".to_string() } else { format!("Q{quarter}YTD") };
            let ytd_eps = eps_for(&period);
            let prev_period = if quarter == 2 { Some("Q1YTD") } else if quarter == 3 { Some("Q2YTD") } else { None };
            let standalone_eps = if quarter == 1 {
                ytd_eps
            } else if let Some(prev) = prev_period {
                ytd_eps.zip(eps_for(prev)).map(|(cur, p)| cur - p)
            } else if quarter == 4 {
                // FY - Q3YTD
                ytd_eps.zip(eps_for("Q3YTD")).map(|(fy, q3)| fy - q3)
            } else {
                None
            };
            if ytd_eps.is_some() || standalone_eps.is_some() {
                observations.push(QuarterlyEpsObservation {
                    fiscal_year,
                    quarter,
                    ytd_eps,
                    standalone_eps,
                    label: format!("{fiscal_year} Q{quarter}"),
                });
            }
        }
    }
    observations
}

pub fn build_analysis_for_stock(conn: &Connection, stock_no: &str, rules: &[ScoreRule]) -> Result<Option<StockAnalysis>> {
    let Some(stock) = get_stock(conn, stock_no)? else { return Ok(None) };
    let stmts = get_statements(conn, stock_no)?;
    let on_wl = is_on_watchlist(conn, stock_no)?;
    Ok(Some(build_analysis(conn, stock, stmts, on_wl, rules)))
}

pub fn build_watchlist_cards(conn: &Connection, rules: &[ScoreRule]) -> Result<Vec<WatchlistCard>> {
    let codes = get_watchlist_codes(conn)?;
    let mut cards = Vec::new();
    for code in &codes {
        let Some(stock) = get_stock(conn, code)? else { continue };
        let stmts = get_statements(conn, code)?;
        let mut annual: Vec<FinancialStatement> = stmts.into_iter().filter(|s| s.is_annual).collect();
        annual.sort_unstable_by(|a, b| b.fiscal_year.cmp(&a.fiscal_year).then(b.fetched_at.cmp(&a.fetched_at)));
        let best = annual.into_iter()
            .fold(std::collections::BTreeMap::<i64,Vec<FinancialStatement>>::new(), |mut m, s| {
                m.entry(s.fiscal_year).or_default().push(s); m
            })
            .into_values().map(choose_best)
            .max_by_key(|s| s.fiscal_year);

        let (fiscal_year, roce, roce_wi, fcf_ps, eps, score, dr) = best.as_ref().map(|s| {
            let score = calc::calculate_score(s, rules);
            (Some(s.fiscal_year), calc::roce(s), calc::roce_with_interest(s),
             calc::fcf(s).fcf_per_share, s.eps, Some(score.total_score.unwrap_or(0.0)), calc::debt_ratio(s))
        }).unwrap_or((None, None, None, None, None, None, None));

        cards.push(WatchlistCard {
            stock_no: stock.stock_no,
            name: stock.name,
            industry_name: stock.industry_name,
            close_price: stock.close_price,
            price_change_percent: stock.price_change_percent,
            fiscal_year,
            roce_percent: roce,
            roce_with_interest_percent: roce_wi,
            fcf_per_share: fcf_ps,
            eps,
            score,
            debt_ratio_percent: dr,
        });
    }
    Ok(cards)
}

#[derive(Debug, Default)]
pub struct ScreenParams {
    pub min_roce: f64,
    pub min_score: f64,
    pub min_coverage: f64,
    pub year: Option<i64>,
    pub industry_name: Option<String>,
    pub size_band: Option<CompanySizeBand>,
    pub min_total_assets_billion: Option<f64>,
}

pub fn screen_stocks(conn: &Connection, params: &ScreenParams, rules: &[ScoreRule]) -> Result<Vec<ScreenItem>> {
    let year_clause = if let Some(y) = params.year {
        format!("AND f.FiscalYear = {y}")
    } else {
        "AND f.FiscalYear = (SELECT MAX(FiscalYear) FROM FinancialStatements WHERE StockNo=f.StockNo AND IsAnnual=1)".to_string()
    };

    let sql = format!(
        "SELECT s.StockNo, s.Name, s.IndustryName, f.FiscalYear, \
         f.OperatingIncome, f.OperatingRevenue, f.TotalAssets, f.CurrentLiabilities, \
         f.NetIncome, f.TotalEquity, f.Eps, f.CurrentAssets, f.TotalLiabilities \
         FROM Stocks s \
         JOIN FinancialStatements f ON f.StockNo=s.StockNo \
         WHERE s.StatementKind=0 AND s.IsSupported=1 AND f.IsAnnual=1 {year_clause} \
         ORDER BY s.StockNo"
    );

    struct RawRow {
        stock_no: String, name: String, industry: String, fy: i64,
        oi: Option<f64>, or_: Option<f64>, ta: Option<f64>, cl: Option<f64>,
        ni: Option<f64>, eq: Option<f64>, eps: Option<f64>,
        ca: Option<f64>, tl: Option<f64>,
    }

    let mut stmt = conn.prepare(&sql)?;
    let raw: Vec<RawRow> = stmt.query_map([], |row| Ok(RawRow {
        stock_no: row.get(0)?,
        name: row.get(1)?,
        industry: row.get(2)?,
        fy: row.get(3)?,
        oi: get_f64(row, "OperatingIncome"),
        or_: get_f64(row, "OperatingRevenue"),
        ta: get_f64(row, "TotalAssets"),
        cl: get_f64(row, "CurrentLiabilities"),
        ni: get_f64(row, "NetIncome"),
        eq: get_f64(row, "TotalEquity"),
        eps: get_f64(row, "Eps"),
        ca: get_f64(row, "CurrentAssets"),
        tl: get_f64(row, "TotalLiabilities"),
    }))?.filter_map(|r| r.ok()).collect();

    // Collect evaluated companies with score and ROCE
    struct Evaluated {
        item: ScreenItem,
        capital_employed: f64,
    }
    let mut evaluated: Vec<Evaluated> = Vec::new();

    // Build synthetic statements for scoring
    for r in &raw {
        let Some(ta) = r.ta else { continue };
        let Some(cl) = r.cl else { continue };
        let capital = ta - cl;
        if capital <= 0.0 { continue; }
        let Some(oi) = r.oi else { continue };
        let roce = oi / capital * 100.0;
        if roce < params.min_roce { continue; }
        if let Some(min_assets) = params.min_total_assets_billion {
            // TotalAssets stored in thousands TWD; convert to 億(100M)
            if ta / 100_000.0 < min_assets { continue; }
        }

        let fake_stmt = FinancialStatement {
            id: 0, stock_no: r.stock_no.clone(), fiscal_year: r.fy, period: "FY".to_string(),
            is_annual: true,
            operating_revenue: r.or_, operating_income: r.oi, interest_income: None,
            net_income: r.ni, current_assets: r.ca, total_assets: r.ta,
            current_liabilities: r.cl, total_liabilities: r.tl, total_equity: r.eq,
            eps: r.eps, operating_cash_flow: None, capex_ppe: None, capex_intangibles: None,
            weighted_average_shares: None, weighted_average_shares_source: String::new(),
            source_kind: 0, source_name: String::new(), source_url: String::new(),
            is_preferred: false, fetched_at: String::new(),
        };
        let score_bd = calc::calculate_score(&fake_stmt, rules);
        let total_score = score_bd.total_score;
        let coverage = score_bd.coverage_percent;

        if let Some(min_s) = Some(params.min_score) {
            if total_score.map_or(true, |s| s < min_s) { continue; }
        }
        if coverage < params.min_coverage { continue; }
        if let Some(ref ind) = params.industry_name {
            if !r.industry.eq_ignore_ascii_case(ind) { continue; }
        }

        let om = r.or_.and_then(|rv| if rv > 0.0 { r.oi.map(|o| o / rv * 100.0) } else { None });
        let roe = r.eq.and_then(|e| if e > 0.0 { r.ni.map(|n| n / e * 100.0) } else { None });
        let dr = r.ta.zip(r.tl).map(|(ta, tl)| tl / ta * 100.0);
        let cr = r.ca.zip(r.cl).and_then(|(ca, cl)| if cl > 0.0 { Some(ca / cl * 100.0) } else { None });

        evaluated.push(Evaluated {
            item: ScreenItem {
                stock_no: r.stock_no.clone(), name: r.name.clone(),
                industry_name: r.industry.clone(), fiscal_year: r.fy,
                roce_percent: Some(roce), operating_margin: om, roe, debt_ratio: dr,
                current_ratio: cr, eps: r.eps, total_assets: r.ta,
                score: total_score, coverage: Some(coverage),
                industry_rank: None, industry_count: None, industry_median_roce: None,
                size_band: None, size_rank: None, size_count: None, size_median_roce: None,
            },
            capital_employed: capital,
        });
    }

    // Build peer groups for ranking
    let mut industry_groups: std::collections::HashMap<String, Vec<(String, f64, f64)>> = Default::default();
    for e in &evaluated {
        let ind = if e.item.industry_name.is_empty() { "未分類".to_string() } else { e.item.industry_name.clone() };
        industry_groups.entry(ind).or_default().push((e.item.stock_no.clone(), e.item.roce_percent.unwrap_or(0.0), e.capital_employed));
    }

    let mut size_maps: std::collections::HashMap<String, std::collections::HashMap<String, CompanySizeBand>> = Default::default();
    for (ind, peers) in &industry_groups {
        let bands = calc::assign_size_bands(peers.iter().map(|(sno, _, cap)| (sno.clone(), *cap)).collect());
        size_maps.insert(ind.clone(), bands);
    }

    let mut items: Vec<ScreenItem> = evaluated.into_iter().filter_map(|mut e| {
        let ind = if e.item.industry_name.is_empty() { "未分類".to_string() } else { e.item.industry_name.clone() };
        let peers = industry_groups.get(&ind)?;
        let target_roce = e.item.roce_percent.unwrap_or(0.0);
        let band = size_maps.get(&ind).and_then(|m| m.get(&e.item.stock_no)).copied().unwrap_or(CompanySizeBand::Small);

        if let Some(ref filter_band) = params.size_band {
            if band != *filter_band { return None; }
        }

        let ind_rank = peers.iter().filter(|(_, r, _)| *r > target_roce).count() + 1;
        let ind_median = calc::median(peers.iter().map(|(_, r, _)| *r).collect());
        let size_peers: Vec<f64> = peers.iter()
            .filter(|(sno, _, _)| size_maps.get(&ind).and_then(|m| m.get(sno)).map_or(false, |b| *b == band))
            .map(|(_, r, _)| *r).collect();
        let size_rank = size_peers.iter().filter(|&&r| r > target_roce).count() + 1;
        let size_median = calc::median(size_peers.clone());

        e.item.industry_rank = Some(ind_rank);
        e.item.industry_count = Some(peers.len());
        e.item.industry_median_roce = Some(ind_median);
        e.item.size_band = Some(band);
        e.item.size_rank = Some(size_rank);
        e.item.size_count = Some(size_peers.len());
        e.item.size_median_roce = Some(size_median);
        Some(e.item)
    }).collect();

    items.sort_unstable_by(|a, b| {
        b.score.unwrap_or(0.0).partial_cmp(&a.score.unwrap_or(0.0))
            .unwrap_or(std::cmp::Ordering::Equal)
            .then(b.roce_percent.unwrap_or(0.0).partial_cmp(&a.roce_percent.unwrap_or(0.0)).unwrap_or(std::cmp::Ordering::Equal))
    });
    Ok(items)
}

// ---------------------------------------------------------------------------
// Annual price cache
// ---------------------------------------------------------------------------

pub fn get_annual_prices(conn: &Connection, stock_no: &str) -> Result<Vec<AnnualPriceReference>> {
    let mut stmt = conn.prepare(
        "SELECT FiscalYear, TradingDate, Close, Source FROM StockAnnualPriceCaches \
         WHERE StockNo=?1 ORDER BY FiscalYear DESC"
    )?;
    let rows: Vec<AnnualPriceReference> = stmt.query_map(params![stock_no], |row| {
        Ok(AnnualPriceReference {
            fiscal_year: row.get(0)?,
            trading_date: row.get(1)?,
            close: row.get(2)?,
            source: row.get::<_, String>(3).unwrap_or_default(),
        })
    })?.filter_map(|r| r.ok()).collect();
    Ok(rows)
}

pub fn upsert_annual_price(conn: &Connection, stock_no: &str, price: &AnnualPriceReference) -> Result<()> {
    conn.execute(
        "INSERT INTO StockAnnualPriceCaches (StockNo, FiscalYear, Close, TradingDate, Source, FetchedAt) \
         VALUES (?1,?2,?3,?4,?5,datetime('now')) \
         ON CONFLICT(StockNo,FiscalYear) DO UPDATE SET Close=excluded.Close, \
           TradingDate=excluded.TradingDate, Source=excluded.Source, FetchedAt=excluded.FetchedAt",
        params![stock_no, price.fiscal_year, price.close, price.trading_date, price.source],
    )?;
    Ok(())
}

pub fn get_daily_prices_cached(conn: &Connection, stock_no: &str) -> Result<Vec<DailyClosingPrice>> {
    let json: Option<String> = conn.query_row(
        "SELECT PricesJson FROM StockDailyPriceCaches WHERE StockNo=?1",
        params![stock_no], |r| r.get(0)
    ).ok();
    if let Some(j) = json {
        if let Ok(prices) = serde_json::from_str::<Vec<DailyClosingPrice>>(&j) {
            return Ok(prices);
        }
    }
    Ok(vec![])
}

pub fn upsert_daily_prices(conn: &Connection, stock_no: &str, prices: &[DailyClosingPrice]) -> Result<()> {
    let json = serde_json::to_string(prices).unwrap_or_default();
    conn.execute(
        "INSERT INTO StockDailyPriceCaches (StockNo, PricesJson, FetchedAt) \
         VALUES (?1,?2,datetime('now')) \
         ON CONFLICT(StockNo) DO UPDATE SET PricesJson=excluded.PricesJson, FetchedAt=excluded.FetchedAt",
        params![stock_no, json],
    )?;
    Ok(())
}

pub fn compute_moving_averages(prices: &[DailyClosingPrice]) -> Vec<MovingAverage> {
    let closes: Vec<f64> = prices.iter().map(|p| p.close).collect();
    [5, 10, 20, 60, 120, 240usize].iter().map(|&n| {
        let value = if closes.len() >= n {
            Some(closes[closes.len() - n..].iter().sum::<f64>() / n as f64)
        } else { None };
        MovingAverage { trading_days: n, label: format!("{n}日均線"), value }
    }).collect()
}

// ---------------------------------------------------------------------------
// Market scan state
// ---------------------------------------------------------------------------

pub fn get_scan_state(conn: &Connection) -> Result<MarketScanProgress> {
    let row = conn.query_row(
        "SELECT FiscalYear, Total, Completed, Success, Failed, Status, \
         CurrentStockNo, RangeStartFiscalYear, RangeEndFiscalYear \
         FROM MarketScanStates WHERE Id=1",
        [],
        |r| Ok((
            r.get::<_,i64>(0)?,
            r.get::<_,i64>(1).unwrap_or(0) as usize,
            r.get::<_,i64>(2).unwrap_or(0) as usize,
            r.get::<_,i64>(3).unwrap_or(0) as usize,
            r.get::<_,i64>(4).unwrap_or(0) as usize,
            r.get::<_,i64>(5).unwrap_or(0),
            r.get::<_,Option<String>>(6)?,
            r.get::<_,Option<i64>>(7)?,
            r.get::<_,Option<i64>>(8)?,
        ))
    );
    match row {
        Ok((fy, total, completed, success, failed, status_raw, current, range_start, range_end)) => {
            let status = match status_raw {
                1 => MarketScanStatus::Running,
                2 => MarketScanStatus::Paused,
                3 => MarketScanStatus::Completed,
                _ => MarketScanStatus::Idle,
            };
            Ok(MarketScanProgress { status, fiscal_year: fy, total, completed, success, failed, current_stock_no: current, range_start_fiscal_year: range_start, range_end_fiscal_year: range_end })
        }
        Err(_) => {
            let total = conn.query_row("SELECT COUNT(*) FROM Stocks WHERE StatementKind=0 AND IsSupported=1", [], |r| r.get::<_,i64>(0)).unwrap_or(0) as usize;
            Ok(MarketScanProgress { status: MarketScanStatus::Idle, fiscal_year: 0, total, completed: 0, success: 0, failed: 0, current_stock_no: None, range_start_fiscal_year: None, range_end_fiscal_year: None })
        }
    }
}

pub fn upsert_scan_state(conn: &Connection, p: &MarketScanProgress) -> Result<()> {
    let status_raw: i64 = match p.status {
        MarketScanStatus::Idle => 0,
        MarketScanStatus::Running => 1,
        MarketScanStatus::Paused => 2,
        MarketScanStatus::Completed => 3,
    };
    conn.execute(
        "INSERT INTO MarketScanStates (Id,FiscalYear,Total,Completed,Success,Failed,Status,CurrentStockNo,RangeStartFiscalYear,RangeEndFiscalYear,StartedAt,UpdatedAt) \
         VALUES (1,?1,?2,?3,?4,?5,?6,?7,?8,?9,datetime('now'),datetime('now')) \
         ON CONFLICT(Id) DO UPDATE SET FiscalYear=excluded.FiscalYear,Total=excluded.Total,\
           Completed=excluded.Completed,Success=excluded.Success,Failed=excluded.Failed,\
           Status=excluded.Status,CurrentStockNo=excluded.CurrentStockNo,\
           RangeStartFiscalYear=excluded.RangeStartFiscalYear,\
           RangeEndFiscalYear=excluded.RangeEndFiscalYear,UpdatedAt=excluded.UpdatedAt",
        params![p.fiscal_year, p.total as i64, p.completed as i64, p.success as i64,
                p.failed as i64, status_raw, p.current_stock_no, p.range_start_fiscal_year, p.range_end_fiscal_year],
    )?;
    Ok(())
}

pub fn list_pending_scan_stocks(conn: &Connection, fiscal_year: i64, count: usize) -> Result<Vec<String>> {
    let mut stmt = conn.prepare(
        "SELECT s.StockNo FROM Stocks s \
         WHERE s.StatementKind=0 AND s.IsSupported=1 \
         AND NOT EXISTS ( \
           SELECT 1 FROM FinancialStatements f \
           WHERE f.StockNo=s.StockNo AND f.FiscalYear=?1 AND f.IsAnnual=1 \
             AND f.OperatingIncome IS NOT NULL AND f.TotalAssets IS NOT NULL \
             AND f.CurrentLiabilities IS NOT NULL AND f.OperatingCashFlow IS NOT NULL \
             AND f.CapexPpe IS NOT NULL \
         ) AND NOT EXISTS ( \
           SELECT 1 FROM MarketScanItems i \
           WHERE i.StockNo=s.StockNo AND i.FiscalYear=?1 AND i.Success=0 \
         ) \
         ORDER BY s.StockNo LIMIT ?2"
    )?;
    let rows: Vec<String> = stmt.query_map(params![fiscal_year, count as i64], |r| r.get(0))?.filter_map(|r| r.ok()).collect();
    Ok(rows)
}

pub fn record_scan_item(conn: &Connection, stock_no: &str, fiscal_year: i64, success: bool, error: Option<&str>) -> Result<()> {
    conn.execute(
        "INSERT INTO MarketScanItems (StockNo, FiscalYear, Success, ErrorMessage, AttemptedAt) \
         VALUES (?1,?2,?3,?4,datetime('now')) \
         ON CONFLICT(StockNo,FiscalYear) DO UPDATE SET \
           Success=excluded.Success,ErrorMessage=excluded.ErrorMessage,AttemptedAt=excluded.AttemptedAt",
        params![stock_no, fiscal_year, success as i64, error],
    )?;
    Ok(())
}

pub fn scan_item_table_exists(conn: &Connection) -> bool {
    conn.query_row(
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MarketScanItems'",
        [], |r| r.get::<_,i64>(0)
    ).unwrap_or(0) > 0
}

pub fn ensure_scan_tables(conn: &Connection) -> Result<()> {
    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS MarketScanStates ( \
           Id INTEGER PRIMARY KEY, FiscalYear INTEGER NOT NULL DEFAULT 0, \
           RangeStartFiscalYear INTEGER, RangeEndFiscalYear INTEGER, \
           Status INTEGER NOT NULL DEFAULT 0, \
           Total INTEGER NOT NULL DEFAULT 0, Completed INTEGER NOT NULL DEFAULT 0, \
           Success INTEGER NOT NULL DEFAULT 0, Failed INTEGER NOT NULL DEFAULT 0, \
           CurrentStockNo TEXT, StartedAt TEXT, UpdatedAt TEXT \
         ); \
         CREATE TABLE IF NOT EXISTS MarketScanItems ( \
           StockNo TEXT NOT NULL, FiscalYear INTEGER NOT NULL, \
           Success INTEGER NOT NULL DEFAULT 0, ErrorMessage TEXT, AttemptedAt TEXT, \
           PRIMARY KEY (StockNo, FiscalYear) \
         );"
    )?;
    Ok(())
}

// ---------------------------------------------------------------------------
// Score template persistence
// ---------------------------------------------------------------------------

pub fn load_score_rules(conn: &Connection) -> Vec<ScoreRule> {
    let json: Option<String> = conn.query_row(
        "SELECT RulesJson FROM ScoreTemplates WHERE IsDefault=1 LIMIT 1",
        [], |r| r.get(0)
    ).ok();
    if let Some(j) = json {
        if let Ok(rules) = serde_json::from_str::<Vec<ScoreRule>>(&j) {
            if !rules.is_empty() { return rules; }
        }
    }
    calc::default_rules()
}

pub fn save_score_rules(conn: &Connection, name: &str, rules: &[ScoreRule]) -> Result<()> {
    let json = serde_json::to_string(rules).unwrap_or_default();
    conn.execute(
        "INSERT INTO ScoreTemplates (Name, IsDefault, RulesJson) VALUES (?1,1,?2) \
         ON CONFLICT(IsDefault) DO UPDATE SET Name=excluded.Name, RulesJson=excluded.RulesJson",
        params![name, json],
    )?;
    Ok(())
}

pub fn ensure_score_template_table(conn: &Connection) -> Result<()> {
    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS ScoreTemplates ( \
           Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, \
           IsDefault INTEGER NOT NULL DEFAULT 0, RulesJson TEXT NOT NULL DEFAULT '[]' \
         ); \
         CREATE UNIQUE INDEX IF NOT EXISTS UX_ScoreTemplates_IsDefault ON ScoreTemplates(IsDefault) WHERE IsDefault=1;"
    )?;
    Ok(())
}
