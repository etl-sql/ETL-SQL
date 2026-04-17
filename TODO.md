# ETL-SQL Development Roadmap
## TUI on-going issues

## VS Code Extension on-going issues

- [ ] **Pipeline execution tree**  When running loops it should just keep restating the same node multiple times rather than print all the iterations.  That really gums up the view when it prints so much.  

---
## Architecture Documentation Gaps  ** For Claude only**

The following architecture documents are missing. Identified 2026-04-14.

### Lower Priority
- [ ] **Docker / Infrastructure Commands** — `DockerContainerManager` and `USE DOCKER` are referenced in the README but the spawn lifecycle, container polling, and session-teardown cleanup are undocumented.
- [ ] **Window Functions & Advanced Operators** — `ExternalWindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) supports signature-based grouping and disk-spilling for hyper-scale scenarios.

---
### Missing ETL Language Features

These are capabilities common in production ETL tools that are either absent from the language or absent from the documentation (unclear which without deeper code investigation):

- [x] **`PIVOT` / `UNPIVOT`** — Implemented and documented.
- [x] **`CROSS APPLY` / `OUTER APPLY`** — Supported and documented.
- [x] **`EXCEPT` / `INTERSECT`** — Supported and documented.
- [x] **Data quality `ASSERT` statement** — Implemented and documented. Includes support for custom messages and TRY...CATCH integration.
- [ ] **Schema drift detection** — No mechanism to detect when a source schema (column names, types) changes between runs. Common in production ETL as a guard against upstream changes breaking a pipeline silently.

### Missing Reporting Features

These are features common in reporting and BI tools that are absent from the Report-SQL language:

- [ ] **Conditional formatting on TABLE visuals** — Ability to highlight cells based on value (e.g., red if negative, green if above target). Standard in every BI tool. Would require a `FORMATTING (column = condition → color)` clause on TABLE visuals.

- [ ] **GAUGE visual type** — A radial gauge / speedometer for KPI dashboards (e.g., 73% of target). ECharts has native `gauge` support. Very common alongside CARD visuals for executive dashboards.

- [ ] **Funnel chart visual type** — Conversion funnel (e.g., impressions → clicks → purchases). ECharts `funnel` type is built-in. Common in marketing and sales reports.

- [ ] **Report parameter type declarations** — Currently parameters are untyped strings. A `PARAMETER @date AS DATE DEFAULT '2024-01-01'` declaration would let the engine validate input types and let DATEPICKER/SLIDER know their expected format.

- [ ] **Cross-filtering between visuals** — Currently, clicking a chart element only fires explicit ACTIONS (DRILL_DOWN or SET_PARAMETER). A declarative `CROSS_FILTER = true` option that automatically filters all visuals on the same page by the clicked value would reduce boilerplate. Common in Power BI and Tableau.

- [ ] **Waterfall chart visual type** — Shows cumulative effect of sequential positive/negative values. Common in financial reporting (P&L, cash flow). ECharts supports this via bar chart with custom series.
