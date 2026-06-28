# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.13.0 Enterprise Hardening)

The stabilization, performance, and service-account work is complete. Enterprise hardening remains
opt-in so standalone installations retain their existing local behavior.

- [x] **Phase 2: Service Accounts**
  - [x] Define the service-account security contract: immutable identity, owner, enabled/expiry state, explicit scopes, role/resource authorization interaction, and audit actor representation.
  - [x] Add provider-neutral service-account persistence and SQLite/PostgreSQL migrations; store only protected or one-way-derived client secrets and never return a secret after initial creation or rotation.
  - [x] Add admin APIs to create, list, rotate, disable, and revoke service accounts with one-time secret display, validation, least-privilege defaults, and atomic audit records.
  - [x] Add a client-credentials token endpoint that authenticates service accounts, emits short-lived JWTs with service identity and scope claims, and fails closed for disabled, expired, revoked, or malformed credentials.
  - [x] Enforce explicit scopes on supported API and scheduled-execution operations while retaining existing role and resource permission checks; reject service identities from interactive-only and human-administration flows.
  - [x] Attribute scheduled CLI/API executions and audit events to the service account, including correlation ID and effective scopes, without exposing credentials in logs, diagnostics, exports, or support bundles.
  - [x] Add unit, SQLite, PostgreSQL, HTTP integration, rotation/revocation, concurrency, and credential-redaction coverage.
  - [x] Document provisioning, scope selection, secret rotation, revocation, unattended CLI/API authentication, and migration/backup behavior.

## Enterprise Policy & Security Monitoring

Enterprise controls remain opt-in: an unenrolled standalone installation retains its existing local
configuration behavior and does not require network connectivity.

- [x] **Phase 1: Enterprise Enrollment Bootstrap**
  - [x] Define a versioned machine enrollment contract for tenant, HTTPS policy endpoint, signing trust key, machine identity, policy-cache lifetime, and fail-closed behavior.
  - [x] Store enrollment outside ordinary application configuration with administrator-owned Windows/Unix permissions and reject broadly writable or malformed enrollment state.
  - [x] Add administrator CLI commands to enroll, inspect status, and unenroll with explicit confirmation and useful non-elevated diagnostics.
  - [x] Detect enrollment before normal application startup so JSON, environment variables, command-line arguments, and scripts cannot disable it; leave standalone behavior unchanged when enrollment is absent.
  - [x] Add tamper, permissions, invalid endpoint/key, startup, standalone-regression, and cross-platform storage tests.
  - [x] Document enrollment, service identity access, recovery, unenrollment, and OS application-control boundaries.
- [x] **Phase 2: Authoritative Policy Runtime**
  - [x] Define and verify tenant-bound, versioned RSA-PSS SHA-256 policy envelopes with issuance, expiry, and schema validation.
  - [x] Retrieve policy over HTTPS with enrollment/machine identity headers and optional client-certificate authentication.
  - [x] Persist only verified envelopes in a separate OS-protected cache; re-verify signatures, reject rollback, and enforce envelope/offline expiry.
  - [x] Apply verified policy after JSON, environment, command-line, and deployment overrides while preserving standalone behavior when unenrolled.
  - [x] Expose redacted effective-policy status, source, version, issuance/expiry, warnings, and governed key names through `enterprise status`.
  - [x] Add signature, tamper, tenant, expiry, rollback, cache, HTTP contract, fail-closed/fail-open, and precedence coverage.
  - [x] Document the policy endpoint, signing contract, client authentication, cache behavior, precedence, diagnostics, and enforcement boundary.
- [ ] **Phase 3: Operation-Boundary Enforcement** — filesystem, network, connector, process, Docker, resource, and script-override controls.
- [ ] **Phase 4: Central Security Events** — structured security events, durable SIEM delivery, backpressure, and optional fail-closed monitoring.
- [ ] **Phase 5: Certification & Operations** — platform certification, outage/tamper drills, upgrades, recovery, and administrator runbooks.

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
- [x] **LATERAL JOIN (alias for CROSS APPLY / OUTER APPLY)** — recognize `LATERAL` so correlated table expressions read in standard ANSI/DuckDB syntax. *(Done: `LATERAL` token; parser maps `[CROSS]/INNER JOIN LATERAL` + `, LATERAL` → CROSS APPLY and `LEFT JOIN LATERAL` → OUTER APPLY, carrying any explicit `ON`; `JoinEngine` APPLY path now honors a non-`true` predicate; ToSql renders it. Tests in `StmtLateralJoinTests`; Grammar §5.6 + `Help/Keywords/LATERAL.md` + Syntax_Index.)*
  - Lexer: add `LATERAL` token.
  - Parser: `[CROSS] JOIN LATERAL <expr>` → CROSS APPLY semantics; `LEFT [OUTER] JOIN LATERAL <expr> ON true` → OUTER APPLY; also the `, LATERAL (...)` comma form.
  - Carry through any explicit `ON <predicate>` (do not assume `ON true`) — LATERAL permits an arbitrary join condition that APPLY does not express.
  - Reuse existing `JoinClause.IsApply` / `JoinEngine`; no new execution path expected.
  - Tests + docs.
- [x] **`EXCLUDE` column selector (`SELECT * EXCLUDE (col1, col2)`)** — project all columns except the listed ones. *(Done as part of the `StarExpression` star-modifier work; expanded in `QueryMetadataHelper.ExpandColumns`. Tests in `StmtTier1SyntaxTests`; Grammar §5.1.1 + `SELECT_MODIFIERS.md` + Syntax_Index.)*
- [x] **DuckDB-style PIVOT / UNPIVOT** — add the cleaner DuckDB grammar alongside the existing MSSQL/Oracle `PIVOT`/`UNPIVOT` (keep the old syntax fully working). *(Done: statement forms `PIVOT src ON … [IN …] USING … [GROUP BY …]` and `UNPIVOT src ON … INTO NAME … VALUE …` desugar to `SELECT *` over an operator. New `DuckPivotClause` (multi-col, multi-agg, dynamic discovery via FILTER-ed aggregates) + extended `UnpivotClause` for `COLUMNS(* EXCLUDE (…))`. Existing MSSQL pivot untouched. Tests in `StmtDuckPivotTests`; Grammar §5.8 + PIVOT.md + Syntax_Index. NOTE: a general `SELECT * EXCLUDE` selector remains its own Tier-1 item.)*
  - `PIVOT tbl ON <cols> USING <agg> [GROUP BY <cols>]`, with dynamic value discovery when the `ON` list is not enumerated.
  - `UNPIVOT tbl ON <cols | COLUMNS(* EXCLUDE (id, dept))> INTO NAME <name_col> VALUE <value_col>`.
  - Depends on the `COLUMNS(* EXCLUDE (...))` selector (reuse the `EXCLUDE` work above).
  - Engine: branch/extend `PivotEngine`; dynamic pivot needs a value-collection pass over the source.
  - Tests + docs (new examples; clarify which dialect each syntax mirrors).
- [x] **ASOF JOIN** — nearest-match temporal/inequality join (e.g. `ASOF [LEFT] JOIN b ON a.id = b.id AND a.ts >= b.ts`) returning the closest qualifying row. *(Done: `ASOF` token; parser maps `ASOF [LEFT] JOIN` → join type; `JoinEngine.PerformAsofJoin` shared by both buffered and streaming paths picks the closest qualifying right row via the one inequality (`>=`/`>` → max, `<=`/`<` → min) after equality keys, using a combined schema so qualified columns resolve. Tests in `StmtAsofJoinTests`; Grammar §5.6.1 + `Help/Keywords/ASOF_JOIN.md` + Syntax_Index. NOTE: O(left×right), right side buffered — sort/merge + spill optimization is a follow-up.)*
  - Engine: new sorted/merge-style matching path; consider spill behavior for large inputs.
- [x] **Window RANGE / GROUPS frames — verify & document** — *(Verified GROUPS peer semantics and default RANGE already worked; **implemented** value-based numeric RANGE offsets in `WindowEngine.ResolveFrameRange` (previously fell back to whole partition). Tests in `StmtWindowFrameVerifyTests`; Grammar §5.11 frame table documents support + the date/interval-RANGE fallback to full partition.)*
- [x] **SELECT * REPLACE / RENAME** — `SELECT * REPLACE (expr AS col)` and `SELECT * RENAME (col AS new)`. *(Done: `StarExpression` AST node carries EXCLUDE/REPLACE/RENAME (order enforced); expanded in `ExpandColumns`; pushdown skips star modifiers. Tests in `StmtTier1SyntaxTests`; Grammar §5.1.1 + `SELECT_MODIFIERS.md` + Syntax_Index.)*
- [x] **ORDER BY ALL** — order by every output column left-to-right; companion to GROUP BY ALL. *(Done: `OrderByAll`/`OrderByAllDescending` flags parsed; expanded to per-column `OrderByClause` after column expansion in `EvaluateSelect`; pushdown skips it. Tests in `StmtTier1SyntaxTests`; Grammar §5.1.1 + `SELECT_MODIFIERS.md` + Syntax_Index.)*
- [x] **Trailing commas** — tolerate an optional trailing comma in SELECT, GROUP BY, ORDER BY, and function-argument lists. *(Done via `AtClauseEnd()` guard + RPAREN check in arg loop. Tests in `StmtTier1SyntaxTests`; Grammar §5.1.1. NOTE: VALUES lists not yet covered.)*
- [x] **Underscore digit separators (`1_000_000`)** — lexer accepts `_` between digits, stripped from the value. *(Done in `Lexer.ReadNumber`. Tests in `StmtTier1SyntaxTests`; Grammar §5.1.1.)*
- [x] **`count()` shorthand** — treat `count()` as `count(*)`. *(Done: zero-arg COUNT normalized to COUNT(*) in `ExpressionParser`. Tests in `StmtTier1SyntaxTests`; Grammar §5.1.1.)*
- [x] **UNION [ALL] BY NAME** — set union that aligns inputs by column name instead of position, filling absent columns with NULL. *(Done: `ByName` flag parsed on `UNION`/`UNION ALL`; `SetOperationEngine` name-aligns to union of column names. Tests in `StmtSetOpExtensionsTests`; Grammar §7 + Syntax_Index.)*
- [x] **Idempotent DDL/DML (OR REPLACE / OR IGNORE / BY NAME)** — *(Done/verified: `CREATE OR REPLACE TABLE|VIEW` implemented (`CreateOrReplace` mode; SchemaManager drops the existing relational table first, temp tables overwrite; views already replace) — tests in `StmtCreateOrReplaceTests`. `INSERT OR REPLACE` already parses and sets `ReplaceOnConflict`. INSERT already aligns source→target **by column name** (`BatchPipelineHelper.MapRow` is name-first with positional fallback), so the `BY NAME` behavior is the default. Grammar §10.1 + Syntax_Index. NOTE: `INSERT OR IGNORE` and an explicit `BY NAME` keyword are NOT added — `OR IGNORE` needs PK/unique conflict (upsert) semantics, a larger engine feature deferred to ROADMAP.)*
- [ ] **Reusable & lateral column aliases** — reference a SELECT alias in WHERE/GROUP BY/HAVING/QUALIFY and in later SELECT expressions (lateral column alias).
  - Binder/scope change: resolve select aliases before clause evaluation; guard ambiguity against real columns; tests; docs.
- [x] **SAMPLE** — `... USING SAMPLE n PERCENT | n% | n ROWS [REPEATABLE (seed)]`. *(Done: `SampleClause` parsed at the select tail; `SelectStatementHandler.ApplySample` does Bernoulli per-row for PERCENT and reservoir sampling for ROWS; seed makes it repeatable; pushdown skips it. Tests in `StmtSampleTests`; Grammar §5.1.1 + Syntax_Index. NOTE: `TABLESAMPLE`-keyword spelling not added — `USING SAMPLE` only.)*
- [x] **General `COLUMNS()` expression** — `COLUMNS(*)`, `COLUMNS(* EXCLUDE (...))`, and `COLUMNS('regex')` in the projection. *(Done by extending `StarExpression` with a regex `Pattern` (reusing the EXCLUDE/REPLACE/RENAME machinery) and parsing `COLUMNS(...)` in the select list; expanded in `ExpandColumns`. Tests in `StmtColumnsExprTests`; Grammar §5.1.1 + Syntax_Index. NOTE: column-selection form only — the lambda/function-broadcast form `func(COLUMNS(*))` is not supported.)*
- [x] **UNNEST / FLATTEN** — expand a LIST/array value into rows. *(Done as table-valued functions reusing the FROM/CROSS APPLY table-function path: `UNNEST(list)`/`FLATTEN(list)` return a one-column `Value` table; use `FROM UNNEST(...)` or `CROSS APPLY UNNEST(t.col)`. Tests in `StmtUnnestTests`; Grammar §5.6 + `Help/Keywords/UNNEST.md` + Syntax_Index. NOTE: table-valued form (not bare `SELECT UNNEST(col)`); single-element `[x]` literal parses as a quoted identifier.)*
- [x] **Minor conveniences** — `LIKE ANY (...)` / `LIKE ALL (...)`, `MINUS` as an alias for `EXCEPT`, and `DESCRIBE <table>` schema summary. *(Done: LIKE ANY/ALL desugar to OR/AND of LIKE (NOT wraps the group); MINUS parses as EXCEPT (reserved set-word so it isn't swallowed as an alias); DESCRIBE maps to `ShowColumnsStatement`. Tests in `StmtMinorConveniencesTests`/`StmtSetOpExtensionsTests`; Grammar §7/§8 + Syntax_Index. NOTE: DESCRIBE covers tables; `DESCRIBE <query>` not yet.)*
