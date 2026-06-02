# ETL-SQL Development TODO List

## High-Fidelity PDF Export
- [x] Add `PdfExportMode`, `PdfExportOptions`, and PDF exporter selection while keeping `STATIC` as the default behavior.
- [x] Add parser/AST support for `EXPORT REPORT ... WITH (PDF_MODE = STATIC|AUTO|HOSTED|BROWSER, HOST = '...', BROWSER_PATH = '...')`.
- [x] Document `PDF_MODE` syntax and behavior in `Docs/Syntax_Index.md`, `Docs/Report_SQL_Guide.md`, `Docs/Reference/Grammar.md`, and `src/ETL-SQL.Core/Resources/Help/Keywords/EXPORT.md` after parser support lands.
- [x] Update architecture/user docs for the dual PDF export model (`STATIC` default, optional `HOSTED`/`BROWSER`, `AUTO` fallback) in `Docs/Architecture/Reporting.md` and `Docs/User_Manual.md`.
- [x] Add a shared report runtime export-readiness signal for browser/hosted capture.
- [ ] Implement `HOSTED` PDF export for `report serve` using the existing report runtime.
- [ ] Implement `HOSTED` PDF export for ReportPortal as the current authenticated user.
- [ ] Implement optional installed-browser PDF export using configured Chrome/Edge/Chromium only; do not bundle a browser.
- [ ] Wire `AUTO` mode to try configured high-fidelity export and fall back to `STATIC` with a warning.

