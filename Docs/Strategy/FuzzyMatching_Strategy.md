# Fuzzy Matching Strategy

**Status:** Phases 1–4 shipped; Phase 5 deferred  
**Date:** 2026-05-14  
**Scope:** New engine functions, query syntax, and normalization capabilities to support joining unstructured data to structured reference data.

> [!NOTE]
> The shipped fuzzy matching surface is documented in `Docs/Reference/Standard_Library.md` and `Docs/Reference/Grammar.md`. Phase 5 embedding/semantic matching is future work and is not part of the 0.7.0 feature-complete surface.

| Phase | Status |
| :--- | :--- |
| Phase 1 — `NORMALIZE()` | ✅ Shipped (`FuzzyFunctions.cs`) |
| Phase 2 — `SIMILARITY`, `LEVENSHTEIN`, `SOUNDEX`, `METAPHONE`, `DMETAPHONE` | ✅ Shipped |
| Phase 3 — `NGRAMS`, `NGRAM_TOKENS` | ✅ Shipped |
| Phase 4 — `FUZZY JOIN` syntax | ✅ Shipped |
| Phase 5 — Embedding-based semantic matching | ⏳ Deferred |

---

## Problem Statement

ETL-SQL increasingly processes unstructured data sources (flat files, OCR output, scraped text, freeform exports) that need to be joined or reconciled with structured reference data (customer tables, product catalogs, address registries, etc.). Exact joins fail because real-world unstructured data has typos, abbreviations, inconsistent formatting, and missing fields. Users currently have no built-in way to express a fuzzy match — they must either push the work into the underlying database (which only works when both sides live in the same database) or pre-process data outside ETL-SQL entirely.

---

## Goals

1. Make fuzzy matching a first-class operation in ETL-SQL scripts.
2. Cover the 80% case well rather than trying to solve every edge case upfront.
3. Keep the implementation layered so early phases deliver value independently and each phase builds cleanly on the previous one.
4. Avoid adding a heavy runtime dependency (no bundled ML model, no mandatory external service).

---

## Non-Goals

- Replacing dedicated record linkage tools (e.g. Splink, dedupe.io) for very large-scale deduplication projects.
- Bundling an inference engine or embedding model into the ETL-SQL runtime.
- Solving the general entity resolution problem (clustering + deduplication across a single dataset). That's a separate effort.

---

## Background: The Matching Problem

When a user tries to join an unstructured dataset to a reference table, three things can cause exact joins to fail:

1. **Surface variation** — `"AT&T"` vs `"AT and T"`, `"St."` vs `"Street"`, `"john smith"` vs `"John Smith"`
2. **Typos and OCR errors** — `"Microsft"`, `"Jhn Smth"`, transposed characters
3. **Semantic variation** — `"Ford F-150"` and `"F150 pickup"` mean the same thing but share almost no characters

Strategies 1–2 are solved by string similarity algorithms and normalization. Strategy 3 requires semantic embeddings and is categorically harder. This document covers all three but recommends deferring #3 until the foundation is solid.

---

## Proposed Implementation: Five Phases

### Phase 1 — String Similarity Functions

**Scope:** New built-in scalar functions in the expression evaluator.  
**Effort estimate:** Medium (1–2 weeks). Pure C# implementation, no external dependencies.

#### Functions

```sql
-- Edit distance (raw integer — number of edits to transform a into b)
LEVENSHTEIN(a, b)                        -- returns INT

-- Normalized similarity scores — all return DECIMAL between 0.0 and 1.0
SIMILARITY(a, b)                         -- default algorithm (Jaro-Winkler)
SIMILARITY(a, b, 'JAROWINKLER')
SIMILARITY(a, b, 'LEVENSHTEIN')          -- normalized: 1 - (distance / max_length)
SIMILARITY(a, b, 'TRIGRAM')              -- character trigram overlap (like pg_trgm)
SIMILARITY(a, b, 'JACCARD')             -- word-token Jaccard: |intersect| / |union|
SIMILARITY(a, b, 'TOKENSORT')           -- Jaro-Winkler after sorting tokens alphabetically

-- Phonetic encoding
SOUNDEX(s)                               -- already documented; needs engine implementation
METAPHONE(s)                             -- more accurate than Soundex
DMETAPHONE(s)                            -- Double Metaphone — returns primary code
DMETAPHONE_ALT(s)                        -- Double Metaphone — alternate code (for joins)
```

#### Algorithm Guide (when to use which)

| Algorithm | Best for | Avoid when |
|-----------|----------|------------|
| `JAROWINKLER` | Person names, short identifiers | Long strings, word-order variation |
| `LEVENSHTEIN` | Short strings with typos | Strings of very different lengths |
| `TRIGRAM` | General purpose, partial matches, longer strings | Very short strings (< 4 chars) |
| `JACCARD` | Strings where word presence matters more than order | Single-word strings |
| `TOKENSORT` | Names where first/last may be swapped | Strings that aren't name-like |
| `SOUNDEX` / `METAPHONE` | Names where spelling varies but pronunciation is consistent | Non-name strings |

#### Usage Patterns

```sql
-- Basic similarity check in a WHERE clause
SELECT a.unstructured_name, b.canonical_name,
       SIMILARITY(a.unstructured_name, b.canonical_name) AS score
FROM   #dirty a
CROSS JOIN #reference b
WHERE  SIMILARITY(a.unstructured_name, b.canonical_name) > 0.80
ORDER BY a.id, score DESC;

-- Phonetic join (fast — it's an exact join on the encoded value)
SELECT a.*, b.*
FROM   #dirty a
JOIN   #reference b ON SOUNDEX(a.name) = SOUNDEX(b.name);

-- Composite score combining two algorithms
SELECT a.id,
       b.id AS ref_id,
       0.6 * SIMILARITY(a.name, b.name, 'JAROWINKLER')
     + 0.4 * SIMILARITY(a.name, b.name, 'TRIGRAM') AS composite_score
FROM   #dirty a
CROSS JOIN #reference b
ORDER BY a.id, composite_score DESC;
```

#### Known Drawback: The Cross-Join Wall

Any `CROSS JOIN` + `WHERE SIMILARITY(...) > threshold` pattern is O(n×m). With 10k unstructured records × 100k reference records, that is 1 billion comparisons. This is the primary motivation for Phase 3 (blocking) and Phase 4 (FUZZY JOIN with built-in blocking).

Document this limit clearly. For the initial phase, recommend the phonetic blocking workaround: join on `SOUNDEX()` or `METAPHONE()` first, then score within that candidate set.

```sql
-- Recommended pattern for large datasets until FUZZY JOIN exists
SELECT a.*, b.*, SIMILARITY(a.name, b.name) AS score
INTO   #candidates
FROM   #dirty a
JOIN   #reference b ON METAPHONE(a.name) = METAPHONE(b.name);   -- blocking pass

SELECT *, ROW_NUMBER() OVER (PARTITION BY a_id ORDER BY score DESC) AS rank
FROM #candidates
WHERE score > 0.75;
```

---

### Phase 2 — NORMALIZE() Function

**Scope:** A single preprocessing function with domain-aware presets.  
**Effort estimate:** Small (3–5 days). String manipulation, no external dependencies.

This is arguably the highest ROI item in the entire plan. Normalization eliminates a large class of false non-matches before any similarity algorithm runs. An 80% match rate can often reach 90%+ with normalization alone.

#### Syntax

```sql
NORMALIZE(expression)                    -- basic: lowercase, trim, collapse whitespace
NORMALIZE(expression, 'COMPANY')         -- company name preset
NORMALIZE(expression, 'PERSON')          -- person name preset
NORMALIZE(expression, 'ADDRESS')         -- address preset
NORMALIZE(expression, 'PHONE')           -- phone number to digits-only
NORMALIZE(expression, 'EMAIL')           -- lowercase, trim
```

#### What Each Preset Does

**Base (no preset):**
- Lowercase
- Trim leading/trailing whitespace
- Collapse internal whitespace to single space
- Unicode NFC normalization (resolves accented character variants)
- Strip zero-width and non-printable characters

**COMPANY:**
- All base transformations
- Remove legal suffixes: LLC, Inc, Corp, Ltd, Co, PLC, LLP, GmbH, SA, NV (and with/without trailing punctuation)
- Expand common abbreviations: `&` → `and`, `Intl` → `International`, `Mfg` → `Manufacturing`
- Remove articles: leading `The `, `A `, `An `
- Strip punctuation except hyphens within words

**PERSON:**
- All base transformations
- Remove common titles/suffixes: Mr, Mrs, Ms, Dr, Jr, Sr, II, III, MD, PhD (and with periods)
- Normalize hyphens in hyphenated names

**ADDRESS:**
- All base transformations
- Expand directional abbreviations: `N` → `North`, `S` → `South`, `E` → `East`, `W` → `West`, `NE` → `Northeast`, etc.
- Expand street type abbreviations: `St` → `Street`, `Ave` → `Avenue`, `Blvd` → `Boulevard`, `Dr` → `Drive`, `Rd` → `Road`, `Ln` → `Lane`, `Ct` → `Court`, `Pl` → `Place`, `Hwy` → `Highway`
- Remove apartment/unit designators: `Apt`, `Ste`, `Unit`, `#` followed by alphanumerics
- Normalize `PO Box` variants

**PHONE:**
- Strip all non-digit characters
- Remove leading country code `1` if result is 11 digits starting with 1

**EMAIL:**
- Lowercase and trim only (email local parts are technically case-sensitive but almost never are in practice)

#### Usage

```sql
-- Normalize before similarity scoring
SELECT SIMILARITY(
    NORMALIZE(a.company_name, 'COMPANY'),
    NORMALIZE(b.company_name, 'COMPANY')
) AS score
FROM #unstructured a
CROSS JOIN #reference b;

-- Normalize into a staging temp table once, then join repeatedly
SELECT id, NORMALIZE(company_name, 'COMPANY') AS norm_name INTO #norm_dirty FROM #dirty;
SELECT id, NORMALIZE(company_name, 'COMPANY') AS norm_name INTO #norm_ref   FROM #reference;

SELECT d.id, r.id, SIMILARITY(d.norm_name, r.norm_name) AS score
FROM #norm_dirty d CROSS JOIN #norm_ref r
WHERE SIMILARITY(d.norm_name, r.norm_name) > 0.80;
```

#### Implementation Notes

- Implement as a lookup table of regex substitutions applied in order for each preset. This makes it easy to add/adjust substitutions without changing function signatures.
- The abbreviation expansion lists should be data-driven (a static dictionary in C#) so they can be extended without touching the expression evaluator.
- Consider a `NORMALIZE_LIST` variant that returns all tokens as a `LIST` for downstream use.

---

### Phase 3 — Blocking Utilities

**Scope:** Helper functions and patterns to reduce candidate sets before expensive similarity scoring.  
**Effort estimate:** Small–Medium (1 week). Mostly built on Phase 1 phonetic functions; the new piece is an inverted-index temp table helper.

Phase 1 documents the cross-join performance problem and provides phonetic blocking as a manual workaround. Phase 3 formalizes blocking as a supported pattern and adds tooling to make it more accessible.

#### N-gram Tokenization Function

```sql
NGRAMS(s, n)        -- returns LIST of n-character grams
                    -- NGRAMS('hello', 3) → ['hel', 'ell', 'llo']

NGRAM_TOKENS(s)     -- convenience: returns 3-gram LIST, lowercased and normalized
```

#### Blocking Index Pattern

Rather than a new statement, provide a documented `CREATE TABLE` + `INSERT` pattern that builds an inverted index:

```sql
-- Build a trigram inverted index on the reference side (do this once)
SELECT gram, ref_id
INTO   #ref_index
FROM   #reference
CROSS APPLY (SELECT value AS gram FROM UNNEST(NGRAM_TOKENS(name))) t;

CREATE INDEX ix_gram ON #ref_index (gram);

-- Look up candidates for each unstructured record
SELECT DISTINCT d.id AS dirty_id, r.ref_id
INTO   #candidates
FROM   #dirty d
CROSS APPLY (SELECT value AS gram FROM UNNEST(NGRAM_TOKENS(d.name))) dg
JOIN   #ref_index r ON dg.gram = r.gram;

-- Now score only the candidates (much smaller set)
SELECT c.dirty_id, c.ref_id,
       SIMILARITY(d.name, r.name) AS score
FROM   #candidates c
JOIN   #dirty     d ON d.id = c.dirty_id
JOIN   #reference r ON r.id = c.ref_id
WHERE  SIMILARITY(d.name, r.name) > 0.80
ORDER BY c.dirty_id, score DESC;
```

This is verbose but explicit, and importantly, the user can see and control the blocking logic. The `FUZZY JOIN` in Phase 4 will automate this pattern internally.

#### Sorted Neighborhood Helper

For deduplication within a single dataset (finding duplicates in #dirty itself), the sorted neighborhood method is more practical than cross-join + blocking:

```sql
-- Sort by a blocking key, then score adjacent records within a window
SELECT a.id, b.id, SIMILARITY(a.name, b.name) AS score
FROM (
    SELECT *, ROW_NUMBER() OVER (ORDER BY NORMALIZE(name, 'COMPANY')) AS rn
    FROM #dirty
) a
JOIN (
    SELECT *, ROW_NUMBER() OVER (ORDER BY NORMALIZE(name, 'COMPANY')) AS rn
    FROM #dirty
) b ON ABS(a.rn - b.rn) <= 3    -- window size
     AND a.id < b.id             -- avoid self-match and duplicates
WHERE SIMILARITY(a.name, b.name) > 0.75;
```

---

### Phase 4 — FUZZY JOIN Syntax

**Scope:** New statement type: parser, AST node, statement handler, and query planner with built-in blocking.  
**Effort estimate:** Large (3–5 weeks). This is the biggest single implementation item.

This is the ergonomic centerpiece of the feature set. It expresses the fuzzy matching operation at the right level of abstraction and handles blocking internally so users don't hit the cross-join wall by accident.

#### Syntax

```sql
-- Basic form — one scoring expression
SELECT a.*, b.*, __score
FROM   #unstructured a
FUZZY JOIN #reference b
    ON SIMILARITY(NORMALIZE(a.name, 'COMPANY'), NORMALIZE(b.name, 'COMPANY')) > 0.80
    KEEP BEST 1;           -- per left row, keep only the highest-scoring match

-- Keep top N matches per left row
FUZZY JOIN #reference b
    ON SIMILARITY(a.name, b.name) > 0.75
    KEEP BEST 3;

-- Keep all matches above threshold (like a regular JOIN — result may have multiple rows per left row)
FUZZY JOIN #reference b
    ON SIMILARITY(a.name, b.name) > 0.80;

-- Composite scoring across multiple columns
FUZZY JOIN #reference b
    ON 0.6 * SIMILARITY(a.name, b.name) + 0.4 * SIMILARITY(a.city, b.city) > 0.75
    KEEP BEST 1;

-- LEFT FUZZY JOIN — unmatched left rows appear with NULLs (like LEFT JOIN)
SELECT a.*, b.*, __score
FROM   #unstructured a
LEFT FUZZY JOIN #reference b
    ON SIMILARITY(a.name, b.name) > 0.80
    KEEP BEST 1;
```

#### Automatic Output Column

`__score` is always injected into the result set with the similarity score of the winning match. This is important for downstream filtering and human review.

#### Semantics — The Hard Decisions

These need to be settled before parser work begins:

| Question | Decision |
|----------|----------|
| Multiple matches above threshold without `KEEP BEST` | Behaves like a regular JOIN — all matching rows returned (result may fan out) |
| No match above threshold | With plain `FUZZY JOIN`: row is excluded (like INNER JOIN). With `LEFT FUZZY JOIN`: row included with NULL right-side columns and `__score = NULL` |
| `KEEP BEST 1` with a tie | Deterministic tiebreak by right-side row order (first encountered wins). Document this. |
| `__score` column name conflict | Error at parse time if the user's SELECT already includes a column named `__score` |
| Threshold in `ON` clause vs. separate `THRESHOLD` keyword | Inline `ON` — keeps it familiar and composable with the full expression evaluator |

#### Internal Query Plan

The FUZZY JOIN handler should never execute as a raw cross-join. Internally it should:

1. **Build a blocking index** on the right-side table using trigrams of the join key column(s). This is the Phase 3 inverted-index pattern, automated.
2. **Candidate lookup** — for each left row, query the blocking index to retrieve a candidate set (typically 10–100 records rather than the full right-side table).
3. **Score candidates** — apply the full ON expression against the candidate set only.
4. **Filter and rank** — apply threshold, apply `KEEP BEST N`.
5. **Project output** — join selected candidates back to full right-side rows, inject `__score`.

The blocking index is built once per `FUZZY JOIN` clause and reused for all left rows. It should be discarded after the statement completes.

#### Scale Expectations (Be Honest in the Docs)

| Left rows | Right rows | Expected behavior |
|-----------|------------|-------------------|
| < 10k | < 100k | Fast. Blocking handles it comfortably. |
| 10k–100k | < 500k | Acceptable. Blocking critical; without it this is unworkable. |
| > 100k | > 500k | This is where dedicated record linkage tools become appropriate. ETL-SQL can still handle it but may be slow. |

#### Known Drawback: Blocking Can Miss True Matches

The trigram blocking index is a recall optimization — it trades a small probability of missing a true match for a large reduction in comparisons. If two strings share no trigrams (e.g., completely garbled OCR output), the candidate will never be generated and the match is missed entirely.

Mitigation: Add phonetic blocking as a fallback. The handler can build both a trigram index and a METAPHONE index and take the union of candidates from both. This recovers most blocking misses at modest extra cost.

#### Drawback: Composite Multi-Column Scoring Is Hard to Block On

`SIMILARITY(a.name, b.name) > 0.75` is straightforward to block on (index on name trigrams). `0.6 * SIMILARITY(a.name, b.name) + 0.4 * SIMILARITY(a.city, b.city) > 0.75` requires blocking on both columns. The handler should detect which columns appear in `SIMILARITY()` calls within the ON expression and build a blocking index for each, then intersect or union candidates as appropriate.

For the first version, it is acceptable to only block on the first detected column and document this limitation.

---

### Phase 5 — Embedding-Based Semantic Matching

**Scope:** Vector column type, EMBED() function, and SIMILARITY() with cosine mode.  
**Effort estimate:** Very Large (6–10 weeks, not including model hosting decisions).

This phase addresses the semantic variation problem — cases where strings mean the same thing but share few or no characters. No string algorithm solves `"Ford F-150"` ↔ `"F150 pickup truck"`. Embeddings do.

#### Why Defer This

- Phases 1–4 solve the 80% case (typos, abbreviations, formatting). Most users will not hit the semantic variation wall before the simpler phases are in place.
- Embeddings require a model. The deployment story for bundled models is complex (binary size, native dependencies, cross-platform inference runtime). The API-based approach is simpler but introduces a runtime dependency on an external service.
- The vector storage and ANN search problem is non-trivial inside the existing engine architecture.
- This should only be designed after the team has real feedback from users of Phases 1–4 about where they're still hitting walls.

#### Recommended Architecture (When the Time Comes)

**Do not bundle a model.** Instead, use a pluggable endpoint architecture:

```sql
-- Configure an embedding endpoint in appsettings.json or via DECLARE
DECLARE @embed_endpoint VARCHAR = 'http://localhost:11434/api/embed';   -- Ollama
-- or 'https://api.openai.com/v1/embeddings'
-- or any OpenAI-compatible endpoint

-- Embed a column's values and store as a VECTOR column in a temp table
SELECT id, name, EMBED(name, @embed_endpoint, 'nomic-embed-text') AS name_vec
INTO   #ref_embedded
FROM   #reference;

-- At query time, embed the unstructured record and find nearest neighbors
SELECT r.id, r.name, SIMILARITY(d.name_vec, r.name_vec, 'COSINE') AS score
FROM   (SELECT EMBED('F150 pickup truck', @embed_endpoint, 'nomic-embed-text') AS name_vec) d
CROSS JOIN #ref_embedded r
ORDER BY score DESC
LIMIT 10;
```

**Vector column type:** The `VECTOR` data type should be added to the type system to hold float arrays. It already appears in `LanguageMetadata.DataTypes` — it just needs actual runtime support (storage as `float[]`, serialization in the manifest/datasets).

**ANN Search:** For large reference datasets, cosine similarity via `CROSS JOIN` has the same O(n×m) problem as string similarity. The long-term solution is an approximate nearest-neighbor index (FAISS, HNSW). For an initial version, exact brute-force cosine on vectors is acceptable up to ~50k rows and can be parallelized via `Parallel.For` in the engine.

**Embedding pre-computation:** The expensive part is embedding the structured reference data. This should be a one-time operation, with results stored either in a temp table (within a session) or in a dedicated dataset (persisted across sessions via `CREATE DATASET`). The user should not have to re-embed the reference data on every run.

#### Known Drawbacks

- **Model dependency.** The quality of results is entirely dependent on the embedding model. A general model may not understand domain-specific terms (medical codes, part numbers, internal jargon). Users with specialized domains will need domain-fine-tuned models.
- **Score opacity.** Cosine distance has no intuitive interpretation. 0.92 doesn't mean much to a user. Pair with a calibration step (show a sample of 0.9+ matches and let the user assess quality) in the cookbook.
- **Latency.** Embedding a large dataset via an external API is slow. 100k records at ~1ms each = ~100 seconds minimum, often more. Design for async/batched embedding calls.
- **Debugging.** When a match is wrong, there's no clear explanation. Add a `SIMILARITY_DEBUG()` function that returns the top contributing n-gram or token for string methods — this doesn't help with embeddings but sets a precedent for transparency.

---

## Recommended Implementation Order

| Phase | What | Status |
|-------|------|--------|
| **1** | `NORMALIZE()` | ✅ Shipped — highest ROI, no new syntax, eliminates surface variation before any scoring |
| **2** | `SIMILARITY()` + phonetic functions | ✅ Shipped — core scoring primitives; unlocks manual blocking patterns |
| **3** | Blocking utilities (`NGRAMS`, `NGRAM_TOKENS`) | ✅ Shipped — enables inverted-index blocking for power users |
| **4** | `FUZZY JOIN` | ✅ Shipped — trigram blocking index, LEFT FUZZY JOIN, __score injection, KEEP BEST n |
| **5** | Embedding / semantic matching | ⏳ Deferred — gather Phase 1–4 user feedback first |

---

## Testing Strategy

Fuzzy matching is particularly susceptible to regression — a change in a similarity algorithm or normalization rule can silently change match rates on existing data. For each phase:

- **Unit tests:** Cover each algorithm with known pairs and expected score ranges. Include edge cases: empty strings, single characters, identical strings, completely different strings.
- **Normalization tests:** One test per abbreviation/suffix in each preset. Regression tests for the full normalization pipeline on a fixed input corpus.
- **Threshold sensitivity tests:** For a fixed dataset, assert that at threshold 0.8 the true positive rate is above X% and false positive rate is below Y%. These bounds need to be established empirically and committed as test expectations.
- **Performance tests:** Mark as `Category=Performance`. For `FUZZY JOIN`, assert that 10k × 100k completes within a reasonable wall-clock bound (e.g., 30 seconds) with blocking enabled.

---

## Documentation Plan

- `Docs/Reference/Standard_Library.md` — add `SIMILARITY`, `LEVENSHTEIN`, `METAPHONE`, `DMETAPHONE`, `NORMALIZE`, `NGRAMS` to the function reference
- `Docs/Report_Cookbook.md` — add a "Fuzzy Matching" section with recipes for each common pattern (name matching, company matching, address matching, product matching)
- `Help/Functions/FUZZY.md` — new help file covering all fuzzy functions
- `Docs/Reference/Grammar.md` — `FUZZY JOIN` syntax added to the JOIN section
- Architecture doc — document the blocking index design and its performance characteristics
