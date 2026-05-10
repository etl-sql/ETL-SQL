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
    - [ ] **Issues** Using: C:\Users\chuck\scratch\ETL-SQL\samples\kitchen_sinks\report_kitchen_sink.rptsql
        - [ ] I'll refer to the chart being clicked on as the parent, and the charts reacting to the click as the child (could be multiple charts but I'll use the singular word.)  When clicking the parent it often takes two clicks to work.  I suspect this is a time for operation to complete and not a two clicks needed issue.  Can you verify?  How can we make it faster and tell the user we are working on it?
        - [ ] Chart colors are not respected on parent or child.  When clicking the parent all bars turn blue.  They should remain the same color the clicked or ctrl+clicked bars should remain their original color whereas the non-click bars should turn 30% opaque.  Likewise the child chart turns blue rather than staying the same colors with the selected portion of the chart staying the same color and the ghosted part staying the same color just having 30% opaque.
        - [ ] Second click on the parent bar should remove that from the selection.  If its the only bar selected then all charts return to normal.  If its a ctrl+click situation the charts adjust to what is left.
        - [ ] Changing SLICERs (or any other filtering components) should cause this effect to reset.  Currently it does nothing.

  - [ ] **Input variables/Run report button click** The Run report button click does not work.  Using this report as an example: C:\Users\chuck\scratch\ETL-SQL\tests\inputs_deferred_run.rptsql.  My expectations are the report loads showing the parameters and the run button.  The user then sets the parameters or takes the defaults and clicks the run button.  At that time the report should run and get the data and then display the table.  This needs some work.

  - [x] **Shared Dataset where is the portal menus?**  We just added shared datasets and I was expecting in the portal to have a section in admin dedicated to view the current shared datasets, applying permissions, etc.  What am I missing?  Documentation states they are experimental when they have been fully implemented. C:\Users\chuck\scratch\ETL-SQL\Docs\ReportPortal_Administrators_Guide.md

- [ ] **TUI hardening and long-script ergonomics**
    - [x] Add boundary regression coverage for multi-cursor navigation at the top/bottom of the buffer.
    - [x] Add focused long-buffer scenarios for navigation, search, and output scrolling.
    - [ ] Improve diagnostics navigation so parser/linter messages can jump back to script locations.
    - [ ] Review whether folding belongs in the TUI now or should wait until diagnostics/navigation are stronger.
    - [ ] Add scripted scenarios for long-running sessions and large result/message output.
    - [ ] Keep VS Code/reporting host consistency under Priority 2 and host-boundary cleanup under Priority 6.

- [x] **Reporting language ergonomics and documentation**
    - [x] Canonicalize Report-SQL syntax in docs, help, and samples around parser-backed forms: `CREATE PAGE ... AS LAYOUT`, `SOURCE = ...`, `STYLE = Name`, and `STYLE (...)`.
    - [x] Add parser smoke coverage for report help SQL examples so hover/help text cannot drift from runnable syntax.
    - [x] Add linter smoke coverage for report help examples once the parser-backed help guardrail is stable.
    - [x] Review confusing compatibility forms (`CREATE PAGE ... AS (...)`, `CREATE DATASET #name`) and decide whether to keep, warn, or document as legacy.
    - [x] Give each major report doc a clear purpose: guide for people, grammar for exact syntax, help resources for editor hovers, samples for runnable workflows.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).
