# ETL-SQL.Reporting

`ETL-SQL.Reporting` owns shared report contracts and reporting semantics that must be consistent across ReportBuilder, ReportPlayer, ReportPortal, VS Code, CLI, and TUI hosts.

The first migration passes move serializable report manifest contracts, manifest/visual builders, style/page/dataset builders, theme translation, snapshot persistence, Markdown/SVG/PDF/terminal rendering, and shared ECharts chart rendering semantics here while preserving their existing `ETL_SQL.ReportBuilder` namespace for compatibility. Future passes can rename namespaces and packages once hosts have migrated cleanly.
