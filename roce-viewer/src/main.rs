mod api;
mod calc;
mod db;
mod fetch;
mod models;

use anyhow::{anyhow, Result};
use axum::{
    routing::{delete, get, post, put},
    Router,
};
use std::{path::PathBuf, sync::Arc};
use tokio::sync::Mutex;
use tower_http::cors::{Any, CorsLayer};

// ---------------------------------------------------------------------------
// DB path discovery
// ---------------------------------------------------------------------------

fn find_db() -> Result<PathBuf> {
    let candidates: Vec<PathBuf> = {
        let exe_dir = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(PathBuf::from));

        let cwd = std::env::current_dir().unwrap_or_default();

        let mut paths = Vec::new();

        // Priority 1: explicit env var
        if let Ok(p) = std::env::var("ROCE_DB_PATH") {
            paths.push(PathBuf::from(p));
        }

        for base in [Some(cwd.clone()), exe_dir.clone()].into_iter().flatten() {
            paths.push(base.join("roce.db"));
            paths.push(base.join("App_Data").join("roce.db"));
            paths.push(base.join("RoceTool.Web").join("App_Data").join("roce.db"));
        }

        // Walk up from cwd (2 levels) and exe_dir (4 levels)
        for (start, levels) in [(cwd.clone(), 3usize), (exe_dir.clone().unwrap_or_default(), 5)] {
            let mut cur = start;
            for _ in 0..levels {
                cur = match cur.parent() { Some(p) => p.to_path_buf(), None => break };
                paths.push(cur.join("RoceTool.Web").join("App_Data").join("roce.db"));
                paths.push(cur.join("App_Data").join("roce.db"));
            }
        }

        paths
    };

    for p in &candidates {
        if p.exists() {
            return Ok(p.clone());
        }
    }

    Err(anyhow!(
        "找不到 roce.db。嘗試路徑：\n{}",
        candidates.iter().map(|p| format!("  {}", p.display())).collect::<Vec<_>>().join("\n")
    ))
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

#[tokio::main]
async fn main() {
    let db_path = match find_db() {
        Ok(p) => {
            eprintln!("[roce-viewer] 資料庫：{}", p.display());
            p
        }
        Err(e) => {
            eprintln!("[roce-viewer] 錯誤：{e}");
            eprintln!("請將 exe 放在 RoceTool.Web 同層目錄，或設定環境變數 ROCE_DB_PATH=<路徑>。");
            std::process::exit(1);
        }
    };

    let conn = match db::open(&db_path) {
        Ok(c) => c,
        Err(e) => { eprintln!("[roce-viewer] 無法開啟資料庫：{e}"); std::process::exit(1); }
    };

    // Ensure auxiliary tables exist and load persisted score rules
    let _ = db::ensure_scan_tables(&conn);
    let _ = db::ensure_score_template_table(&conn);
    let score_rules = db::load_score_rules(&conn);

    let client = reqwest::Client::builder()
        .user_agent("RoceViewer/1.0")
        .timeout(std::time::Duration::from_secs(30))
        .build()
        .expect("reqwest client");

    let state = api::AppState {
        db: Arc::new(Mutex::new(conn)),
        client: Arc::new(client),
        score_rules: Arc::new(Mutex::new(score_rules)),
    };

    let cors = CorsLayer::new()
        .allow_origin(Any)
        .allow_methods(Any)
        .allow_headers(Any);

    let app = Router::new()
        .route("/", get(api::serve_index))
        .route("/api/stocks", get(api::search_stocks))
        .route("/api/stocks/:stockNo/analysis", get(api::get_analysis))
        .route("/api/stocks/:stockNo/focus-metrics", get(api::get_focus_metrics))
        .route("/api/stocks/:stockNo/watchlist", post(api::add_to_watchlist))
        .route("/api/stocks/:stockNo/watchlist", delete(api::remove_from_watchlist))
        .route("/api/stocks/:stockNo/refresh", post(api::refresh_stock))
        .route("/api/stocks/:stockNo/prices/refresh", post(api::refresh_prices))
        .route("/api/watchlist", get(api::get_watchlist))
        .route("/api/screen", get(api::screen))
        .route("/api/refresh/catalog", post(api::refresh_catalog))
        .route("/api/score-template", get(api::get_score_template))
        .route("/api/score-template", put(api::put_score_template))
        .route("/api/score-template/reset", post(api::reset_score_template))
        .route("/api/market-scan/status", get(api::market_scan_status))
        .route("/api/market-scan/start", post(api::market_scan_start))
        .route("/api/market-scan/pause", post(api::market_scan_pause))
        .layer(cors)
        .with_state(state);

    // Use tokio's async listener so we don't need set_nonblocking()
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0")
        .await
        .expect("bind");
    let addr = listener.local_addr().expect("local_addr");
    let url = format!("http://{addr}");
    eprintln!("[roce-viewer] 啟動於 {url}");

    // Open browser after a short delay so the server starts first
    let url_clone = url.clone();
    tokio::spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(500)).await;
        if let Err(e) = webbrowser::open(&url_clone) {
            eprintln!("[roce-viewer] 無法自動開啟瀏覽器：{e}  請手動開啟 {url_clone}");
        }
    });

    axum::serve(listener, app).await.expect("server");
}
