# 08_Reporting: Report-SQL Dashboards, Interactions, and Datasets

This directory contains Report-SQL (`.rptsql`) scripts demonstrating interactive dashboard capabilities: filters, slicers, master-detail rows, deferred execution, drill-downs, statistical overlays, and portal dataset integration.

## Key Demonstrations

| Script | Category | Focus |
| :--- | :--- | :--- |
| [`sales_dashboard.rptsql`](sales_dashboard.rptsql) | Dashboard | Complete multi-visual sales dashboard with cards, charts, and table layout. |
| [`daily_sales_report.rptsql`](daily_sales_report.rptsql) | Production Report | Certified reporting layout with stewardship metadata and KPIs. |
| [`kitchen_sink.rptsql`](kitchen_sink.rptsql) | Interactive Controls | Demonstrates `SLICER`, `NUMBERBOX`, `CHECKBOX`, `TEXTBOX`, `RELDATEPICKER`, and `BUTTON`. |
| [`report_views.rptsql`](report_views.rptsql) | Reusable Views | Declaring `CREATE VIEW` queries and reusing them across multiple visuals. |
| [`drill_hierarchy.rptsql`](drill_hierarchy.rptsql) | Drill-Down | Hierarchical navigation from category down to subcategory and item details. |
| [`master_detail_rows.rptsql`](master_detail_rows.rptsql) | Master-Detail | Expanding row-level detail beneath primary summary rows in a table visual. |
| [`forecast_overlay_anomaly_intervals.rptsql`](forecast_overlay_anomaly_intervals.rptsql) | Overlays | Confidence bands, reference lines, and anomaly interval overlays. |
| [`stewardship_scorecard.rptsql`](stewardship_scorecard.rptsql) | Governance | Certified reporting scorecard displaying data classification, quality tags, and ownership. |
| [`datasets/`](datasets/) | Portal Datasets | Subdirectory verifying portal-mode datasets (`CREATE DATASET`, `USE DATASET`). |

## Companion Reporting Directories

- **Single-Visual Reference**: For focused, isolated examples of all 39 individual chart and control types, see [`../visual_gallery/`](../visual_gallery/).
- **Executive Analytics**: For specialized analytical dashboards (Pareto 80/20, Cohort Retention, Sales Funnel, Waterfall), see [`../visual_analytics/`](../visual_analytics/).
- **Multi-Page Layouts**: For paginated report collections, see [`../paginated/`](../paginated/).

## Running a Report

```bash
dotnet run --project src/ETL-SQL.App -- run samples/08_Reporting/sales_dashboard.rptsql
dotnet run --project src/ETL-SQL.App -- run samples/08_Reporting/daily_sales_report.rptsql
```
