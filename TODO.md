# ETL-SQL Development TODO List

## Bugs
### Report Portal
- [ ] Script Page structure is not generating correctly.  Structure as a lot of . . . . . . . . and is repeated twice
```sql
CREATE PAGE [Trends] AS DASHBOARD (
    LAYOUT (
        STRUCTURE = 'ComboRevenueReturns ComboRevenueReturns WaterfallDelta WaterfallDelta . . . . . . . . / ScatterQtyRev ScatterQtyRev ScatterQtyRev ScatterQtyRev . . . . . . . . / HBarRep HBarRep BoxRevDist BoxRevDist . . . . . . . .',
        MAP (
            'ComboRevenueReturns' = ComboRevenueReturns,
            'WaterfallDelta' = WaterfallDelta,
            'ScatterQtyRev' = ScatterQtyRev,
            'HBarRep' = HBarRep,
            'BoxRevDist' = BoxRevDist
        )
    )
);
```
- [ ] When exiting design it returns to the homepage instead of back to the report we were editing.
- [ ] Lineage link in the left sidebar looks nice but doesn't have any data in it. → Root cause: `PersistAdHocInteractions` defaults to `false` in `src/appsettings.json`. Setting it to `true` enables catalog persistence for ad-hoc portal runs via `TryPersistAdHocLineageAsync()` in `ExecutionController.cs`. Orchestrator jobs always persist regardless of this flag.

### Lineage (Future Work)
- [ ] **Ad-hoc run lineage catalog persistence** — `TryPersistAdHocLineageAsync()` exists in `ExecutionController.cs` and calls `SaveLineageAsync()`, but is gated behind `PersistAdHocInteractions: false` in `appsettings.json` (off by default). Decide whether to flip the default to `true`, or surface it as a portal setting. This is why the sidebar Lineage catalog is empty until a scheduled job runs.
- [ ] **Cross-report downstream impact analysis** — add a portal feature: "which other reports use this table/column?" The `CatalogLineageHistoryDto` already includes `reportId`/`reportName`; needs an aggregate API endpoint + UI in the Dependencies or Catalog view.
- [x] **OpenLineage export** — Completed: OpenLineage JSON serialization added to support Marquez, DataHub, Airflow, and Apache Atlas.
- [x] **Database catalog import** — Completed: Built `ICatalogMetadataProvider` for SQL Server, PostgreSQL, and MySQL to pull comments, nullability, and primary key status dynamically.
- [ ] **Standard tag governance / enforcement** — `StandardTags` HashSet is already defined in `LanguageMetadata.cs` with all 20 standard tags, but zero linting rules use it. Need: (1) a lint rule that warns on unknown tag keys (e.g. `@is_pii` vs `@pii`), (2) case-normalization so `@PII` and `@pii` resolve to the same tag, and (3) a `SensitiveExportLintRule` warning when PII-tagged columns flow to unencrypted targets.
- [x] Report structure button looks OK but I think we can do better. → Fixed: Structure DAG now shows source tables → temp tables → datasets → visuals (with axis labels). Dependencies modal filters RESULTSET noise and shows TransformationKind badges, source columns, and security tag badges.
- [ ] **Unqualified Column Precision** — Fix the static analyzer fallback in `LineageAnalyzer.cs` that maps unqualified join columns to all source tables, resolving it by matching column names against known table schemas.
- [ ] **View & Stored Procedure Transparency** — Update metadata catalog providers to fetch view/procedure definitions and recursively parse them to trace lineage through view queries.
- [ ] **Interactive Column Flows (Portal DAG)** — Implement collapsible column-level sub-nodes in ECharts (`designer.js`) to support column-to-column flow tracing.