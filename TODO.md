# ETL-SQL Development Roadmap
## Bugs
- [ ] **CROSS HIGHLIGHT not working both directions**  Using kitchen sink report as reference.  "C:\Users\chuck\scratch\ETL-SQL\samples\kitchen_sinks\report_kitchen_sink.rptsql".  Clicking on a bar on BarByRegion works perfectly to highlight against DrillRegionDetail.  But clicking on DrillRegionDetail does nothing to BarByRegion.  Note the users has to have stopped cross highlighting by BarByRegion first its not a both ways at the same time.  So workflow of click East twice on BarByRegion, once for on, once for off.   Then clicking Grocery on DrillRegionDetail should trigger to cross highlight against BarByRegion.  I'm guessing here but it should show about 25% in East and 75% ghosted, 20% in South and 80% ghosted,... 

-[x] **Publish failed Login failed: invalid credentials.** When trying to publish to the portal I get a failure for invalid credentials, they are not, I just logged in with those.

-[ ] **DRILL_IN not working in VS Code** This may be a VS Code limitation but drill in does not work in the preview sidebar but works fine in Portal and report player

-[ ] **Running .\scripts\Test-AllSamples.ps1 returns all as failures?** They all used to work just fine what changed? 

## Up Next
- [ ] **Report portal add Active Directory support**  Need to add the ability to hook credentials with active directory.

- [ ] **Fuzzy Matching — Full Feature Set** — See `Docs/Strategy/FuzzyMatching_Strategy.md` for the complete design. Five phases in recommended order:
    - **Phase 1 — `NORMALIZE()` function** *(go first — highest ROI, smallest scope)*: Domain-aware string preprocessing with presets for COMPANY, PERSON, ADDRESS, PHONE, EMAIL. Eliminates surface variation before any similarity algorithm runs.
    - **Phase 2 — String Similarity & Phonetic Functions**: `SIMILARITY(a, b, algorithm)` supporting JAROWINKLER, LEVENSHTEIN, TRIGRAM, JACCARD, TOKENSORT. Engine-level `SOUNDEX`, `METAPHONE`, `DMETAPHONE`. Foundation for all subsequent phases.
    - **Phase 3 — Blocking Utilities**: `NGRAMS(s, n)` and `NGRAM_TOKENS(s)` to support user-built inverted-index blocking patterns. Documented cookbook recipes for manual block → score → rank pipeline.
    - **Phase 4 — `FUZZY JOIN` syntax** *(biggest ergonomic win)*: `FROM #a FUZZY JOIN #b ON SIMILARITY(...) > 0.80 KEEP BEST 1`. Built-in trigram blocking index; `LEFT FUZZY JOIN` variant; `__score` injected into result. Semantics for threshold/cardinality/ties fully documented in strategy doc.
    - **Phase 5 — Embedding-Based Semantic Matching** *(defer until Phase 4 ships)*: `EMBED(col, endpoint, model)` via pluggable HTTP endpoint (Ollama/OpenAI-compatible). `VECTOR` column type. `SIMILARITY(a, b, 'COSINE')`. For the cases where string algorithms fail entirely (semantic variation).