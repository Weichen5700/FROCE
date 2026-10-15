use crate::models::{CompanySizeBand, FinancialStatement, MetricKey, ScoreBreakdown, ScoreRule, MetricResult, ThresholdPoint};

/// ROCE = OperatingIncome / (TotalAssets - CurrentLiabilities) × 100
pub fn roce(s: &FinancialStatement) -> Option<f64> {
    let capital = s.total_assets? - s.current_liabilities?;
    if capital <= 0.0 { return None; }
    Some(s.operating_income? / capital * 100.0)
}

/// ROCE+interest = (OperatingIncome + InterestIncome) / CapitalEmployed × 100
pub fn roce_with_interest(s: &FinancialStatement) -> Option<f64> {
    let capital = s.total_assets? - s.current_liabilities?;
    if capital <= 0.0 { return None; }
    Some((s.operating_income? + s.interest_income?) / capital * 100.0)
}

pub fn current_ratio(s: &FinancialStatement) -> Option<f64> {
    pct_ratio(s.current_assets, s.current_liabilities)
}

pub fn roa(s: &FinancialStatement) -> Option<f64> {
    pct_ratio(s.net_income, s.total_assets)
}

pub fn static_pe(close_price: Option<f64>, eps: Option<f64>) -> Option<f64> {
    match (close_price, eps) {
        (Some(p), Some(e)) if p > 0.0 && e > 0.0 => Some(p / e),
        _ => None,
    }
}

pub fn book_value_per_share(s: &FinancialStatement) -> Option<f64> {
    // equity in thousands TWD; shares are individual shares
    let eq = s.total_equity.filter(|&v| v > 0.0)?;
    let sh = estimated_shares(s).filter(|&v| v > 0.0)?;
    Some(eq * 1000.0 / sh)
}

pub fn static_pb(close_price: Option<f64>, s: &FinancialStatement) -> Option<f64> {
    let bv = book_value_per_share(s).filter(|&v| v > 0.0)?;
    let p = close_price.filter(|&v| v > 0.0)?;
    Some(p / bv)
}

pub fn ytd_annualization_factor(period: &str) -> Option<f64> {
    let bytes = period.as_bytes();
    if bytes.len() < 2 || bytes[0].to_ascii_uppercase() != b'Q' { return None; }
    let q = (bytes[1] as char).to_digit(10)? as f64;
    if q < 1.0 || q > 4.0 { return None; }
    Some(4.0 / q)
}

pub fn rough_annualize_ytd_roce(roce_percent: Option<f64>, period: &str) -> Option<f64> {
    Some(roce_percent? * ytd_annualization_factor(period)?)
}

fn estimated_shares(s: &FinancialStatement) -> Option<f64> {
    if s.weighted_average_shares.is_some_and(|v| v > 0.0) {
        return s.weighted_average_shares;
    }
    let est = s.net_income? * 1000.0 / s.eps?;
    if est > 0.0 { Some(est) } else { None }
}

pub struct FcfResult {
    pub capex: Option<f64>,
    pub fcf: Option<f64>,
    pub shares: Option<f64>,
    pub shares_source: Option<String>,
    pub fcf_per_share: Option<f64>,
}

/// FCF_PPE = OperatingCashFlow - |CapexPpe|
pub fn fcf(s: &FinancialStatement) -> FcfResult {
    let capex = s.capex_ppe.map(|v| v.abs());
    let fcf_val = s.operating_cash_flow.zip(capex).map(|(ocf, cap)| ocf - cap);

    let (shares, shares_source) = if s.weighted_average_shares.is_some_and(|v| v > 0.0) {
        let src = if s.weighted_average_shares_source.is_empty() {
            "財報揭露股數".to_string()
        } else {
            s.weighted_average_shares_source.clone()
        };
        (s.weighted_average_shares, Some(src))
    } else if let Some(est) = estimate_from_ni_eps(s) {
        (Some(est), Some("NetIncome/EPS 推估".to_string()))
    } else {
        (None, None)
    };

    let fcf_per_share = match (fcf_val, shares) {
        // amounts in thousands → ×1000 to get TWD, then ÷ shares
        (Some(f), Some(sh)) if sh > 0.0 => Some(f * 1000.0 / sh),
        _ => None,
    };

    FcfResult { capex, fcf: fcf_val, shares, shares_source, fcf_per_share }
}

fn estimate_from_ni_eps(s: &FinancialStatement) -> Option<f64> {
    let est = s.net_income? * 1000.0 / s.eps?;
    if est > 0.0 { Some(est) } else { None }
}

pub fn roe(s: &FinancialStatement) -> Option<f64> {
    pct_ratio(s.net_income, s.total_equity)
}

pub fn operating_margin(s: &FinancialStatement) -> Option<f64> {
    pct_ratio(s.operating_income, s.operating_revenue)
}

pub fn debt_ratio(s: &FinancialStatement) -> Option<f64> {
    pct_ratio(s.total_liabilities, s.total_assets)
}

fn pct_ratio(a: Option<f64>, b: Option<f64>) -> Option<f64> {
    let b = b?;
    if b <= 0.0 { return None; }
    Some(a? / b * 100.0)
}

/// Flags large year-over-year swings in operating CF, CapEx, or share count.
pub fn cash_flow_review_note(cur: &FinancialStatement, prev: &FinancialStatement) -> Option<String> {
    let mut notes: Vec<&str> = Vec::new();
    if large_jump(prev.operating_cash_flow, cur.operating_cash_flow, 100.0) {
        notes.push("營業現金流年變動超過 100%");
    }
    let pc = prev.capex_ppe.map(|v| v.abs());
    let cc = cur.capex_ppe.map(|v| v.abs());
    if large_jump(pc, cc, 100.0) {
        notes.push("資本支出年變動超過 100%");
    }
    let prev_shares = estimated_shares(prev);
    let cur_shares = estimated_shares(cur);
    if large_jump(prev_shares, cur_shares, 20.0) {
        notes.push("股數年變動超過 20%");
    }
    if notes.is_empty() { None } else { Some(notes.join("；")) }
}

fn large_jump(prev: Option<f64>, cur: Option<f64>, threshold: f64) -> bool {
    match (prev, cur) {
        (Some(p), Some(c)) if p != 0.0 => ((c - p) / p * 100.0).abs() >= threshold,
        _ => false,
    }
}

// ---------------------------------------------------------------------------
// Score engine
// ---------------------------------------------------------------------------

pub fn metric_value(key: MetricKey, s: &FinancialStatement) -> Option<f64> {
    match key {
        MetricKey::Roce => roce(s),
        MetricKey::OperatingMargin => operating_margin(s),
        MetricKey::Roe => roe(s),
        MetricKey::DebtRatio => debt_ratio(s),
        MetricKey::CurrentRatio => current_ratio(s),
    }
}

pub fn piecewise(value: f64, points: &[ThresholdPoint]) -> f64 {
    if points.is_empty() { return 0.0; }
    let mut p: Vec<&ThresholdPoint> = points.iter().collect();
    p.sort_unstable_by(|a, b| a.value.partial_cmp(&b.value).unwrap_or(std::cmp::Ordering::Equal));
    if value <= p[0].value { return p[0].score.clamp(0.0, 100.0); }
    if value >= p[p.len() - 1].value { return p[p.len() - 1].score.clamp(0.0, 100.0); }
    for i in 1..p.len() {
        if value <= p[i].value {
            let low = p[i - 1];
            let high = p[i];
            let ratio = (value - low.value) / (high.value - low.value);
            return (low.score + ratio * (high.score - low.score)).clamp(0.0, 100.0);
        }
    }
    0.0
}

pub fn calculate_score(s: &FinancialStatement, rules: &[ScoreRule]) -> ScoreBreakdown {
    let enabled: Vec<&ScoreRule> = rules.iter().filter(|r| r.is_enabled).collect();
    let total_weight: f64 = enabled.iter().map(|r| r.weight).sum();
    let results: Vec<MetricResult> = enabled.iter().map(|r| {
        let value = metric_value(r.metric_key, s);
        let score = value.map(|v| piecewise(v, &r.points));
        MetricResult { key: r.metric_key, label: metric_label(r.metric_key), value, score, weight: r.weight }
    }).collect();

    let available_weight: f64 = results.iter().filter(|r| r.score.is_some()).map(|r| r.weight).sum();
    let total_score = if available_weight <= 0.0 {
        None
    } else {
        Some(results.iter().filter_map(|r| r.score.map(|sc| sc * r.weight)).sum::<f64>() / available_weight)
    };
    let coverage = if total_weight <= 0.0 { 0.0 } else { available_weight / total_weight * 100.0 };
    ScoreBreakdown { total_score, coverage_percent: coverage, metrics: results }
}

pub fn default_rules() -> Vec<ScoreRule> {
    vec![
        ScoreRule { metric_key: MetricKey::Roce,           is_enabled: true, weight: 40.0,
            points: vec![tp(0.0,0.0), tp(5.0,25.0), tp(10.0,50.0), tp(15.0,75.0), tp(20.0,100.0)] },
        ScoreRule { metric_key: MetricKey::OperatingMargin, is_enabled: true, weight: 20.0,
            points: vec![tp(0.0,0.0), tp(5.0,25.0), tp(10.0,50.0), tp(20.0,75.0), tp(30.0,100.0)] },
        ScoreRule { metric_key: MetricKey::Roe,            is_enabled: true, weight: 15.0,
            points: vec![tp(0.0,0.0), tp(5.0,25.0), tp(10.0,50.0), tp(15.0,75.0), tp(20.0,100.0)] },
        ScoreRule { metric_key: MetricKey::DebtRatio,      is_enabled: true, weight: 15.0,
            points: vec![tp(20.0,100.0), tp(35.0,75.0), tp(50.0,50.0), tp(65.0,25.0), tp(80.0,0.0)] },
        ScoreRule { metric_key: MetricKey::CurrentRatio,   is_enabled: true, weight: 10.0,
            points: vec![tp(50.0,0.0), tp(75.0,25.0), tp(100.0,50.0), tp(150.0,75.0), tp(200.0,100.0)] },
    ]
}

fn tp(value: f64, score: f64) -> ThresholdPoint { ThresholdPoint { value, score } }

pub fn metric_label(key: MetricKey) -> &'static str {
    match key {
        MetricKey::Roce => "ROCE",
        MetricKey::OperatingMargin => "營業利益率",
        MetricKey::Roe => "ROE",
        MetricKey::DebtRatio => "負債比",
        MetricKey::CurrentRatio => "流動比率",
    }
}

pub fn metric_description(key: MetricKey) -> &'static str {
    match key {
        MetricKey::Roce => "資本報酬率：每投入 100 元資本能賺回多少，越高越好（一般 > 15% 合格）",
        MetricKey::OperatingMargin => "營業利潤：每 100 元營收去掉成本後剩多少，越高代表產品有定價能力",
        MetricKey::Roe => "股東權益報酬率：股東投入的錢能賺回多少，越高越好（一般 > 15% 合格）",
        MetricKey::DebtRatio => "負債比例：資產中有多少比例是借來的，越低越安全（一般 < 50% 合格）",
        MetricKey::CurrentRatio => "短期償債能力：流動資產能否支付流動負債，越高越安全（一般 > 100% 合格）",
    }
}

/// Assign size bands (Small / Medium / Large) by capital employed rank within peer group.
pub fn assign_size_bands(mut peers: Vec<(String, f64)>) -> std::collections::HashMap<String, CompanySizeBand> {
    peers.sort_unstable_by(|a, b| a.1.partial_cmp(&b.1).unwrap_or(std::cmp::Ordering::Equal).then(a.0.cmp(&b.0)));
    let n = peers.len();
    peers.into_iter().enumerate()
        .map(|(i, (sno, _))| {
            let bucket = (i * 3 / n.max(1)).min(2);
            (sno, match bucket { 0 => CompanySizeBand::Small, 1 => CompanySizeBand::Medium, _ => CompanySizeBand::Large })
        })
        .collect()
}

pub fn median(mut values: Vec<f64>) -> f64 {
    values.sort_unstable_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
    let n = values.len();
    if n == 0 { return 0.0; }
    if n % 2 == 1 { values[n / 2] } else { (values[n / 2 - 1] + values[n / 2]) / 2.0 }
}
