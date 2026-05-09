# ETL-SQL Development Roadmap
## Up Next
- [ ] **Cross highlight/filtering (Power BI Parity)** 
    Follow Power BI interactions: [End-user interactions](https://learn.microsoft.com/en-us/power-bi/explore-reports/end-user-interactions)
    - [x] **Phase 1: Basic Interaction Test & Feedback**
        - [x] **Test Report**: `tests/interaction_p1_feedback.rptsql` (Two simple Bar charts).
        - [x] Update `report-runtime.js` to use `chart.dispatchAction({ type: 'highlight' })` for immediate feedback.
        - [x] Support multi-select via `Ctrl+Click`.
        - [x] **CLEAR_FILTERS Action**: Implement a new action type that can be bound to any visual or button: `ACTIONS (ON_CLICK = CLEAR_FILTERS)`. This will be the primary way users reset visual filters.
    - [x] **Phase 2: "Ghost" Chart Rendering (C#)**
        - [x] **Test Report**: `tests/interaction_p2_ghosting.rptsql` (Bar, HBar, Pie).
        - [x] Update `CartesianRenderer.cs` to render "Selection" + "Remainder" stacked series.
        - [x] Remainder series opacity: 20-30%.
        - [x] **Selection-Aware Tooltips**: Combined tooltips showing "X of Y" data.
        - [x] **Alignment Strategy**: Universe defines buckets; Selection is mapped onto them.
    - [x] **Phase 3: Slicer & Global Filter Coordination**
        - [x] **Test Report**: `tests/interaction_p3_slicers.rptsql` (Slicer + Multiple Charts).
        - [x] Slicer changes must reset any visual-level highlights.
        - [x] **Legend Highlighting**: Implement click listeners on Chart Legends to trigger cross-visual actions.
    - [x] **Phase 4: Advanced Support**
        - [x] **Test Report**: `tests/interaction_p4_advanced.rptsql` (Line, Scatter, Combo).
        - [x] Support highlighting in LINE (ghost other lines) and SCATTER (dim non-matching points).
        - [x] Support "Ghosting" in PIE and DONUT (dimming non-selected slices).
    - [ ] **Issues**
        - [ ] I'll refer to the chart being clicked on as the parent, and the charts reacting to the click as the child (could be multiple charts but I'll use the singular word.)  When clicking the parent it often takes two clicks to work.  I suspect this is a time for operation to complete and not a two clicks needed issue.  Can you verify?  How can we make it faster and tell the user we are working on it?
        - [ ] Chart colors are not respected on parent or child.  When clicking the parent all bars turn blue.  They should remain the same color the clicked or ctrl+clicked bars should remain their original color whereas the non-click bars should turn 30% opaque.  Likewise the child chart turns blue rather than staying the same colors with the selected portion of the chart staying the same color and the ghosted part staying the same color just having 30% opaque.
        - [ ] Second click on the parent bar should remove that from the selection.  If its the only bar selected then all charts return to normal.  If its a ctrl+click situation the charts adjust to what is left.
        - [ ] Changing SLICERs (or any other filtering components) should cause this effect to reset.  Currently it does nothing.

  - [ ] **Input variables/Run report button click** The Run report button click does not work.  Using this report as an example: C:\Users\chuck\scratch\ETL-SQL\tests\inputs_deferred_run.rptsql.  My expectations are the report loads showing the parameters and the run button.  The user then sets the parameters or takes the defaults and clicks the run button.  At that time the report should run and get the data and then display the table.  This needs some work.

- [ ] **TUI hardening and long-script ergonomics**
    - [ ] Review long-script navigation, search, folding, diagnostics, and output handling in the TUI.
    - [ ] Add focused tests or scripted scenarios for large scripts and long-running sessions.
    - [ ] Keep VS Code/reporting host consistency under Priority 2 and host-boundary cleanup under Priority 6.

- [ ] **Reporting language ergonomics and documentation**
    - [ ] Review Report-SQL syntax for consistency with the rest of ETL-SQL and simplify confusing forms where possible.
    - [ ] Keep help, hover text, and documentation aligned with parser behavior and runnable examples.
    - [ ] Give each major doc a clear purpose and make the documentation both people-friendly and agent-friendly.


- [ ] **Lineage & Data Governance — Full Feature Set** *(priority — core selling feature)* — See `Docs/Strategy/Lineage_Strategy.md` for the complete design. Reference documentation for standard tags and usage: `Docs/Reference/Lineage.md`.
    - **Phase 1 — Standard Tag Library & Reference Docs** ✓: Define the 20 standard tags (`@pii`, `@phi`, `@pci`, `@sensitive`, `@classification`, `@encrypted_at_rest`, `@owner`, `@domain`, `@steward`, `@contact`, `@freshness`, `@sla`, `@quality`, `@nullable`, `@d`, `@example`, `@unit`, `@format`, `@source_system`, `@source_table`, `@load_pattern`). Created `Docs/Reference/Lineage.md`. Rewrote `Help/Operations/LINEAGE.md`. Added `LanguageMetadata.StandardTags` set. Added `@`-prefix completions with docs to `LanguageService`. @pii: true-wins inheritance implemented.
    - **Phase 2 — Transformation Recording** ✓: `TransformationKind` enum (PassThrough, Cast, FunctionCall, CaseExpression, Arithmetic, StringOperation, Aggregation, WindowFunction, Conditional, Literal, Subquery, Unknown). `TransformationExpression` (raw SQL text) and `FunctionsApplied` (list) added to `LineageEntry`. `LineageAnalyzer.ClassifyExpression()` + `CollectFunctions()` wired into SELECT, UPDATE COLUMN, MERGE UPDATE, MERGE INSERT paths. `LineageGraphRenderer` shows transformation annotations (cycle detection via visited-set replaces hardcoded depth-20). `LineageDataSource` exposes `TransformationKind`, `TransformationExpression`, `FunctionsApplied` columns. 10 new tests.
    - **Phase 3 — Tag Governance & Query Ergonomics** ✓: `LINEAGE_TAGS` virtual table — flat `TargetTable, TargetColumn, Operation, TagName, TagValue, Scope, Line, SourceFile` rows, no more JSON_VALUE gymnastics. `HAS_TAG(table, column, tag_name [, value])` predicate function. Cycle detection via visited-set (done in Phase 2). FOREACH/FOR loop lineage — `FOREACH_LOOP` entry records row variable and source tables; `FOR_LOOP` entry records counter variable. `HAS_TAG`, `GET_TAGS`, `GET_TAG_VALUE` added to `LanguageMetadata.Functions` for intellisense. `LINEAGE_TAGS` keyword added. 7 new tests.
    - **Phase 4 — Report Lineage** ✓: `LineageAnalyzer` handles `CreateVisualStatement` (→ `report:Name`) and `CreateDatasetStatement` (→ `dataset:#name`). `LineageGraphRenderer` shows `[Visual: ...]`/`[Dataset: ...]` headers and uses distinct Mermaid shapes (rounded for visuals, cylinder for datasets). End-to-end chain `SourceDB.table → #temp → dataset:#sales → report:SalesChart` verified. 7 new tests.
    - **Phase 5 — OpenLineage Export** *(major differentiator — enables DataHub, Airflow, Collibra, Alation interop)*: `LINEAGE EXPORT AS OPENLINEAGE TO 'file.jsonl'` syntax. Auto-export on run via `appsettings.json → Lineage:OpenLineageFile` and `Lineage:OpenLineageEndpoint`. Full `columnLineage` facet with transformation descriptions from Phase 2. Validates against published OpenLineage JSON Schema.
    - **Phase 6 — Database Catalog Metadata Import**: New `ICatalogMetadataProvider` interface on connectors. Lazy catalog query on first table reference (opt-in via `appsettings.json → Lineage:ImportCatalogMetadata`). SQL Server: `sys.extended_properties` + `sys.columns`. Postgres: `pg_catalog.obj_description()`. MySQL: `INFORMATION_SCHEMA.COLUMNS.COLUMN_COMMENT`. Snowflake: `INFORMATION_SCHEMA.COLUMNS`. Imported tags prefixed `@db_` (e.g. `@db_description`, `@db_type`, `@db_is_pk`).

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).
