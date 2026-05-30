# ETL-SQL Development TODO List

## Bugs
### Report Portal
- [x] Script Page structure had trailing `. . . . . . . .` dots → Fixed: `BuildStructure()` in `DesignerController.cs` now trims the grid to the actual max column used by visuals, not the full 12-column width.
- [x] When exiting design it returns to the homepage → Fixed: `designer.html` `onSave`/`onCancel` now navigate to `/index.html#report-{id}`; `openReportHashView()` in `index.html` handles the `#report-{n}` hash and calls `openReport(id)`.
- [x] Lineage link in the left sidebar looks nice but doesn't have any data in it. → Fixed: flipped `PersistAdHocInteractions` to `true` in `src/appsettings.json`. Ad-hoc portal runs now persist lineage to the catalog.
- [x] Lineage/Dependencies modal too narrow → Added `.modal-xl { width: min(920px, calc(100vw - 40px)) }` to `portal.css`; both `structureModal` and `dependenciesModal` now use `modal-xl`.
- [x] Structure DAG jumbled → Container height 420px → 600px; `LAYER_H` 130 → 160, `NODE_W` 200 → 240, initial zoom 0.9 → 0.65 in `designer.js`; assets synced.

### Lineage (Future Work)
- [x] **Ad-hoc run lineage catalog persistence** — Flipped `PersistAdHocInteractions` to `true` in `appsettings.json`. `TryPersistAdHocLineageAsync()` in `ExecutionController.cs` is now active for all ad-hoc portal runs.
- [x] **Cross-report downstream impact analysis** — Added `GET /api/catalog/lineage/downstream?table={name}` endpoint; Dependencies modal "Downstream Impact" section now lists other reports that consume the same source tables.
- [x] **OpenLineage export** — Completed: OpenLineage JSON serialization added to support Marquez, DataHub, Airflow, and Apache Atlas.
- [x] **Database catalog import** — Completed: Built `ICatalogMetadataProvider` for SQL Server, PostgreSQL, and MySQL to pull comments, nullability, and primary key status dynamically.
- [x] **Standard tag governance / enforcement** — Added `UnknownTagLintRule` in `ETL-SQL.Analysis/Linting/Rules/`; warns when `/* @tag */` key is not in `LanguageMetadata.StandardTags` (auto-discovered, no DI registration needed). Case-normalization was already handled by the OrdinalIgnoreCase Metadata dictionary.
- [x] Report structure button looks OK but I think we can do better. → Fixed: Structure DAG now shows source tables → temp tables → datasets → visuals (with axis labels). Dependencies modal filters RESULTSET noise and shows TransformationKind badges, source columns, and security tag badges.
- [x] **Unqualified Column Precision** — `LineageAnalyzer.cs` fallback now uses `Tracker.GetColumnMetadata()` to narrow fan-out to only source tables that have the column already tracked.
- [x] **View & Stored Procedure Transparency** — Added `IViewDefinitionProvider` interface; implemented in SQL Server (`sys.sql_modules`), PostgreSQL (`pg_get_viewdef`), and MySQL (`INFORMATION_SCHEMA.VIEWS`); `ExpandViewLineageAsync` in `Evaluator.cs` parses view DDL recursively and records `VIEW_EXPAND` lineage entries.
- [x] **Interactive Column Flows (Portal DAG)** — `renderDag()` now supports click-to-expand column sub-nodes; `GetStructure()` enriches table/dataset nodes with `columns` + `colEdges` from `LineageAnalyzer`; column-to-column edges drawn when both parent nodes are expanded. HTML-escaped via `_h()` helper.