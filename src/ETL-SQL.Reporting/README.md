# ETL-SQL.Reporting

`ETL-SQL.Reporting` owns shared report contracts and reporting semantics that must be consistent across ReportBuilder, ReportPlayer, ReportPortal, VS Code, CLI, and TUI hosts.

The report manifest contracts, manifest/visual builders, style/page/dataset builders, theme translation, snapshot persistence, CSV/Markdown/SVG/PDF/terminal rendering, report interaction refresh semantics, and shared ECharts chart rendering semantics live here under the `ETL_SQL.Reporting` namespace. `ETL-SQL.ReportBuilder` remains as the compatibility assembly for the engine-facing export handler.
