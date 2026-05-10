# ETL-SQL Development Roadmap
## Up Next
  - [x] **Input variables/Run report button click** SSRS-style deferred execution: report loads with placeholder, user sets parameters, clicks Run to fetch data.

- [ ] **DRILL_DOWN — Polish & Expand** — Assessed against Power BI / Tableau; current implementation is solid drill-through navigation. Phases below improve UX parity.
    - [x] **Phase 1 — Polish existing drill-through** *(shipped)*: Tooltip dismissed on right-click (`hideTip`), `cursor: context-menu` + `⤵` badge affordance on hover, client-side back-navigation stack with floating "← Back" button.
    - [x] **Phase 2 — Multi-key drill parameters** *(shipped)*: `Key = (Region, Year, Quarter)`; `keyColumns: string[]` throughout AST → manifest → frontend.
    - [x] **Phase 4 — Hierarchical DRILL_IN / expand-in-place** *(shipped)*: `DRILL_IN(HIERARCHY = (Year, Quarter, Month))` action type; server-side aggregation per level; breadcrumb trail; `DrillInAsync`/`DrillUpAsync` on `DashboardService`; `/api/drill` endpoint in ReportPlayer + ReportPortal.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).

- [ ] **Publish button in VS Code**  Although we have the wonderful PUBLISH command I'm wondering if we can do a helper button which would bring up a form asking the user what server, what folder, what permission.  This would then generate a CREATE CONNECTION m ON PORTAL... EXECUTE BEGIN PUBLISH ... END;  We could also manage datasets and data caching lifecycles too.  Last 24 hrs, refresh at 7 AM ...  Just some easy button deploy options.
