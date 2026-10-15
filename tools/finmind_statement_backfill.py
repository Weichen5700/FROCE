#!/usr/bin/env python3
"""Backfill annual ROCE + FCF fields from FinMind into the local SQLite DB.

The scanner attempts MOPS one company-year at a time. This tool is a slower but
more durable fallback: one company is fetched for the full year range, then
income, balance-sheet, and cash-flow rows are merged into annual FY statements.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import sqlite3
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DB = ROOT / "RoceTool.Web" / "App_Data" / "roce.db"
DEFAULT_STATE = ROOT / "RoceTool.Web" / "App_Data" / "finmind_backfill_state.json"
FINMIND_URL = "https://api.finmindtrade.com/api/v4/data"
SOURCE_KIND_FINMIND = 4
SOURCE_NAME = "FinMind 合併補充"

MONEY_FIELDS = {
    "OperatingRevenue",
    "OperatingIncome",
    "InterestIncome",
    "NetIncome",
    "CurrentAssets",
    "TotalAssets",
    "CurrentLiabilities",
    "TotalLiabilities",
    "TotalEquity",
    "OperatingCashFlow",
    "CapexPpe",
    "CapexIntangibles",
}

REQUIRED_COMPLETE_FIELDS = [
    "OperatingIncome",
    "TotalAssets",
    "CurrentLiabilities",
    "OperatingCashFlow",
    "CapexPpe",
]

STATEMENT_COLUMNS = [
    "StockNo",
    "FiscalYear",
    "Period",
    "IsAnnual",
    "OperatingRevenue",
    "OperatingIncome",
    "InterestIncome",
    "NetIncome",
    "CurrentAssets",
    "TotalAssets",
    "CurrentLiabilities",
    "TotalLiabilities",
    "TotalEquity",
    "Eps",
    "OperatingCashFlow",
    "CapexPpe",
    "CapexIntangibles",
    "WeightedAverageShares",
    "WeightedAverageSharesSource",
    "SourceKind",
    "SourceName",
    "SourceUrl",
    "IsPreferred",
    "FetchedAt",
]


def parse_args() -> argparse.Namespace:
    current_year = dt.date.today().year
    parser = argparse.ArgumentParser(description="Backfill local ROCE DB from FinMind annual statements.")
    parser.add_argument("--db", type=Path, default=DEFAULT_DB, help="SQLite DB path.")
    parser.add_argument("--state", type=Path, default=DEFAULT_STATE, help="State JSON path for progress.")
    parser.add_argument("--start-year", type=int, default=current_year - 7, help="First fiscal year to backfill.")
    parser.add_argument("--end-year", type=int, default=current_year - 1, help="Last fiscal year to backfill.")
    parser.add_argument("--stock", action="append", help="Limit to one stock. Can be repeated.")
    parser.add_argument("--max-stocks", type=int, help="Maximum pending stocks to process this run.")
    parser.add_argument("--sleep-seconds", type=float, help="Delay after each FinMind request.")
    parser.add_argument("--token", default=os.environ.get("FINMIND_TOKEN"), help="FinMind token. Defaults to FINMIND_TOKEN.")
    parser.add_argument("--retries", type=int, default=3, help="Retries per API request.")
    parser.add_argument("--retry-sleep-seconds", type=float, default=60, help="Delay before retrying API errors.")
    return parser.parse_args()


def connect(db_path: Path) -> sqlite3.Connection:
    conn = sqlite3.connect(db_path, timeout=60)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA busy_timeout=60000;")
    return conn


def decimal_value(value: Any) -> Decimal | None:
    if value is None:
        return None
    try:
        return Decimal(str(value).replace(",", "").strip())
    except (InvalidOperation, AttributeError):
        return None


def db_decimal(value: Decimal | None) -> str | None:
    if value is None:
        return None
    return format(value.normalize(), "f")


def utc_timestamp() -> str:
    return dt.datetime.now(dt.UTC).replace(tzinfo=None).isoformat(timespec="seconds")


def money_to_thousand(value: Decimal | None) -> str | None:
    if value is None:
        return None
    return db_decimal(value / Decimal("1000"))


def stock_source_url(stock_no: str, start_year: int, end_year: int) -> str:
    query = urllib.parse.urlencode(
        {
            "dataset": "FinMind annual fallback",
            "data_id": stock_no,
            "start_date": f"{start_year}-01-01",
            "end_date": f"{end_year}-12-31",
        }
    )
    return f"{FINMIND_URL}?{query}"


def request_dataset(
    dataset: str,
    stock_no: str,
    start_year: int,
    end_year: int,
    token: str | None,
    sleep_seconds: float,
    retries: int,
    retry_sleep_seconds: float,
) -> list[dict[str, Any]]:
    params = {
        "dataset": dataset,
        "data_id": stock_no,
        "start_date": f"{start_year}-01-01",
        "end_date": f"{end_year}-12-31",
    }
    if token:
        params["token"] = token
    url = f"{FINMIND_URL}?{urllib.parse.urlencode(params)}"
    headers = {"User-Agent": "RoceTool/1.0 (+local research tool)"}

    last_error: str | None = None
    for attempt in range(1, retries + 1):
        try:
            request = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(request, timeout=60) as response:
                body = response.read().decode("utf-8")
            payload = json.loads(body)
            if int(payload.get("status", 0)) != 200:
                raise RuntimeError(payload.get("msg") or f"FinMind status {payload.get('status')}")
            return list(payload.get("data") or [])
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, json.JSONDecodeError, RuntimeError) as exc:
            last_error = str(exc)
            if attempt >= retries:
                raise RuntimeError(f"{dataset}: {last_error}") from exc
            time.sleep(retry_sleep_seconds)
        finally:
            if sleep_seconds > 0:
                time.sleep(sleep_seconds)
    raise RuntimeError(f"{dataset}: {last_error or 'unknown error'}")


def annual_rows(rows: list[dict[str, Any]], start_year: int, end_year: int) -> dict[int, dict[str, dict[str, Any]]]:
    result: dict[int, dict[str, dict[str, Any]]] = {}
    for row in rows:
        date_text = str(row.get("date") or "")
        if not date_text.endswith("-12-31"):
            continue
        try:
            year = int(date_text[:4])
        except ValueError:
            continue
        if year < start_year or year > end_year:
            continue
        row_type = str(row.get("type") or "")
        if not row_type:
            continue
        result.setdefault(year, {})[row_type] = row
    return result


def type_value(rows_by_type: dict[str, dict[str, Any]], *types: str) -> Decimal | None:
    for row_type in types:
        row = rows_by_type.get(row_type)
        if row is None:
            continue
        value = decimal_value(row.get("value"))
        if value is not None:
            return value
    return None


def origin_value(rows_by_type: dict[str, dict[str, Any]], includes: tuple[str, ...]) -> Decimal | None:
    for row in rows_by_type.values():
        origin = str(row.get("origin_name") or "")
        if all(part in origin for part in includes):
            value = decimal_value(row.get("value"))
            if value is not None:
                return value
    return None


def existing_by_year(conn: sqlite3.Connection, stock_no: str, start_year: int, end_year: int) -> dict[int, sqlite3.Row]:
    rows = conn.execute(
        """
        SELECT *
        FROM "FinancialStatements"
        WHERE "StockNo" = ?
          AND "FiscalYear" BETWEEN ? AND ?
          AND "IsAnnual" = 1
        """,
        (stock_no, start_year, end_year),
    ).fetchall()
    grouped: dict[int, list[sqlite3.Row]] = {}
    for row in rows:
        grouped.setdefault(int(row["FiscalYear"]), []).append(row)

    def score(row: sqlite3.Row) -> tuple[int, int, int, str]:
        fields = sum(1 for field in REQUIRED_COMPLETE_FIELDS if row[field] is not None)
        source = int(row["SourceKind"] or 0)
        source_priority = {2: 0, SOURCE_KIND_FINMIND: 1, 3: 2, 0: 3, 1: 3}.get(source, 9)
        preferred = int(row["IsPreferred"] or 0)
        return (preferred, fields, -source_priority, str(row["FetchedAt"] or ""))

    return {year: sorted(year_rows, key=score, reverse=True)[0] for year, year_rows in grouped.items()}


def completed_years(conn: sqlite3.Connection, stock_no: str, start_year: int, end_year: int) -> set[int]:
    where_fields = " AND ".join(f'"{field}" IS NOT NULL' for field in REQUIRED_COMPLETE_FIELDS)
    rows = conn.execute(
        f"""
        SELECT DISTINCT "FiscalYear"
        FROM "FinancialStatements"
        WHERE "StockNo" = ?
          AND "FiscalYear" BETWEEN ? AND ?
          AND "IsAnnual" = 1
          AND {where_fields}
        """,
        (stock_no, start_year, end_year),
    ).fetchall()
    return {int(row["FiscalYear"]) for row in rows}


def eligible_stocks(conn: sqlite3.Connection, args: argparse.Namespace) -> list[str]:
    if args.stock:
        return sorted({stock.strip() for stock in args.stock if stock.strip()})
    rows = conn.execute(
        """
        SELECT "StockNo"
        FROM "Stocks"
        WHERE "StatementKind" = 0
          AND "IsSupported" = 1
        ORDER BY "StockNo"
        """
    ).fetchall()
    return [str(row["StockNo"]) for row in rows]


def pick_intangible_capex(cash_rows: dict[str, dict[str, Any]]) -> Decimal | None:
    direct = type_value(
        cash_rows,
        "PurchaseOfIntangibleAssets",
        "PurchaseOfIntangibleAssetsClassifiedAsInvestingActivities",
        "IntangibleAssets",
    )
    if direct is not None:
        return direct
    return origin_value(cash_rows, ("取得", "無形資產"))


def build_statement(
    stock_no: str,
    year: int,
    start_year: int,
    end_year: int,
    income_rows: dict[str, dict[str, Any]],
    balance_rows: dict[str, dict[str, Any]],
    cash_rows: dict[str, dict[str, Any]],
    base: sqlite3.Row | None,
) -> dict[str, Any]:
    values: dict[str, Any] = {column: None for column in STATEMENT_COLUMNS}
    values.update(
        {
            "StockNo": stock_no,
            "FiscalYear": year,
            "Period": "FY",
            "IsAnnual": 1,
            "SourceKind": SOURCE_KIND_FINMIND,
            "SourceName": SOURCE_NAME,
            "SourceUrl": stock_source_url(stock_no, start_year, end_year),
            "IsPreferred": 0,
            "FetchedAt": utc_timestamp(),
            "WeightedAverageSharesSource": "",
        }
    )

    if base is not None:
        for field in STATEMENT_COLUMNS:
            if field in {"StockNo", "FiscalYear", "Period", "IsAnnual", "SourceKind", "SourceName", "SourceUrl", "IsPreferred", "FetchedAt"}:
                continue
            values[field] = base[field]
    if values["WeightedAverageSharesSource"] is None:
        values["WeightedAverageSharesSource"] = ""

    net_income = type_value(income_rows, "IncomeAfterTaxes", "IncomeFromContinuingOperations")
    eps = type_value(income_rows, "EPS")

    finmind_values = {
        "OperatingRevenue": money_to_thousand(type_value(income_rows, "Revenue")),
        "OperatingIncome": money_to_thousand(type_value(income_rows, "OperatingIncome")),
        "InterestIncome": money_to_thousand(type_value(income_rows, "InterestIncome", "InterestRevenue")),
        "NetIncome": money_to_thousand(net_income),
        "CurrentAssets": money_to_thousand(type_value(balance_rows, "CurrentAssets")),
        "TotalAssets": money_to_thousand(type_value(balance_rows, "TotalAssets")),
        "CurrentLiabilities": money_to_thousand(type_value(balance_rows, "CurrentLiabilities")),
        "TotalLiabilities": money_to_thousand(type_value(balance_rows, "Liabilities")),
        "TotalEquity": money_to_thousand(type_value(balance_rows, "Equity")),
        "Eps": db_decimal(eps),
        "OperatingCashFlow": money_to_thousand(
            type_value(
                cash_rows,
                "CashFlowsFromOperatingActivities",
                "NetCashInflowFromOperatingActivities",
                "CashFlowsFromUsedInOperatingActivities",
            )
        ),
        "CapexPpe": money_to_thousand(
            type_value(
                cash_rows,
                "PropertyAndPlantAndEquipment",
                "PurchaseOfPropertyPlantAndEquipment",
                "PurchaseOfPropertyPlantAndEquipmentClassifiedAsInvestingActivities",
            )
        ),
        "CapexIntangibles": money_to_thousand(pick_intangible_capex(cash_rows)),
    }
    for field, value in finmind_values.items():
        if value is not None:
            values[field] = value

    if values["WeightedAverageShares"] is None and net_income is not None and eps not in (None, Decimal("0")):
        shares = net_income / eps
        if shares > 0:
            values["WeightedAverageShares"] = db_decimal(shares)
            values["WeightedAverageSharesSource"] = "FinMind NetIncome/EPS 推估"

    return values


def is_complete(values: dict[str, Any]) -> bool:
    return all(values.get(field) is not None for field in REQUIRED_COMPLETE_FIELDS)


def upsert_statement(conn: sqlite3.Connection, values: dict[str, Any]) -> None:
    existing = conn.execute(
        """
        SELECT "Id"
        FROM "FinancialStatements"
        WHERE "StockNo" = ?
          AND "FiscalYear" = ?
          AND "Period" = ?
          AND "SourceName" = ?
        """,
        (values["StockNo"], values["FiscalYear"], values["Period"], values["SourceName"]),
    ).fetchone()
    if existing is None:
        placeholders = ", ".join("?" for _ in STATEMENT_COLUMNS)
        columns = ", ".join(f'"{column}"' for column in STATEMENT_COLUMNS)
        conn.execute(
            f'INSERT INTO "FinancialStatements" ({columns}) VALUES ({placeholders})',
            [values[column] for column in STATEMENT_COLUMNS],
        )
        return

    assignments = ", ".join(f'"{column}" = ?' for column in STATEMENT_COLUMNS if column != "StockNo")
    update_columns = [column for column in STATEMENT_COLUMNS if column != "StockNo"]
    conn.execute(
        f'UPDATE "FinancialStatements" SET {assignments} WHERE "Id" = ?',
        [values[column] for column in update_columns] + [existing["Id"]],
    )


def mark_scan_success(conn: sqlite3.Connection, stock_no: str, year: int) -> None:
    now = utc_timestamp()
    existing = conn.execute(
        'SELECT "Id" FROM "MarketScanItems" WHERE "StockNo" = ? AND "FiscalYear" = ?',
        (stock_no, year),
    ).fetchone()
    if existing is None:
        conn.execute(
            """
            INSERT INTO "MarketScanItems" ("StockNo", "FiscalYear", "Success", "Message", "AttemptedAt")
            VALUES (?, ?, 1, ?, ?)
            """,
            (stock_no, year, SOURCE_NAME, now),
        )
        return
    conn.execute(
        """
        UPDATE "MarketScanItems"
        SET "Success" = 1,
            "Message" = ?,
            "AttemptedAt" = ?
        WHERE "Id" = ?
        """,
        (SOURCE_NAME, now, existing["Id"]),
    )


def save_state(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def process_stock(conn: sqlite3.Connection, stock_no: str, args: argparse.Namespace) -> tuple[int, int]:
    income = annual_rows(
        request_dataset(
            "TaiwanStockFinancialStatements",
            stock_no,
            args.start_year,
            args.end_year,
            args.token,
            args.sleep_seconds,
            args.retries,
            args.retry_sleep_seconds,
        ),
        args.start_year,
        args.end_year,
    )
    balance = annual_rows(
        request_dataset(
            "TaiwanStockBalanceSheet",
            stock_no,
            args.start_year,
            args.end_year,
            args.token,
            args.sleep_seconds,
            args.retries,
            args.retry_sleep_seconds,
        ),
        args.start_year,
        args.end_year,
    )
    cash = annual_rows(
        request_dataset(
            "TaiwanStockCashFlowsStatement",
            stock_no,
            args.start_year,
            args.end_year,
            args.token,
            args.sleep_seconds,
            args.retries,
            args.retry_sleep_seconds,
        ),
        args.start_year,
        args.end_year,
    )

    bases = existing_by_year(conn, stock_no, args.start_year, args.end_year)
    upserted = 0
    completed = 0
    for year in range(args.end_year, args.start_year - 1, -1):
        statement = build_statement(
            stock_no,
            year,
            args.start_year,
            args.end_year,
            income.get(year, {}),
            balance.get(year, {}),
            cash.get(year, {}),
            bases.get(year),
        )
        if not any(statement.get(field) is not None for field in MONEY_FIELDS | {"Eps", "WeightedAverageShares"}):
            continue
        upsert_statement(conn, statement)
        upserted += 1
        if is_complete(statement):
            mark_scan_success(conn, stock_no, year)
            completed += 1
    conn.commit()
    return upserted, completed


def main() -> int:
    args = parse_args()
    if args.start_year > args.end_year:
        print("--start-year must be <= --end-year", file=sys.stderr)
        return 2
    if args.sleep_seconds is None:
        args.sleep_seconds = 6.2 if args.token else 12.2
    if not args.db.exists():
        print(f"DB not found: {args.db}", file=sys.stderr)
        return 2

    conn = connect(args.db)
    stocks = eligible_stocks(conn, args)
    years = set(range(args.start_year, args.end_year + 1))
    pending: list[str] = []
    for stock_no in stocks:
        if completed_years(conn, stock_no, args.start_year, args.end_year) >= years:
            continue
        pending.append(stock_no)
    if args.max_stocks is not None:
        pending = pending[: max(0, args.max_stocks)]

    state = {
        "started_at": utc_timestamp(),
        "db": str(args.db),
        "start_year": args.start_year,
        "end_year": args.end_year,
        "total_pending_stocks": len(pending),
        "processed_stocks": 0,
        "upserted_years": 0,
        "completed_years": 0,
        "errors": 0,
        "sleep_seconds": args.sleep_seconds,
    }
    save_state(args.state, state)
    print(
        f"FinMind backfill start: {len(pending)} pending stocks, "
        f"{args.start_year}-{args.end_year}, sleep {args.sleep_seconds}s/request"
    )

    for index, stock_no in enumerate(pending, start=1):
        try:
            upserted, completed = process_stock(conn, stock_no, args)
            state["processed_stocks"] = index
            state["upserted_years"] += upserted
            state["completed_years"] += completed
            state["last_stock_no"] = stock_no
            state["updated_at"] = utc_timestamp()
            save_state(args.state, state)
            print(f"[{index}/{len(pending)}] {stock_no}: upserted={upserted}, completed={completed}")
        except Exception as exc:
            conn.rollback()
            state["processed_stocks"] = index
            state["errors"] += 1
            state["last_stock_no"] = stock_no
            state["last_error"] = str(exc)
            state["updated_at"] = utc_timestamp()
            save_state(args.state, state)
            print(f"[{index}/{len(pending)}] {stock_no}: ERROR {exc}", file=sys.stderr)

    state["finished_at"] = utc_timestamp()
    save_state(args.state, state)
    print(
        "FinMind backfill finished: "
        f"processed={state['processed_stocks']}, "
        f"upserted_years={state['upserted_years']}, "
        f"completed_years={state['completed_years']}, "
        f"errors={state['errors']}"
    )
    return 0 if state["errors"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
