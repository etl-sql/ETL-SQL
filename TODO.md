# ETL-SQL Development Roadmap
## Up Next
  - [x] **Input variables/Run report button click** SSRS-style deferred execution: report loads with placeholder, user sets parameters, clicks Run to fetch data.

- [ ] **DRILL_DOWN — Polish & Expand** — Assessed against Power BI / Tableau; current implementation is solid drill-through navigation. Phases below improve UX parity.
    - [x] **Phase 1 — Polish existing drill-through** *(shipped)*: Tooltip dismissed on right-click (`hideTip`), `cursor: context-menu` + `⤵` badge affordance on hover, client-side back-navigation stack with floating "← Back" button.
    - [ ] **Phase 2 — Multi-key drill parameters**: Extend syntax to `Key = (Region, Year, Quarter)`; `keyColumn` → `keyColumns: string[]` in manifest and frontend; destination receives full context.
    - [ ] **Phase 3 — Left-click direct navigation**: When `ON_CLICK` has exactly one DRILL_DOWN and no competing cross-filter action, skip the context menu and navigate immediately.
    - [ ] **Phase 4 — Hierarchical DRILL_IN / expand-in-place** *(large, strategic)*: New `DRILL_IN` action type with `HIERARCHY` + `LEVEL`; breadcrumb trail + up/down arrows; server tracks drill depth per visual per session.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).
