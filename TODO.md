# ETL-SQL Development Roadmap
## Up Next
- [x] **Report Portal UX modernization**
    Goal: make Report Portal feel like a polished operational analytics workspace: calm, fast to scan, confidence-building for business users, and still efficient for admins.
    - [x] **Phase 1: Visual foundation and shell**
        - [x] Establish a shared design token layer in `portal.css`: background, surface, border, text, muted text, accent, danger/warning/success, focus rings, spacing, and type scale.
        - [x] Modernize the top bar and app shell: clearer brand mark, active nav state, user/account area, consistent page width, and responsive behavior.
        - [x] Replace emoji folder/report affordances with CSS/icon-style primitives or consistent lightweight symbols.
        - [x] Remove inline modal/layout styles from `index.html` and `admin.html` where practical; move reusable modal, form, toolbar, and table classes into `portal.css`.
        - [x] Add visual regression-friendly smoke checks for the portal static pages if an existing browser test lane is available. No existing browser visual lane was found in this pass, so screenshot automation is deferred until a portal browser test lane exists.
    - [x] **Phase 2: Reports library**
        - [x] Redesign the folder/sidebar and report grid so users can quickly understand where they are and what reports are available.
        - [x] Improve report cards: stronger title hierarchy, clearer last-run/stale state, better empty descriptions, consistent thumbnail framing, and scan-friendly metadata.
        - [x] Add a library toolbar pattern for folder title, count, search/filter placeholder, sort affordance, and refresh action.
        - [x] Improve empty/loading/error states so they suggest the next action instead of looking like placeholders.
    - [x] **Phase 3: Report viewer and run workflow**
        - [x] Redesign the viewer header as a compact command bar with report title, freshness/build metadata, and primary actions ordered by user intent.
        - [x] Make stale/running states more legible: persistent progress banner, disabled duplicate actions while running, clear success/failure feedback, and current page preservation.
        - [x] Ensure the embedded report frame gets maximum useful space without nested card chrome.
        - [x] Review export/subscribe/run controls for consistent labels, button hierarchy, and keyboard/focus behavior.
    - [x] **Phase 4: Subscriptions and parameter modals**
        - [x] Unify modal styling and layout across export, subscribe, edit parameters, script browser, and admin dialogs.
        - [x] Make parameter editing easier to scan: grouped labels, required markers, descriptions, relative date quick picks, validation messages, and sticky actions for long forms.
        - [x] Modernize My Subscriptions as an operational table with clear status, schedule, delivery format, parameter summary, and safer destructive actions.
    - [x] **Phase 5: Admin workspace**
        - [x] Rework admin tabs into a denser workspace pattern that can scale beyond seven tabs without wrapping awkwardly.
        - [x] Standardize admin cards, tables, inline forms, action groups, filters, pagination, and selected-item detail panels.
        - [x] Give Shared Datasets the same visual maturity as reports: access level, TTL, lineage/source, permissions, refresh/expiry status, and dataset actions.
        - [x] Reduce repeated inline form code where small helper renderers or CSS utility classes would lower maintenance risk.
    - [x] **Phase 6: Accessibility, responsiveness, and polish**
        - [x] Audit contrast, focus states, keyboard navigation, modal escape behavior, table overflow, and mobile/tablet layouts.
        - [x] Add loading skeletons or compact progress states for slow API calls.
        - [x] Confirm report thumbnails, viewer iframe, modals, and admin tables behave at narrow widths and short heights.
        - [x] Document portal UI conventions in the presentation standards once the first two phases settle.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).

- [ ] **Publish button in VS Code**  Although we have the wonderful PUBLISH command I'm wondering if we can do a helper button which would bring up a form asking the user what server, what folder, what permission.  This would then generate a CREATE CONNECTION m ON PORTAL... EXECUTE BEGIN PUBLISH ... END;  We could also manage datasets and data caching lifecycle too.  Last 24 hrs, refresh at 7 AM ...  Just some easy button deploy options.
