# ETL-SQL.Reporting

`ETL-SQL.Reporting` owns shared report contracts and reporting semantics that must be consistent across ReportBuilder, ReportPlayer, ReportPortal, VS Code, CLI, and TUI hosts.

The first migration passes move serializable report manifest contracts, manifest/visual builders, style/page/dataset builders, theme translation, snapshot persistence, CSV/Markdown/SVG/PDF/terminal rendering, report interaction refresh semantics, and shared ECharts chart rendering semantics here under the `ETL_SQL.Reporting` namespace. `ETL-SQL.ReportBuilder` remains as a compatibility assembly for the engine-facing export handler until package/project renaming is planned.
