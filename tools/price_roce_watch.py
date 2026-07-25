#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import date
import json
import sqlite3
import ssl
import urllib.request
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CONFIG_PATH = ROOT / "price-roce-alerts.json"
DB_PATH = ROOT / "RoceTool.Web" / "App_Data" / "roce.db"

TWSE_DAILY_URL = "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL"
TPEX_DAILY_URL = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes"

DEFAULT_CONFIG: dict[str, Any] = {
    "minRocePercent": 20,
    "minDrawdownPercent": 20,
    "highReferenceMode": "yearToDate",
    "stockNos": [],
    "useWatchlistWhenStockNosEmpty": True,
    "fallbackStockNos": ["2330", "2454", "2308", "2317", "6488"],
    "statePath": "RoceTool.Web/App_Data/price-alert-state.json",
}


@dataclass(frozen=True)
class PriceQuote:
    stock_no: str
    trade_date: str
    close: Decimal
    high: Decimal | None
    low: Decimal | None
    source: str


@dataclass(frozen=True)
class StockFundamental:
    stock_no: str
    name: str
    market: int
    fiscal_year: int
    roce: Decimal
    pe_ratio: Decimal | None


def main() -> int:
    parser = argparse.ArgumentParser(description="Check ROCE-qualified stocks for price drawdown alerts.")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG_PATH)
    parser.add_argument("--no-write", action="store_true", help="Do not update the tracked-high state file.")
    parser.add_argument("--json", action="store_true", help="Print machine-readable JSON.")
    args = parser.parse_args()

    config = load_config(args.config)
    min_roce = to_decimal(config["minRocePercent"]) or Decimal("20")
    min_drawdown = to_decimal(config["minDrawdownPercent"]) or Decimal("20")
    high_reference_mode = str(config.get("highReferenceMode", "yearToDate")).strip()
    state_path = resolve_path(str(config["statePath"]))

    stock_nos = resolve_stock_nos(config)
    if not stock_nos:
        return finish({"alerts": [], "warnings": ["沒有可檢查的股票清單。"]}, args.json)

    fundamentals = load_fundamentals(stock_nos)
    quotes = fetch_quotes() if high_reference_mode == "trackedFromMonitor" else {}
    state = load_state(state_path)

    alerts: list[dict[str, Any]] = []
    tracked: list[dict[str, Any]] = []
    warnings: list[str] = []

    for stock_no in stock_nos:
        fundamental = fundamentals.get(stock_no)
        if fundamental is None:
            warnings.append(f"{stock_no}: 尚無完整年度財報，無法計算 ROCE。")
            continue
        if fundamental.roce < min_roce:
            tracked.append({
                "stockNo": stock_no,
                "name": fundamental.name,
                "status": "roce_below_threshold",
                "rocePercent": fmt(fundamental.roce),
            })
            continue

        if high_reference_mode == "trackedFromMonitor":
            quote = quotes.get(stock_no)
            high_reference_label = "監控開始後高點"
        else:
            quote = fetch_year_to_date_quote(fundamental)
            high_reference_label = "今年以來高點"
        if quote is None:
            warnings.append(f"{stock_no} {fundamental.name}: 官方每日行情尚無收盤價。")
            continue

        stock_state = state.setdefault("stocks", {}).setdefault(stock_no, {})
        if high_reference_mode == "trackedFromMonitor":
            previous_high = to_decimal(stock_state.get("trackedHigh"))
            quote_high = quote.high or quote.close
            tracked_high = max(x for x in [previous_high, quote_high, quote.close] if x is not None)
        else:
            tracked_high = quote.high or quote.close
        drawdown = (tracked_high - quote.close) / tracked_high * Decimal("100") if tracked_high > 0 else Decimal("0")
        is_alert = drawdown >= min_drawdown
        already_alerted = (
            stock_state.get("lastAlertTradeDate") == quote.trade_date
            and stock_state.get("lastAlertHighReference") == high_reference_label
        )

        row = {
            "stockNo": stock_no,
            "name": fundamental.name,
            "tradeDate": quote.trade_date,
            "fiscalYear": fundamental.fiscal_year,
            "rocePercent": fmt(fundamental.roce),
            "peRatio": fmt(fundamental.pe_ratio),
            "close": fmt(quote.close),
            "trackedHigh": fmt(tracked_high),
            "highReference": high_reference_label,
            "drawdownPercent": fmt(drawdown),
            "priceSource": quote.source,
            "freshAlert": is_alert and not already_alerted,
        }
        tracked.append(row)
        if is_alert:
            alerts.append(row)
            if not already_alerted:
                stock_state["lastAlertTradeDate"] = quote.trade_date
                stock_state["lastAlertHighReference"] = high_reference_label

        stock_state["name"] = fundamental.name
        stock_state["trackedHigh"] = fmt(tracked_high)
        stock_state["highReference"] = high_reference_label
        stock_state["lastClose"] = fmt(quote.close)
        stock_state["lastTradeDate"] = quote.trade_date
        stock_state["lastDrawdownPercent"] = fmt(drawdown)

    if not args.no_write:
        state_path.parent.mkdir(parents=True, exist_ok=True)
        state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    result = {
        "thresholds": {
            "minRocePercent": fmt(min_roce),
            "minDrawdownPercent": fmt(min_drawdown),
            "highReferenceMode": high_reference_mode,
        },
        "alerts": alerts,
        "tracked": tracked,
        "warnings": warnings,
        "statePath": str(state_path),
    }
    return finish(result, args.json)


def load_config(path: Path) -> dict[str, Any]:
    config = dict(DEFAULT_CONFIG)
    if path.exists():
        config.update(json.loads(path.read_text(encoding="utf-8")))
    return config


def resolve_path(value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else ROOT / path


def resolve_stock_nos(config: dict[str, Any]) -> list[str]:
    explicit = normalize_codes(config.get("stockNos", []))
    if explicit:
        return explicit

    if config.get("useWatchlistWhenStockNosEmpty", True):
        watchlist = load_watchlist_codes()
        if watchlist:
            return watchlist

    return normalize_codes(config.get("fallbackStockNos", []))


def load_watchlist_codes() -> list[str]:
    if not DB_PATH.exists():
        return []
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        rows = conn.execute(
            'SELECT "StockNo" FROM "Watchlist" ORDER BY "SortOrder", "AddedAt"'
        ).fetchall()
    return [str(row["StockNo"]) for row in rows]


def load_fundamentals(stock_nos: list[str]) -> dict[str, StockFundamental]:
    if not DB_PATH.exists():
        raise SystemExit(f"找不到資料庫：{DB_PATH}")

    placeholders = ",".join("?" for _ in stock_nos)
    with sqlite3.connect(DB_PATH) as conn:
        conn.row_factory = sqlite3.Row
        stocks = {
            row["StockNo"]: row
            for row in conn.execute(
                f'SELECT "StockNo", "Name", "Market", "PeRatio" FROM "Stocks" WHERE "StockNo" IN ({placeholders})',
                stock_nos,
            )
        }
        rows = conn.execute(
            f"""
            SELECT *
            FROM "FinancialStatements"
            WHERE "IsAnnual" = 1 AND "StockNo" IN ({placeholders})
            ORDER BY "StockNo", "FiscalYear" DESC, "FetchedAt" DESC
            """,
            stock_nos,
        ).fetchall()

    by_stock_year: dict[tuple[str, int], list[sqlite3.Row]] = {}
    for row in rows:
        by_stock_year.setdefault((row["StockNo"], int(row["FiscalYear"])), []).append(row)

    selected: dict[str, sqlite3.Row] = {}
    for (stock_no, fiscal_year), group in by_stock_year.items():
        best = sorted(group, key=statement_priority)[0]
        current = selected.get(stock_no)
        if current is None or fiscal_year > int(current["FiscalYear"]):
            selected[stock_no] = best

    fundamentals: dict[str, StockFundamental] = {}
    for stock_no, statement in selected.items():
        roce = calculate_roce(statement)
        if roce is None:
            continue
        stock = stocks.get(stock_no)
        fundamentals[stock_no] = StockFundamental(
            stock_no=stock_no,
            name=str(stock["Name"] if stock else ""),
            market=int(stock["Market"] if stock else 0),
            fiscal_year=int(statement["FiscalYear"]),
            roce=roce,
            pe_ratio=to_decimal(stock["PeRatio"] if stock else None),
        )
    return fundamentals


def statement_priority(row: sqlite3.Row) -> tuple[int, int, str]:
    source_kind = int(row["SourceKind"] or 0)
    source_priority = 0 if source_kind == 2 else 1 if source_kind == 3 else 2
    preferred = 0 if bool(row["IsPreferred"]) else 1
    fetched_at = str(row["FetchedAt"] or "")
    return preferred, source_priority, fetched_at


def calculate_roce(row: sqlite3.Row) -> Decimal | None:
    operating_income = to_decimal(row["OperatingIncome"])
    total_assets = to_decimal(row["TotalAssets"])
    current_liabilities = to_decimal(row["CurrentLiabilities"])
    if operating_income is None or total_assets is None or current_liabilities is None:
        return None
    capital_employed = total_assets - current_liabilities
    if capital_employed <= 0:
        return None
    return operating_income / capital_employed * Decimal("100")


def fetch_quotes() -> dict[str, PriceQuote]:
    quotes: dict[str, PriceQuote] = {}

    for row in fetch_json(TWSE_DAILY_URL):
        close = to_decimal(row.get("ClosingPrice"))
        if close is None or close <= 0:
            continue
        code = str(row.get("Code", "")).strip()
        quotes[code] = PriceQuote(
            stock_no=code,
            trade_date=roc_date(str(row.get("Date", ""))),
            close=close,
            high=to_decimal(row.get("HighestPrice")),
            low=to_decimal(row.get("LowestPrice")),
            source="TWSE STOCK_DAY_ALL",
        )

    for row in fetch_json(TPEX_DAILY_URL):
        close = to_decimal(row.get("Close"))
        if close is None or close <= 0:
            continue
        code = str(row.get("SecuritiesCompanyCode", "")).strip()
        quotes[code] = PriceQuote(
            stock_no=code,
            trade_date=roc_date(str(row.get("Date", ""))),
            close=close,
            high=to_decimal(row.get("High")),
            low=to_decimal(row.get("Low")),
            source="TPEx mainboard quotes",
        )

    return quotes


def fetch_year_to_date_quote(fundamental: StockFundamental) -> PriceQuote | None:
    today = date.today()
    rows: list[PriceQuote] = []
    for month in range(1, today.month + 1):
        month_date = f"{today.year}{month:02d}01"
        if fundamental.market == 1:
            rows.extend(fetch_tpex_month_quotes(fundamental.stock_no, month_date))
        else:
            rows.extend(fetch_twse_month_quotes(fundamental.stock_no, month_date))

    if not rows:
        return None

    latest = sorted(rows, key=lambda row: row.trade_date)[-1]
    ytd_high = max(row.high or row.close for row in rows)
    return PriceQuote(
        stock_no=fundamental.stock_no,
        trade_date=latest.trade_date,
        close=latest.close,
        high=ytd_high,
        low=latest.low,
        source=f"{latest.source} YTD",
    )


def fetch_twse_month_quotes(stock_no: str, month_date: str) -> list[PriceQuote]:
    url = (
        "https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY"
        f"?date={month_date}&stockNo={stock_no}&response=json"
    )
    payload = fetch_json(url)
    if not isinstance(payload, dict) or str(payload.get("stat", "")).upper() != "OK":
        return []

    rows: list[PriceQuote] = []
    for item in payload.get("data", []):
        if len(item) < 7:
            continue
        close = to_decimal(item[6])
        high = to_decimal(item[4])
        if close is None or close <= 0:
            continue
        rows.append(PriceQuote(
            stock_no=stock_no,
            trade_date=roc_slash_date(str(item[0])),
            close=close,
            high=high,
            low=to_decimal(item[5]),
            source="TWSE STOCK_DAY",
        ))
    return rows


def fetch_tpex_month_quotes(stock_no: str, month_date: str) -> list[PriceQuote]:
    url = (
        "https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock"
        f"?code={stock_no}&date={month_date[:4]}/{month_date[4:6]}/01&response=json"
    )
    payload = fetch_json(url)
    if not isinstance(payload, dict) or str(payload.get("stat", "")).lower() != "ok":
        return []

    tables = payload.get("tables", [])
    if not tables:
        return []

    rows: list[PriceQuote] = []
    for item in tables[0].get("data", []):
        if len(item) < 7:
            continue
        close = to_decimal(item[6])
        high = to_decimal(item[4])
        if close is None or close <= 0:
            continue
        rows.append(PriceQuote(
            stock_no=stock_no,
            trade_date=roc_slash_date(str(item[0])),
            close=close,
            high=high,
            low=to_decimal(item[5]),
            source="TPEx tradingStock",
        ))
    return rows


def fetch_json(url: str) -> Any:
    request = urllib.request.Request(url, headers={"User-Agent": "RoceTool/1.0 price alert"})
    context = ssl.create_default_context()
    if hasattr(ssl, "VERIFY_X509_STRICT"):
        context.verify_flags &= ~ssl.VERIFY_X509_STRICT
    with urllib.request.urlopen(request, timeout=45, context=context) as response:
        return json.loads(response.read().decode("utf-8-sig"))


def load_state(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {"stocks": {}}
    return json.loads(path.read_text(encoding="utf-8"))


def normalize_codes(values: Any) -> list[str]:
    if not isinstance(values, list):
        return []
    seen: set[str] = set()
    result: list[str] = []
    for value in values:
        code = str(value).strip()
        if code and code not in seen:
            seen.add(code)
            result.append(code)
    return result


def to_decimal(value: Any) -> Decimal | None:
    if value is None:
        return None
    text = str(value).replace(",", "").strip()
    if text in {"", "----", "---", "--", "NaN"}:
        return None
    try:
        return Decimal(text)
    except InvalidOperation:
        return None


def roc_date(value: str) -> str:
    value = value.strip()
    if len(value) == 7 and value.isdigit():
        year = int(value[:3]) + 1911
        return f"{year:04d}-{value[3:5]}-{value[5:7]}"
    return value


def roc_slash_date(value: str) -> str:
    value = value.strip()
    parts = value.split("/")
    if len(parts) == 3 and all(part.isdigit() for part in parts):
        year = int(parts[0]) + 1911
        return f"{year:04d}-{int(parts[1]):02d}-{int(parts[2]):02d}"
    return value


def fmt(value: Decimal | None) -> str | None:
    if value is None:
        return None
    return format(value.quantize(Decimal("0.01")), "f")


def finish(result: dict[str, Any], as_json: bool) -> int:
    if as_json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    thresholds = result.get("thresholds", {})
    if thresholds:
        high_mode = thresholds.get("highReferenceMode", "yearToDate")
        high_label = "今年以來高點" if high_mode == "yearToDate" else "監控開始後高點"
        print(f"ROCE 門檻：{thresholds['minRocePercent']}%；回落門檻：{thresholds['minDrawdownPercent']}%；高點基準：{high_label}")

    fresh_alerts = [row for row in result.get("alerts", []) if row.get("freshAlert")]
    repeated = [row for row in result.get("alerts", []) if not row.get("freshAlert")]
    if fresh_alerts:
        print("ALERT：符合 ROCE 與價格回落條件，請回到工具檢查基本面與風險。")
        for row in fresh_alerts:
            print(alert_line(row))
    else:
        print("目前沒有新的價格回落提醒。")

    if repeated:
        print("仍符合條件但今天已提醒過：")
        for row in repeated:
            print(alert_line(row))

    warnings = result.get("warnings", [])
    if warnings:
        print("注意：")
        for warning in warnings:
            print(f"- {warning}")
    return 0


def alert_line(row: dict[str, Any]) -> str:
    pe = row["peRatio"] if row["peRatio"] is not None else "—"
    return (
        f"- {row['stockNo']} {row['name']}：ROCE {row['rocePercent']}%，"
        f"收盤 {row['close']}，{row.get('highReference', '追蹤高點')} {row['trackedHigh']}，"
        f"回落 {row['drawdownPercent']}%，本益比 {pe}，"
        f"財報年度 {row['fiscalYear']}，行情日 {row['tradeDate']}"
    )


if __name__ == "__main__":
    raise SystemExit(main())
