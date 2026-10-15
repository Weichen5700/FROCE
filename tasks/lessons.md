# Lessons

## 2026-10-15 research dates and local runtime locks

- Classification: incorrect assumption about data contracts / environment-dependent verification.
- Failure mode: treated a dividend source or approval date as a financial period end, rejected legitimate event observations, and initially required a date for a documented unknown.
- Detection signal: packaged JSON failed the real C# parser despite passing generic JSON parsing; dated dividend observations did not align with FY year-end.
- Prevention rule: validate period-end, source-event, and unknown date semantics separately. Preserve unknowns and document source versions; never fabricate year-end dates to satisfy a schema.
- Tripwire: load the exact packaged source files through the production parser, including a next-year dividend decision and a null date/value observation.
- Environment lesson: Windows holds loaded DLLs. Stop the agent-owned local service before rebuilding shared output; confirm port ownership before restarting. A blocked file copy is not a code compilation defect.

## 2026-10-15 scenario forms and financial navigation

- Classification: missing verification.
- Failure mode: hidden fields and editable inputs emitted duplicate IDs; scenario parameters were preserved between companies but initially omitted from the market-view round trip.
- Detection signal: Playwright strict locator matched two ScenarioYear elements; read-only review found missing route parameters.
- Prevention rule: use unique IDs only for editable controls, and carry user assumptions through every adjacent route and post redirect.
- Tripwire: inspect the rendered input IDs, change all scenario inputs, switch companies, then visit the market view and return with the same values.

## 2026-10-14 financial dividends require visible prices

- Classification: misunderstanding requirements.
- Failure mode: added dividends without displaying the price denominator in the financial comparison, leaving users to enter another page to assess income versus cost.
- Detection signal: user could not find prices when looking for a 5 percent dividend threshold.
- Prevention rule: dividend comparisons must show cash basis, share price, trading date, and target-price formula together; fetched time is not trading date.
- Tripwire: inspect one financial list row and verify cash amount, dated closing price, calculated yield, and 5 percent reference price agree numerically.

## 2026-10-14 native anchors and SPA history

- Classification: missing verification.
- Failure mode: native section anchors raised popstate, and the global SPA handler fetched the same page and reset scrolling to zero.
- Detection signal: URL hash changed while the section remained below the viewport after an actual browser click.
- Prevention rule: distinguish a fragment within the rendered page from navigation to another pathname or query; check final section position and browser back, not only the URL hash.
- Tripwire: click the financial earnings section link, verify its top clears the sticky header after the page settles, then switch companies and use browser back to verify matching content.

## 2026-10-14 finance missing data and navigation

- Classification: missing verification / misunderstanding requirements.
- Failure mode: treated locally missing annual statements as missing source data and placed annual information behind a second navigation action.
- Detection signal: user identified Fubon Financial gaps and needed two levels to compare years.
- Prevention rule: verify an official report for missing flagship companies and show the annual comparison directly on the first company page; retrieval failures must remain distinct from absent reports.
- Tripwire: check Fubon seven-year coverage, dividend fiscal year, consolidated earnings basis, and one-click list-to-annual comparison in a browser.

## 2026-10-14 finance list and detail hierarchy

- Classification: misunderstanding requirements.
- Failure mode: moved seven annual EPS columns to the company list without reading the existing stock detail flow; sorting was implemented as a year selector while company details still lacked annual navigation.
- Detection signal: user explained that the outer table should support sorting and annual reports should appear after entering a company.
- Prevention rule: before redesigning a feature, read both its list and detail routes; define the user's decision at each level, the metric period, and the navigation between them.
- Tripwire: inspect list headers for one EPS comparison period, clickable sort directions, and a company link; inspect the destination for annual history, year selection, missing-year handling, and a return path.

## 2026-07-26 tracked-high requirement mismatch

- Classification: misunderstanding requirements.
- Failure mode: price alert logic initially used "high since monitoring started", while the intended product signal was a meaningful historical high such as year-to-date high.
- Detection signal: user pointed out the tracked high behaved like the current day's movement instead of annual high context.
- Prevention rule: when implementing market-derived alert baselines, explicitly name the reference window in config, output, docs, and tests (`yearToDate`, `trackedFromMonitor`, etc.).
- Tripwire: before finalizing future alert logic, run one sample output and check that the printed reference label and numeric high match the intended window.

## 2026-07-26 backfill required-column default

- Classification: missing verification.
- Failure mode: the FinMind backfill script wrote `NULL` to `FinancialStatements.WeightedAverageSharesSource` when shares could not be estimated, but the SQLite column is `NOT NULL`.
- Detection signal: long-run state recorded `NOT NULL constraint failed: FinancialStatements.WeightedAverageSharesSource` on stock `1235`.
- Prevention rule: before long-running DB backfills, inspect `PRAGMA table_info` or the initializer for non-null columns and set explicit defaults for every inserted row.
- Tripwire: run at least one sample where optional derived fields are absent, not only a sample where every derived field can be calculated.

## 2026-07-26 annual price reference endpoint mismatch

- Classification: integration boundary.
- Failure mode: the new annual-price tooltip rendered correctly, but every reference was missing because the .NET TWSE history URL used an older path while the working Python tool used the `/rwd/zh/afterTrading/STOCK_DAY` path.
- Detection signal: the details page had eight hover elements, but all tooltip values said `尚無官方收盤資料`.
- Prevention rule: when adding a new consumer of an external data source, compare endpoint paths with existing working clients and verify the request URL with a stub handler before relying on live network access.
- Tripwire: assert the history request path in a provider test and inspect one rendered tooltip value, not only the presence of the HTML element.

## 2026-10-13 finance navigation presence

- Classification: misunderstanding requirements.
- Failure mode: finance-related text inside stock search and detail pages was treated as proof that a finance section existed in the main menu.
- Detection signal: the user pointed out that the screenshot's top navigation had no finance item.
- Prevention rule: distinguish a navigation item/page from conditional labels or content blocks, and verify the shared layout's links and matching page route before saying a feature is visible from the menu.
- Tripwire: when asked whether a feature appears in navigation, inspect the layout navigation and confirm a corresponding route/page exists.

## 2026-10-13 PowerShell switch parameter variable collision

- Classification: incorrect assumption about shell behavior.
- Failure mode: a background launcher assigned `Start-Process` output to `$worker` while its switch parameter was named `$Worker`; PowerShell variable names are case-insensitive and rejected assigning a process object to the switch parameter even though the child process had already started.
- Detection signal: launcher reported a Process-to-SwitchParameter conversion error, while a child held the exclusive lock and continued writing progress.
- Prevention rule: use distinct variable names for process handles and switch parameters, even when their capitalization differs; after any launch error, inspect the lock and progress log before retrying.
- Tripwire: invoke the launcher in its default background mode and verify it returns a PID; if it errors, check the exclusive lock and log before attempting another launch.
