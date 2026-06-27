# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.13.0 Service Accounts)

The stabilization and performance work is complete. The final sprint phase adds narrowly scoped,
non-interactive identities without starting the deferred stewardship, debugger, or approval-workflow work.

- [ ] **Phase 2: Service Accounts**
  - [ ] Define the service-account security contract: immutable identity, owner, enabled/expiry state, explicit scopes, role/resource authorization interaction, and audit actor representation.
  - [ ] Add provider-neutral service-account persistence and SQLite/PostgreSQL migrations; store only protected or one-way-derived client secrets and never return a secret after initial creation or rotation.
  - [ ] Add admin APIs to create, list, rotate, disable, and revoke service accounts with one-time secret display, validation, least-privilege defaults, and atomic audit records.
  - [ ] Add a client-credentials token endpoint that authenticates service accounts, emits short-lived JWTs with service identity and scope claims, and fails closed for disabled, expired, revoked, or malformed credentials.
  - [ ] Enforce explicit scopes on supported API and scheduled-execution operations while retaining existing role and resource permission checks; reject service identities from interactive-only and human-administration flows.
  - [ ] Attribute scheduled CLI/API executions and audit events to the service account, including correlation ID and effective scopes, without exposing credentials in logs, diagnostics, exports, or support bundles.
  - [ ] Add unit, SQLite, PostgreSQL, HTTP integration, rotation/revocation, concurrency, and credential-redaction coverage.
  - [ ] Document provisioning, scope selection, secret rotation, revocation, unattended CLI/API authentication, and migration/backup behavior.

## Language Parity (DuckDB-inspired syntax)

Modern, ergonomic syntax extensions inspired by DuckDB. Each item ships with parser support, engine
execution, tests, and complete documentation (`Docs/Reference/Grammar.md`, `Docs/Syntax_Index.md`, and
the relevant reference/help pages **and the in-app help under `src/ETL-SQL.Core/Resources/Help/`**).
Documentation is a completion requirement, not a follow-up. Existing MSSQL/Oracle-style syntax stays working — these are additive.

- [x] **IS [NOT] DISTINCT FROM** — null-safe equality so `a IS DISTINCT FROM b` / `a IS NOT DISTINCT FROM b` treat NULL as an ordinary value (`NULL IS NOT DISTINCT FROM NULL` = true; `NULL IS DISTINCT FROM 1` = true). *(Done: `IsDistinctFromExpression` node, parser, evaluator reusing `IsSoftEqual`, all AST-walker sites, ToSql; `StmtIsDistinctFromTests`; Grammar §14.3 + `Help/Keywords/IS_DISTINCT_FROM.md` + Syntax_Index.)*
  - Parser: add the operator to expression parsing with precedence alongside `IS NULL`/comparisons.
  - Engine: null-safe comparison in `ExpressionEvaluator` (bypass three-valued-logic NULL propagation).
  - Tests + docs: comparison-operator reference and Grammar.
- [x] **GROUP BY positional (`GROUP BY 1, 2`)** — integer literals reference the Nth SELECT item instead of grouping by a constant (current behavior parses them as constant expressions). *(Done: `ResolvePositionalReference` in the parser resolves GROUP BY and ORDER BY ordinals against the SELECT list; rejects out-of-range and `*`-list positions; `1 + 1` stays an expression. Tests in `StmtGroupByExtensionsTests`; Grammar §5.7 + Syntax_Index.)*
- [x] **GROUP BY ALL** — group by every non-aggregated expression in the SELECT list automatically. *(Done: `GroupByAll` AST flag parsed; `SelectStatementHandler.EvaluateSelect` expands it to all non-aggregate/non-window SELECT expressions before pushdown/routing; lint + ToSql aware. Tests in `StmtGroupByExtensionsTests`; Grammar §5.7 + `Help/Keywords/GROUP_BY_ALL.md` + Syntax_Index.)*
- [ ] **LATERAL JOIN (alias for CROSS APPLY / OUTER APPLY)** — recognize `LATERAL` so correlated table expressions read in standard ANSI/DuckDB syntax.
  - Lexer: add `LATERAL` token.
  - Parser: `[CROSS] JOIN LATERAL <expr>` → CROSS APPLY semantics; `LEFT [OUTER] JOIN LATERAL <expr> ON true` → OUTER APPLY; also the `, LATERAL (...)` comma form.
  - Carry through any explicit `ON <predicate>` (do not assume `ON true`) — LATERAL permits an arbitrary join condition that APPLY does not express.
  - Reuse existing `JoinClause.IsApply` / `JoinEngine`; no new execution path expected.
  - Tests + docs.
- [ ] **`EXCLUDE` column selector (`SELECT * EXCLUDE (col1, col2)`)** — project all columns except the listed ones (also accept the single-column `EXCLUDE col` form).
  - Parser/AST: extend the star projection with an exclude list.
  - Engine: drop excluded columns at projection time after schema resolution.
  - Build this first — it is the shared primitive for the generic UNPIVOT below.
  - Tests + docs.
- [ ] **DuckDB-style PIVOT / UNPIVOT** — add the cleaner DuckDB grammar alongside the existing MSSQL/Oracle `PIVOT`/`UNPIVOT` (keep the old syntax fully working).
  - `PIVOT tbl ON <cols> USING <agg> [GROUP BY <cols>]`, with dynamic value discovery when the `ON` list is not enumerated.
  - `UNPIVOT tbl ON <cols | COLUMNS(* EXCLUDE (id, dept))> INTO NAME <name_col> VALUE <value_col>`.
  - Depends on the `COLUMNS(* EXCLUDE (...))` selector (reuse the `EXCLUDE` work above).
  - Engine: branch/extend `PivotEngine`; dynamic pivot needs a value-collection pass over the source.
  - Tests + docs (new examples; clarify which dialect each syntax mirrors).
- [ ] **ASOF JOIN** — nearest-match temporal/inequality join (e.g. `ASOF [LEFT] JOIN b ON a.id = b.id AND a.ts >= b.ts`) returning the closest qualifying row.
  - Lexer/parser: `ASOF` join type with one inequality predicate plus optional equality keys.
  - Engine: new sorted/merge-style matching path; consider spill behavior for large inputs.
  - Tests + docs (time-series examples).
- [ ] **Window RANGE / GROUPS frames — verify & document** — frame parsing and in-memory execution already exist for ROWS/RANGE/GROUPS (`ExpressionParser`, `WindowEngine`); confirm completeness rather than build from scratch.
  - Verify value-based RANGE offsets, numeric and date/interval (e.g. `RANGE BETWEEN INTERVAL '5 days' PRECEDING AND CURRENT ROW`).
  - Verify GROUPS peer semantics and frame `EXCLUDE` options.
  - Document supported frame shapes and the large-partition spill fallback (cross-link the external window spill work).
  - Add/round out tests for value-based RANGE and GROUPS.
- [ ] **SELECT * REPLACE / RENAME** — `SELECT * REPLACE (expr AS col)` to substitute a column's value within the star, and `SELECT * RENAME (col AS new)` to rename. Share the star-modifier path with `EXCLUDE`; clause order is `EXCLUDE` → `REPLACE` → `RENAME`.
  - Parser/AST star-modifier list, engine projection rewrite, tests.
  - Docs: Grammar, Syntax_Index, and the `SELECT` help page (`Resources/Help/Keywords/SELECT.md`).
- [ ] **ORDER BY ALL** — order by every output column left-to-right; companion to GROUP BY ALL.
  - Parser + binder expansion; tests; docs + help.
- [ ] **Trailing commas** — tolerate an optional trailing comma after the final item in SELECT, GROUP BY, ORDER BY, function-argument, and VALUES lists.
  - Parser tolerance only; tests; Grammar note.
- [ ] **Underscore digit separators (`1_000_000`)** — lexer accepts `_` between digits in integer/decimal literals.
  - Lexer; tests; Grammar note.
- [ ] **`count()` shorthand** — treat `count()` as `count(*)`.
  - Parser/function binding; tests; update `Resources/Help/Functions/COUNT.md`.
- [ ] **UNION [ALL] BY NAME** — set union that aligns inputs by column name instead of position, filling absent columns with NULL.
  - Parser (`BY NAME` modifier on set ops), `SetOperationEngine` name-alignment path; tests; docs + `Resources/Help/Keywords/UNION.md`.
- [ ] **Idempotent DDL/DML (OR REPLACE / OR IGNORE / BY NAME)** — `CREATE OR REPLACE TABLE|VIEW`, `INSERT OR IGNORE`, `INSERT OR REPLACE`, and `INSERT INTO ... BY NAME` (confirmed missing in the language parser today).
  - Parser + CREATE/INSERT handlers; conflict semantics for replace/ignore; tests; docs + help for CREATE and INSERT.
- [ ] **Reusable & lateral column aliases** — reference a SELECT alias in WHERE/GROUP BY/HAVING/QUALIFY and in later SELECT expressions (lateral column alias).
  - Binder/scope change: resolve select aliases before clause evaluation; guard ambiguity against real columns; tests; docs.
- [ ] **SAMPLE / TABLESAMPLE** — return a random subset, e.g. `... USING SAMPLE 10%` / `TABLESAMPLE (n ROWS | p PERCENT)` with an optional deterministic seed.
  - Parser + sampling operator in the select pipeline; tests; docs + help.
- [ ] **General `COLUMNS()` expression** — apply an expression/pattern across many columns, e.g. `COLUMNS(* EXCLUDE (id))`, `COLUMNS('regex')`. Superset of the `COLUMNS(* EXCLUDE)` selector slated for UNPIVOT — build the selector once and reuse it in SELECT, PIVOT, and UNPIVOT.
  - Parser/AST COLUMNS selector; projection expansion; tests; docs.
- [ ] **UNNEST / FLATTEN** — expand a LIST/array value into rows (`SELECT UNNEST(list_col)`); the table-producing complement to LISTAGG. (LIST type exists; STRUCT/MAP types are intentionally out of scope, and lambda-based list comprehensions are deferred to ROADMAP.)
  - Parser + engine row-expansion; interaction with sibling select items; tests; docs + help.
- [ ] **Minor conveniences** — `LIKE ANY (...)` / `LIKE ALL (...)`, `MINUS` as an alias for `EXCEPT`, and `DESCRIBE <table|query>` schema summary (all confirmed missing; each small and self-contained).
  - Parser + small engine/eval additions; tests; docs + help where a keyword page applies.
