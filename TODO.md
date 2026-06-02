# ETL-SQL Development TODO List
## v0.10.0 work
- [ ] **Add lineage** Databases can store lineage in a variety of ways.  For it to flow all the way to a report we need to make it available that the user can import it into a script that falls outside the traditional ways
  - Add Open Lineage import  CREATE LINEAGE FOR TABLE <table> FROM <markdown>.  I'm mocking this after this SHOW LINEAGE FOR #target_table TO 'lineage_report.md';
  - CREATE TAG FOR TABLE <table> [COLUMN <col>]  I'm mocking this after this SHOW TAGS FOR TABLE <table> [COLUMN <col>]
    This allows the user to loop through add add tags to table if they saved them in a non-standard area

- [ ] **Job scheduling verification**  Need to harden jobs.  The few small tests I have ran show they work but we need a full integration.  What happens when it breaks, can we resume, can we email.  Server down, source systems down, etc.

- [ ] **Subscription verification**  I have not done any report subscription testing.  We need to create an SMTP so we can validate the emails come through.  Then we need to run it through the scenarios of where it could fail.  We'll want to try all export types and compare the results to what they should to to what the user got.

- [ ] **Report portal create users**  We need to write out some real scenarios of different user accounts with different security options.  Then I'll log in and test them.  We need to create a script with users, groups, permissions, folders, and reports.  Then we test to see who can see what and make sure they work as they should.

- [ ] **Portal load testing** Need to be able to tell administrators how to size the portal server for number of users.  We need to get a baseline of how well it performs under load with fixed machine specs, find its breaking point, dial it back to an appropriate amount and then use that as the multiplier for an estimated system resource guide.

- [ ] **Orchestrator load testing** Need to be to tell administrators how to size the orchestrator server for number of jobs.  We need to get a baseline of how well "normal" jobs run under load with fixed machine specs, find its breaking point, dial it back to an appropriate amount and then use that as the multiplier for an estimated system resource guide.

- [ ] **Add some fuzzy matching samples**  Our matching joins, and functions haven't really been used.  Thinking we can add a few samples.

- [ ] **Add some cookbook recipes**  Its been a while since we added some recipes to either the regular and reporting cookbooks.  Thinking fuzzy matching, some of the new report types, lineage, tags, orchestrator and portal examples.  We also need a way to check these queries that they work.  I think we have a script that looks through documentation to check them let's make sure it works for cookbook items.

### v0.9.0 code-review follow-ups (deferred from the release gate)

_Performance:_
- [ ] **Chart SSR concurrency (V8 engine pool)**  `EChartsSsrRenderer` serializes every chart render through one process-wide V8 engine behind a single lock, so concurrent PDF/export requests with many charts fully serialize on it. Replace the single shared engine with a small pool (or per-request engine) so chart rendering can parallelize.
- [ ] **XLSX export streaming**  `DatasetViewerService.ExportXlsxAsync` / `DatasetController` buffer the whole workbook in memory (`LoadCachedAsync` → `OrderBy().ToList()` → `Materialize` → `MemoryStream` → `ToArray()`), risking OOM on large datasets; the CSV path already streams to `Response.Body`. Stream the XLSX write to the response and drop the full materialization. (CancellationToken is already wired through `XlsxWriter`.)
- [ ] **Catalog metadata import off the hot path**  With `LINEAGE_IMPORT_CATALOG` on, each distinct source table's first `SELECT … INTO` blocks on ~3 live-DB metadata round-trips in `SelectStatementHandler.EnsureCatalogMetadataImportedAsync`. Per-session deduped, but consider prefetching/batching or moving it off the statement-execution path.

_Cleanup / maintainability:_
- [ ] **PDF/Markdown renderer dedup + TEXT drift (real inconsistency)**  `PdfExporter` and `MarkdownRenderer` duplicate filter parameter resolution, the SSR→static chart fallback, and TEXT/IMAGE content resolution — and they have drifted: PDF resolves TEXT content from `CONTENT/VALUE/DefaultValue/mapping:content` while Markdown only checks `VALUE/DefaultValue`, so a TEXT visual can export non-empty in PDF but blank in Markdown. Extract a shared content/filter resolver and align behavior across renderers.
- [ ] **Connector exception-wrapping dedup**  The three catalog providers (MySql/Postgres/SqlServer) repeat 9 near-identical `try/catch-when(ShouldWrapProviderException) → ConnectorExceptionWrapper.Wrap` blocks plus 3 copies of `ShouldWrapProviderException`. Add a synchronous `ConnectorExceptionWrapper.Run<T>(label, predicate, func)` (mirroring the existing async `WrapAsync`) and collapse the call sites.
- [ ] **XLSX export minor cleanup**  `ExportController.ExportXlsx` computes `SelectExportVisuals` twice per request (empty-check + inside `XlsxExporter.ExportAsync`); and `XlsxExporter.Uniquify` duplicates the name-dedup logic in `XlsxWriter.UniqueSheetName`. Compute the selection once and share a single name-deduplicator.
