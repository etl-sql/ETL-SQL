## ETL-SQL v0.12.0

This release adds durable job-scoped state for incremental loads, spec-backed schema contracts, certified OpenID Connect authentication for the Report Portal, and a round of pushdown/evaluator performance work.

### Highlights

**Job-Scoped State Persistence & Incremental Watermarking**
- New `GET_JOB_STATE(key)` / `SET_JOB_STATE(key, value)` primitives for scheduled and ad-hoc incremental loads.
- State updates are buffered during execution and committed atomically to the orchestrator store (SQLite or PostgreSQL) only on successful script completion.
- Developer CLI fallback persists state in local `[script_name].etlstate` JSON files.

**JSON/Spec-Backed Schema Contract Checks**
- `EXPECT SCHEMA target FROM 'path/to/spec.json' [ON DRIFT WARN];` validates against a reviewed JSON contract.
- Verifies column presence, type-family matching, nullability, string-length limits, and decimal precision/scale, honoring `context.ResolvePath()`.

**Certified OpenID Connect (OIDC) Authentication**
- Federated login, logout, and token refresh in the Report Portal with external Identity Provider support.
- User accounts are keyed to the immutable OIDC `sub` claim to prevent takeover if usernames/emails are reassigned.
- Dynamic group mapping syncs IdP role/group claims to local portal groups at login.
- Redacted configuration diagnostics let provider availability be monitored without exposing client secrets.
- Recovery scenarios (IdP outages, JWKS rotation, claim changes, token revocation) covered by an integration suite.

**VS Code Extension Enhancements**
- Cleaned up ESLint static analysis and type declarations across the TypeScript sources.
- Stabilized the extension integration test suite by tuning Mocha bootstrap timeouts for headless activation.

### Performance

- **Pushdown aggregation for staged extracts** — `SELECT ... INTO #temp` with `GROUP BY`, aggregates, `DISTINCT`, and compatible joins now pushes aggregation to the source and streams only grouped/filtered results back.
- **Cross-connection semi-join pushdown** — joins between small local temp tables (1–1000 rows) and large remote tables are rewritten to push a parameterized key filter (`IN`) to the remote query, avoiding full-table loads. Visible as `[SEMI-JOIN PUSHDOWN ON ...]`.
- **Evaluator hot paths** — allocation-free `Row.TryGetValue` resolution and unified `TryResolveIdentifier` lookups cut heap allocation during streaming execution.

### Fixed

- Stabilized two timing-sensitive Docker integration-lane tests that flaked only under full pre-release load (Retry-After delay tolerance and orchestrator job-history poll timeout).

### Install

Download the platform asset below:
- **Windows** — `ETL-SQL-v0.12.0-x64-Setup.msi` (or the win-x64 ZIP)
- **Linux** — `etl-sql_0.12.0_amd64.deb` (or the linux-x64 ZIP)
- **macOS** — osx-arm64 ZIP (Intel x64 ZIP/DMG best-effort)
- **VS Code** — the bundled `.vsix`

Full details in [CHANGELOG.md](https://github.com/etl-sql/ETL-SQL/blob/v0.12.0/CHANGELOG.md).
