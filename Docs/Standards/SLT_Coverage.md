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
| `basic.test` | 5 | Fundamental CREATE/INSERT/SELECT/ORDER BY/SUM smoke |
| `case.test` | 8 | Searched CASE; CASE with NULLs; nested CASE; CASE in WHERE/ORDER BY |
| `cte.test` | 12 | Simple CTEs; chained WITH; WITH RECURSIVE; CTE in subquery |
| `date_functions.test` | 11 | DATEPART, DATEDIFF, DATEADD; date string ordering |
| `distinct.test` | 12 | SELECT DISTINCT; COUNT(DISTINCT); SUM(DISTINCT); DISTINCT with NULLs |
| `expressions.test` | 7 | Arithmetic; UPPER/LOWER; CAST; CASE in expression list |
| `functions.test` | 9 | UPPER, TRIM, ABS, ROUND, LEN, COALESCE, IIF |
| `fuzzy_logic.test` | 8 | SOUNDEX, LEVENSHTEIN, SIMILARITY, METAPHONE, DMETAPHONE, NORMALIZE |
| `generators.test` | 7 | GENERATE_SERIES; STRING_SPLIT; NGRAMS; CROSS APPLY |
| `groupby.test` | 5 | GROUP BY with aggregates; HAVING; ORDER BY on aggregate |
| `join.test` | 24 | INNER/LEFT/CROSS JOIN; comma-join syntax; 3- and 5-table joins |
| `json_xml.test` | 11 | ISJSON; JSON_VALUE/JSON_QUERY; XMLVALUE path expressions |
| `match_recognize.test` | 7 | MATCH_RECOGNIZE; PARTITION BY; PATTERN/DEFINE; V-shape detection; ALL ROWS PER MATCH classifier output; repeated match numbering |
| `mini_select1.test` | 33 | Complex CASE/arithmetic combos; hash-verified 60-value result sets |
| `null_edge_cases.test` | 17 | NULL comparison (= vs IS NULL); NULL in arithmetic; NULLIF; three-valued logic; BETWEEN with NULL |
| `nulls.test` | 10 | COUNT(*) vs COUNT(x); SUM over NULLs; IS NULL; COALESCE |
| `dml.test` | 31 | UPDATE (WHERE, arithmetic, CASE-in-SET, subquery-in-WHERE, multi-column, unconditional, no-op); DELETE (WHERE, subquery-IN-WHERE, no-op, unconditional) |
| `insert.test` | 24 | INSERT VALUES (basic, NULL, expressions); INSERT SELECT (filtered, with JOIN, with aggregate) |
| `merge.test` | 40 | MERGE upsert (WHEN MATCHED UPDATE + WHEN NOT MATCHED INSERT); inventory top-up with conditional update; WHEN MATCHED AND condition; unmatched source rows silently ignored |
| `pivot_unpivot.test` | 6 | PIVOT (rows→columns); UNPIVOT (columns→rows) |
| `set_operations.test` | 30 | UNION/UNION ALL; EXCEPT; INTERSECT; NULL handling; nested set ops |
| `string_functions.test` | 17 | UPPER/LOWER/TRIM/LEN/SUBSTRING/REPLACE/LEFT/RIGHT/CHARINDEX/CONCAT/REVERSE |
| `subquery.test` | 12 | Scalar subquery; IN(SELECT); NOT IN; EXISTS/NOT EXISTS; correlated subquery |
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
| Aggregate functions (COUNT, SUM, AVG, MIN, MAX) | **High** | select1–3, aggregates.test |
| GROUP BY / HAVING | **High** | select1–3, groupby.test, aggregates.test |
| Scalar subqueries | **High** | select1–3, subquery.test |
| Correlated subqueries | **High** | select3, subquery.test |
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
| PIVOT / UNPIVOT | **Medium** | pivot_unpivot.test (2 records) |
| Date functions (DATEPART/DATEDIFF/DATEADD) | **Medium** | date_functions.test (6 records) |
| JSON/XML functions | **Medium** | json_xml.test (7 records) |
| ETL-SQL generators (GENERATE_SERIES, NGRAMS) | **Medium** | generators.test (5 records) |
| MATCH_RECOGNIZE | **Medium** | match_recognize.test (3 query records: one-row measures, classifier rows, repeated matches) |
| VIEW CREATE/DROP | **Low** | createview.test (25 records), dropview.test |
| INSERT / REPLACE INTO | **Medium** | select5, replace.test, insert.test (VALUES with NULL and expressions; INSERT SELECT with JOIN and aggregate) |
| UPDATE | **Medium** | update.test + dml.test (arithmetic SET, CASE-in-SET, subquery-in-WHERE, multi-column, unconditional, no-op) |
| DELETE | **Medium** | dml.test (WHERE, subquery-in-WHERE, no-op, unconditional DELETE) |
| MERGE | **Medium** | merge.test (upsert, conditional WHEN MATCHED AND, inventory top-up, unmatched-source-ignored) |
| PRIMARY KEY / UNIQUE constraints | **None** | Not in SLT scope |
| FOREIGN KEY constraints | **None** | Not in SLT scope |
| CHECK / DEFAULT constraints | **None** | Not in SLT scope |
| Transactions (BEGIN/COMMIT/ROLLBACK) | **None** | Not in SLT scope |
| ALTER TABLE | **None** | Not in SLT scope |
| Triggers | **None** | Intentionally unsupported |

---

## Gap analysis

### What matters for ETL-SQL use cases

ETL-SQL is a scripting engine for data movement and transformation, not an OLTP application database. The majority of user scripts are SELECT-heavy pipelines. Evaluated through that lens:

**DML gap closed**: `dml.test`, `insert.test`, and `merge.test` were added, bringing UPDATE, DELETE, INSERT SELECT, and MERGE to **Medium** confidence. The remaining gaps are intentionally out of scope for an ETL scripting engine:

- Constraint enforcement (PRIMARY KEY, FOREIGN KEY, CHECK) — ETL-SQL processes data, it doesn't enforce application schema rules
- Transactions — ETL-SQL pipelines are single-session scripts, not concurrent OLTP workloads
- ALTER TABLE — ETL scripts create/drop/read tables; in-flight column changes are not an ETL pattern
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
