# Report-SQL & Dashboard Guides

[« Back to Guides](../README.md)

Report-SQL extends ETL-SQL with dedicated declarative statements for creating interactive web dashboards and paginated reports. These guides cover authoring, interactive filtering, row-level security, paginated print layouts, and branding.

---

## Guides in this Section

| Guide | Description |
| :--- | :--- |
| [Authoring Dashboards](authoring-dashboards.md) | Three-tier architecture, pages, containers, layout grid, and complete dashboard examples. |
| [Report Parameters & Filters](report-parameters-and-filters.md) | Connect INPUT variables, relative date expressions (`RELDATE`), slicers, and sliders. |
| [Cascading Slicers](cascading-slicers.md) | Configure dependent parent-child filter hierarchies with atomic parameter updates. |
| [Row-Level Security (RLS)](report-row-level-security.md) | Secure datasets using `@@CURRENT_USER`, `HAS_GROUP()`, and dynamic permission mappings. |
| [Paginated & Print-Ready Reports](paginated-and-print-reports.md) | Multi-page documents, Letter/A4 layouts, page breaks, table splitting, and deferred execution. |
| [Micro-Charts & KPI Cards](micro-charts-and-kpis.md) | Embedded table sparklines, progress bars, and KPI cards. |
| [Custom Theming & Branding](custom-theming-and-branding.md) | Global shell branding, CSS overrides, and custom action buttons. |
| [Report Badges & Trust](report-badges-and-trust.md) | Ownership, stewardship, certification tier, and freshness indicators. |
| [From Named Visuals to Custom Charts](custom-chart-learning-path.md) | Progressive tutorial: rebuild BAR as CUSTOM, add layers and scales one concept at a time, end at a bullet chart no named visual can express. |
| [Vega-Lite to ETL-SQL](vega-lite-to-etl-sql.md) | Convert field/datum/value encodings, geometry, scales, and transforms into native script-first charts. |
| [ggplot2 to ETL-SQL](ggplot2-to-etl-sql.md) | Map aesthetics, layers, positions, facets, intervals, fixed aspect, and visible SQL statistics. |

---

## Tooling & Design

- [Visual Report Builder Guide](../tooling/report-builder.md) — 12-column drag-and-drop WYSIWYG dashboard designer.
- [Report-SQL Reference](../../reference/visuals-reporting/README.md) — Complete keyword and visual type syntax.
