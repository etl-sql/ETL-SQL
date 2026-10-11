# ETL-SQL SLT Coverage Reference

## What is the SLT?

The **SQLite Logic Test** (SLT) suite was created by D. Richard Hipp as a cross-engine SQL correctness test. It encodes thousands of SQL queries with hash-verified expected results so any SQL engine can run the same tests and compare output. The full suite focuses heavily on SELECT semantics — expression evaluation, NULL propagation, aggregate behavior, join ordering, and sorting.

ETL-SQL carries a curated subset of the SLT corpus: the five primary SELECT-coverage files (`select1`–`select5`), a set of DDL/DML evidence files adapted from the original SLT evidence directory, and a growing library of ETL-SQL-specific custom test files. All SLT tests are gated behind the `ETL_SQL_RUN_SLT=1` environment variable and the `Category=SLT` trait to keep normal CI fast.

---

## Active corpus inventory

### Corpus files (`tests/slt_data/corpus/`)

These are taken directly from the SQLite Logic Test suite. Each builds a table `t1(a,b,c,d,e INTEGER)` with 30 rows and then fires hundreds to thousands of query variations.

| File | Statements | Queries | What it covers |
| :--- | ---: | ---: | :--- |
| `select1.test` | 31 | 1,000 | Core SELECT: column references, arithmetic, CASE, scalar subqueries, ORDER BY |
| `select2.test` | 31 | 1,000 | Same patterns as select1 with NULLs seeded in the data |
| `select3.test` | 31 | 3,320 | Extended SELECT: correlated subqueries, multi-column CASE, complex arithmetic combos |
| `select4.test` | 1,025 | 2,832 | Multi-table joins (join0–join499), cross-join combinations, join with aggregates |
| `select5.test` | 704 | 732 | INSERT/SELECT patterns, type-specific result formatting, edge-case column expressions |
| **Total** | **1,822** | **8,884** | |

### Evidence files (`tests/slt_data/evidence/`)

Adapted from the SLT evidence directory. These validate specific SQL statement semantics.

| File | Records | What it covers |
| :--- | ---: | :--- |
| `in1.test` | 216 | IN/NOT IN: empty sets, NULL in left/right operand, scalar vs table forms |
| `in2.test` | 54 | IN cross-engine compliance; row value expressions; multi-column subquery errors |
| `slt_lang_createview.test` | 25 | CREATE VIEW; duplicate view error; TEMP views; DROP VIEW cascade |
| `slt_lang_dropview.test` | 13 | DROP VIEW semantics; DROP IF EXISTS; non-existent view error |
| `slt_lang_droptable.test` | 12 | DROP TABLE; DROP IF EXISTS; index cascade on drop |
| `slt_lang_dropindex.test` | 8 | DROP INDEX; error on non-existent index |
| `slt_lang_reindex.test` | 7 | REINDEX command (SQLite/Postgres; skips MSSQL/Oracle/MySQL) |
| `slt_lang_replace.test` | 14 | REPLACE INTO; conflict resolution on PRIMARY KEY; insert-or-update semantics |
| `slt_lang_update.test` | 27 | UPDATE with WHERE, multi-column SET, expressions (x=x+2), LIMIT/OFFSET |

### Custom ETL-SQL tests (`tests/slt_data/*.test`)

Hand-authored tests covering ETL-SQL-specific features and SQL areas not in the corpus.

| File | Records | What it covers |
| :--- | ---: | :--- |
| `aggregates.test` | 23 | SUM/AVG/COUNT/MIN/MAX; FILTER clause; GROUPING SETS/ROLLUP; COUNT(DISTINCT) |
| `alter_table.test` | 18 | ALTER TABLE ADD COLUMN, RENAME COLUMN, DROP COLUMN; `#temp` table indexes |
| `asof_join.test` | 33 | Nearest-match temporal/inequality joins: backward (`>=`), forward (`<=`), strict inequality (`>`), equi-partition keys, inner drops, ASOF LEFT NULL preservation, unsorted inputs, duplicate timestamps, empty left/right tables, NULL handling |
| `basic.test` | 5 | Fundamental CREATE/INSERT/SELECT/ORDER BY/SUM smoke |
| `case.test` | 10 | Searched CASE; simple CASE (operand matching); CASE with NULLs; nested CASE; CASE in WHERE/ORDER BY |
| `cte.test` | 12 | Simple CTEs; chained WITH; WITH RECURSIVE; CTE in subquery |
| `date_functions.test` | 11 | DATEPART, DATEDIFF, DATEADD; date string ordering |
| `distinct.test` | 12 | SELECT DISTINCT; COUNT(DISTINCT); SUM(DISTINCT); DISTINCT with NULLs |
| `dml.test` | 35 | UPDATE (WHERE, arithmetic, CASE-in-SET, subquery-in-WHERE, multi-column, unconditional, no-op); DELETE (WHERE, subquery-IN-WHERE, no-op, unconditional) |
| `expressions.test` | 7 | Arithmetic; UPPER/LOWER; CAST; CASE in expression list |
| `functions.test` | 11 | UPPER, TRIM, ABS, ROUND, LEN, COALESCE (2-arg, multi-arg, all-NULL fallback), IIF |
| `fuzzy_logic.test` | 10 | SOUNDEX, LEVENSHTEIN, SIMILARITY, METAPHONE, DMETAPHONE, NORMALIZE |
| `generators.test` | 11 | GENERATE_SERIES; STRING_SPLIT; NGRAMS; CROSS APPLY |
| `groupby.test` | 7 | GROUP BY with aggregates; HAVING; ORDER BY on aggregate; HAVING without GROUP BY (scalar aggregation pass/fail) |
| `insert.test` | 24 | INSERT VALUES (basic, NULL, expressions); INSERT SELECT (filtered, with JOIN, with aggregate) |
| `join.test` | 26 | INNER/LEFT/RIGHT/FULL OUTER/CROSS JOIN; comma-join syntax; 3- and 5-table joins |
| `json_xml.test` | 18 | ISJSON; JSON_VALUE/JSON_QUERY; XMLVALUE path expressions |
| `language_extensions.test` | 31 | GROUP BY ALL (single aggregate, empty table, constants, non-aggregates only, expressions wrapping aggregates, lateral aliases, NULL grouping, scalar aggregates, HAVING); ORDER BY ALL; `* EXCLUDE`, `REPLACE`, `RENAME`; USING SAMPLE (0%, 100%, rows, repeatable) |
| `lateral.test` | 17 | CROSS JOIN LATERAL; LEFT JOIN LATERAL (OUTER APPLY); Comma LATERAL; CROSS APPLY; correlated subqueries with LIMIT; scalar vs grouped aggregate lateral; empty outer table; arithmetic correlation; post-filtering ON predicates; multi-row expansions |
| `match_recognize.test` | 7 | MATCH_RECOGNIZE; PARTITION BY; PATTERN/DEFINE; V-shape detection; ALL ROWS PER MATCH classifier output; repeated match numbering |
| `merge.test` | 40 | MERGE upsert (WHEN MATCHED UPDATE + WHEN NOT MATCHED INSERT); inventory top-up with conditional update; WHEN MATCHED AND condition; unmatched source rows silently ignored |
| `mini_select1.test` | 33 | Complex CASE/arithmetic combos; hash-verified 60-value result sets |
| `null_edge_cases.test` | 17 | NULL comparison (= vs IS NULL); NULL in arithmetic; NULLIF; three-valued logic; BETWEEN with NULL |
| `nulls.test` | 10 | COUNT(*) vs COUNT(x); SUM over NULLs; IS NULL; COALESCE |
| `obscure_features.test` | 33 | SEMI JOIN, ANTI JOIN, LEFT SEMI/ANTI JOIN; QUALIFY (compound predicates, pipeline execution WHERE -> GROUP BY -> HAVING -> WINDOW -> QUALIFY -> ORDER BY, ties with RANK/DENSE_RANK, impossible filters `1=0` / negative / NULL, empty tables, `1=1`, LAG boundary detection, COUNT(*) window, DISTINCT deduplication, LIMIT) |
| `pivot_unpivot.test` | 6 | PIVOT (rows→columns); UNPIVOT (columns→rows) |
| `set_operations.test` | 30 | UNION/UNION ALL; EXCEPT; INTERSECT; NULL handling; nested set ops |
| `standard_library_boundaries.test` | 21 | Deterministic standard library boundary tests: math, date leap year/clamping, string functions |
| `string_functions.test` | 17 | UPPER/LOWER/TRIM/LEN/SUBSTRING/REPLACE/LEFT/RIGHT/CHARINDEX/CONCAT/REVERSE |
| `subquery.test` | 16 | Scalar subquery in SELECT (populated and 0-match NULL); IN(SELECT); NOT IN with NULL (3-valued logic); EXISTS/NOT EXISTS; correlated subquery |
| `transform.test` | 51 | TRANSFORM statement algorithms: FILL_DATES, INTERPOLATE (linear, forward, NOCB, boundary NULLs) |
| `type_coercion.test` | 23 | Integer vs decimal division; CAST; truncation; NULL CAST; boolean comparison |
| `window.test` | 14 | ROW_NUMBER; RANK; running SUM; LAG/LEAD; ROWS BETWEEN framing; NTILE |

---

## Excluded files

| File | Reason |
| :--- | :--- |
| `corpus/select4_debug.test` | Truncated artifact — identical 1,025 setup statements as `select4.test` but only 1,019 of its 2,832 queries (cuts off before complex join tests). No unique content; deleted from repo. |
| `evidence/slt_lang_aggfunc.test` | SQLite-only by design. Opens with `skipif sqlite; halt` — the SLT convention for "run this only on SQLite." Tests `total()` (SQLite-only function, returns 0.0 not NULL for empty sets), `group_concat()` (SQLite-specific; standard SQL uses `STRING_AGG`/`LISTAGG`), and non-numeric string coercion to 0 in `avg()`/`sum()` — all SQLite-specific behaviors ETL-SQL correctly does not replicate. |
| `evidence/slt_lang_createtrigger.test` | ETL-SQL has no trigger support. Deleted from repo. |
| `evidence/slt_lang_droptrigger.test` | ETL-SQL has no trigger support. Deleted from repo. |
| `index/` subdirectories | 8 directories (`between/`, `commute/`, `delete/`, `in/`, `orderby/`, `orderby_nosort/`, `random/`, `view/`) containing real SLT index-optimization tests — 10,000+ queries per file, testing that indexed and non-indexed query paths return identical results. Excluded because all files use `CREATE INDEX` on regular (non-temp) tables, which ETL-SQL does not support (ETL-SQL supports `CREATE INDEX` only on `#temp` tables). Files are retained in the repo as a future coverage target. |

---

## Coverage confidence matrix

| SQL feature area | Confidence | Evidence |
| :--- | :---: | :--- |
| SELECT expressions, arithmetic, type formatting | **High** | select1–3: 5,320 queries |
| ORDER BY (all sort modes), LIMIT/OFFSET | **High** | select1–3, custom tests |
| CASE (searched, simple, nested, with NULLs) | **High** | select1–3: hundreds of CASE queries; case.test |
| JOINs (INNER, LEFT, CROSS, multi-table) | **High** | select4: 2,832 join-heavy queries; join.test |
| ASOF JOIN (temporal, nearest-match, partitioned) | **High** | asof_join.test (21 records: backward, forward, equi-partitions, inner/left, ties, unsorted, NULLs) |
| LATERAL / APPLY (CROSS/LEFT LATERAL, APPLY) | **High** | lateral.test (11 records) + generators.test (11 records) |
| Aggregate functions (COUNT, SUM, AVG, MIN, MAX) | **High** | select1–3, aggregates.test |
| GROUP BY / HAVING | **High** | select1–3, groupby.test, aggregates.test |
| GROUP BY ALL (modern extension) | **High** | language_extensions.test (24 records: expressions, literals, window functions, lateral aliases, NULL grouping, scalar aggregates, HAVING) |
| QUALIFY (window filtering) | **High** | obscure_features.test (22 records: pipeline execution WHERE->GROUP BY->HAVING->WINDOW->QUALIFY, compound predicates, ties, empty match, LIMIT) |
| Scalar subqueries | **High** | select1–3, subquery.test |
| Correlated subqueries | **High** | select3, subquery.test, lateral.test |
| EXISTS / NOT EXISTS | **High** | subquery.test |
| IN / NOT IN (all NULL variants) | **High** | in1.test (216 records), in2.test |
| NULL semantics (3-valued logic, aggregate exclusion) | **High** | select2 (NULL-seeded data), nulls.test, null_edge_cases.test |
| CASE with NULL input / NULLIF | **High** | case.test, null_edge_cases.test |
| CTEs (simple, chained, recursive) | **High** | cte.test |
| Set operations (UNION, INTERSECT, EXCEPT) | **High** | set_operations.test |
| Window functions (ROW_NUMBER, RANK, LAG/LEAD, frames) | **High** | window.test |
| DISTINCT (single/multi-column, with NULLs) | **High** | distinct.test |
| Type coercion / CAST | **High** | type_coercion.test |
| String functions (UPPER/LOWER/TRIM/SUBSTRING etc.) | **High** | string_functions.test |
| Standard library boundary cases | **High** | standard_library_boundaries.test (21 records: leap year, clamping, math sign/round) |
| TRANSFORM algorithms (FILL_DATES, INTERPOLATE) | **High** | transform.test (51 records: fill dates, linear/forward/nocb interpolation) |
| PIVOT / UNPIVOT | **Medium** | pivot_unpivot.test (6 records) |
| Date functions (DATEPART/DATEDIFF/DATEADD) | **Medium** | date_functions.test (11 records) |
| JSON/XML functions | **Medium** | json_xml.test (18 records) |
| ETL-SQL generators (GENERATE_SERIES, NGRAMS) | **Medium** | generators.test (11 records) |
| MATCH_RECOGNIZE | **Medium** | match_recognize.test (7 records: one-row measures, classifier rows, repeated matches) |
| VIEW CREATE/DROP | **Low** | createview.test (25 records), dropview.test |
| INSERT / REPLACE INTO | **Medium** | select5, replace.test, insert.test (VALUES with NULL and expressions; INSERT SELECT with JOIN and aggregate) |
| UPDATE | **Medium** | update.test + dml.test (arithmetic SET, CASE-in-SET, subquery-in-WHERE, multi-column, unconditional, no-op) |
| DELETE | **Medium** | dml.test (WHERE, subquery-in-WHERE, no-op, unconditional DELETE) |
| MERGE | **Medium** | merge.test (upsert, conditional WHEN MATCHED AND, inventory top-up, unmatched-source-ignored) |
| ALTER TABLE | **Medium** | alter_table.test (18 records: ADD COLUMN, RENAME COLUMN, DROP COLUMN, #temp indexes) |
| PRIMARY KEY / UNIQUE constraints | **None** | Not in SLT scope |
| FOREIGN KEY constraints | **None** | Not in SLT scope |
| CHECK / DEFAULT constraints | **None** | Not in SLT scope |
| Transactions (BEGIN/COMMIT/ROLLBACK) | **None** | Not in SLT scope |
| Triggers | **None** | Intentionally unsupported |

---

## Gap analysis

### Modern SQL Extensions & Outlier Verification

Modern SQL extensions are extensively tested against common gotchas and edge cases:

1. **ASOF JOIN (`asof_join.test`)**:
   - Directionality: Verified backward (`>=`: latest prior) and forward (`<=`: earliest next).
   - Equi-join key partitions: Partition isolation ensures tickers/groups do not cross-match.
   - Dropping vs. preservation: Inner `ASOF JOIN` drops rows before the earliest right timestamp; `ASOF LEFT JOIN` preserves them with `NULL`.
   - Out-of-order data: Correct nearest candidate is identified regardless of row arrival or insertion sequence.
   - High frequency: Multiple left events correctly match the identical right record when no newer state exists.
   - Three-valued logic: `NULL` partition keys and `NULL` timestamps never produce false matches.

2. **GROUP BY ALL (`language_extensions.test`)**:
   - Expressions: Non-aggregate calculations (`UPPER(col)`, `price * qty`) are grouped automatically.
   - Literals: String and numeric constant expressions in the `SELECT` list are safely handled without breaking group-by expansion.
   - Window functions: Window expressions (`ROW_NUMBER()`, `RANK()`) are excluded from group keys.
   - Lateral column aliases: Referencing earlier aliases in the same `SELECT` list expands and groups correctly.
   - Null values: `NULL` values in grouping columns are consolidated into a single distinct group.
   - Total aggregates: Queries with only aggregate expressions evaluate as a single scalar summary row.

3. **QUALIFY Clause (`obscure_features.test`)**:
   - Pipeline ordering: Strictly verifies that execution order is `WHERE` &rarr; `GROUP BY` &rarr; `HAVING` &rarr; `WINDOW FUNCTIONS` &rarr; `QUALIFY` &rarr; `ORDER BY` &rarr; `LIMIT`.
   - Compound predicates: Supports compound boolean expressions combining window function aliases and regular scalar columns.
   - Ties handling: Validates `RANK()` ties preservation vs. `DENSE_RANK()`.
   - Empty results: Predicates matching 0 rows terminate cleanly with empty result sets.

4. **LATERAL / APPLY (`lateral.test`, `generators.test`)**:
   - Correlation: Inner subqueries reference outer table columns dynamically.
   - Outer preservation: `LEFT JOIN LATERAL ... ON true` and `OUTER APPLY` preserve outer rows even when inner subqueries return 0 rows.
   - Comma syntax: Supports `, LATERAL (...)` as `CROSS APPLY`.

### What matters for ETL-SQL use cases

ETL-SQL is a scripting engine for data movement and transformation, not an OLTP application database. The majority of user scripts are SELECT-heavy pipelines. Evaluated through that lens:

**DML & Schema gaps closed**: `dml.test`, `insert.test`, `merge.test`, and `alter_table.test` bring UPDATE, DELETE, INSERT SELECT, MERGE, and ALTER TABLE on `#temp` tables to **Medium** confidence. The remaining gaps are intentionally out of scope for an ETL scripting engine:

- Constraint enforcement (PRIMARY KEY, FOREIGN KEY, CHECK) — ETL-SQL processes data; it does not enforce application schema rules
- Transactions — ETL-SQL pipelines are single-session scripts, not concurrent OLTP workloads
- Triggers — explicitly unsupported

---

## How to run

```bash
# Skip SLT (normal CI — runs nothing, 7 tests skipped)
dotnet test ETL-SQL.slnx --filter "Category=SLT"

# CorpusRegressionTests only — CI gate, ~2s, no env var needed... wait, these need the gate too
# Set ETL_SQL_RUN_SLT=1 first

# Full SLT suite (manual / release validation)
$env:ETL_SQL_RUN_SLT = "1"
dotnet test ETL-SQL.slnx --filter "Category=SLT"

# Via script (saves TRX + log to slt_results/)
.\scripts\Test-SltCorpus.ps1
.\scripts\Parse-SltResults.ps1   # parse most recent run

# Corpus-only (large files, OOM risk on select4/select5 — run with care)
.\scripts\Test-SltCorpus.ps1 -CorpusOnly
```

> **OOM note**: `select4.test` (1,167 KB, 2,832 queries) and `select5.test` (686 KB, 732 queries) run large multi-table join workloads. On machines with < 8 GB available RAM, run `-CorpusOnly` separately or use `CorpusRegressionTests` (targeted hand-crafted equivalents) for routine validation.
