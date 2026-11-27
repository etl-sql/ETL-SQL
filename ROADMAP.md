# ETL-SQL Product Roadmap

This document describes future product outcomes and their sequencing. Detailed architecture belongs
in the linked decisions and architecture documents, executable release work belongs in `TODO.md`,
and shipped outcomes belong in `CHANGELOG.md` and the release notes under `docs/releases/`.

The stable deployment-profile topology is defined in
[`docs/architecture/DeploymentProfiles.md`](docs/architecture/deployment-profiles.md). The Enterprise
operating model and trust hierarchy are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/enterprise-platform-strategy.md).

## Roadmap Authoring Contract

Before adding or revising an entry:

1. **Verify current reality.** Search source, tests, `TODO.md`, `CHANGELOG.md`, release notes, and
   authoritative architecture documents. Do not describe an implemented capability as future work.
2. **Separate outcome from mechanism.** State the user or operator outcome first. Treat libraries,
   providers, protocols, performance figures, and implementation shapes as candidates unless an ADR
   has accepted them.
3. **Declare maturity and horizon.** Every entry must use one status and one horizon from the
   vocabularies below.
4. **Keep one coherent initiative per entry.** Split work when parts have different dependencies,
   security boundaries, delivery horizons, or independent user value.
5. **Name boundaries and dependencies.** State what the initiative intentionally excludes and which
   contracts or earlier initiatives it requires.
6. **Deliver vertical slices.** Stages must produce independently testable product capability, not
   merely layers of internal plumbing or an exhaustive all-at-once migration.
7. **Make claims testable.** Exact latency, size, parity, coverage, or visual-count claims require a
   defined fixture and verification method. Otherwise describe them as targets to measure.
8. **Preserve sources of truth.** Detailed design belongs in an ADR or architecture document;
   active executable tasks belong in `TODO.md`; deferred requirements stay with their roadmap entry
   until picked up; shipped outcomes belong in `CHANGELOG.md` and release notes.
9. **Retire completed work.** When the promised outcome ships and is verified, remove the roadmap
   entry or rewrite it to describe only the genuinely remaining increment.
10. **Respect product boundaries.** Roadmap work must preserve script-first authoring, transparent
    transformation, portability, lineage, Zero-Trust execution, deployment profiles, and the
    third-party dependency policy.

### Status vocabulary

- **Exploring** — The problem is plausible, but product or architecture decisions remain open.
- **Accepted** — The outcome and governing architecture are accepted, but work is not scheduled.
- **Planned** — Delivery is selected for an upcoming milestone and decomposed in `TODO.md`.
- **In Progress** — Implementation is active; the entry states only remaining scope and risks.
- **Incremental** — A useful baseline has shipped; the entry describes the next independently
  valuable expansion.

### Horizon vocabulary

- **Foundation** — Required before dependent product work can safely proceed.
- **Next** — A leading candidate for the next planning horizon.
- **Later** — Valuable but not currently a dependency or launch gate.
- **Launch Gate** — Required before a named deployment profile or hosted service can make its
  production claim.

### Required entry shape

Each entry states its status, horizon, authoritative design, problem and intended outcome, why the
horizon is appropriate, boundaries, dependencies, vertical delivery slices, and acceptance evidence.
Keep entries concise and link to detailed designs rather than copying them here.

### Sprint planning for backlog tables

Select task IDs, not an entire family. Move only selected tasks into
`TODO.md`, keeping their dependencies and finish lines. **S** targets 1–2 focused developer-days;
**M** targets 3–5; **D** is a 1–2 day decision or measurement task whose output is a bounded proposal
and an implementation estimate. These are initial sizing targets, not measured delivery promises.
Re-estimate against the fixture and current source before committing a sprint. Split anything that
still exceeds five engineering days. Environment provisioning, soak duration, external access and
approval waits must be recorded separately from engineering effort. No task closes merely because
its test exists; the stated behavior must pass. Any defect found outside its scope becomes an
explicit task with its own estimate.

Tables below are open backlog tasks. Dependencies name prerequisite IDs; an em dash means only the
entry's existing foundations are required. Completed neighboring capabilities remain in the
implemented baseline and are not work to redo.

A task is ready for a sprint only when its fixture, prerequisite IDs, required environment/access
and revised estimate are recorded. Decision tasks may finish by retaining an unsupported outcome;
that does not schedule their proposed implementation.

---

## Foundation and Next Horizon

### Release Engineering — Routine, GitHub-Built Releases

**Status:** Incremental
**Horizon:** Later
**Authoritative design:** [Release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md).

**Problem and intended outcome:** Keep the GitHub-built release process reliable and reduce the
time it takes to find and recover from a failed check.

**Remaining work:** The fast pre-push follow-up is tracked as RG03 in [TODO.md](TODO.md).
Open VSX publishing is deferred. The older timing, runner-capacity, watchdog, and rehearsal
improvements remain parked in the reliability plan until selected.

**Boundaries and dependencies:** Reuse the hosted release, installer, and Marketplace workflows.
Keep certification receipts tied to the candidate and verify changes to any publishing path
through its own rehearsal.

**Acceptance evidence:** Record the docs-only push timing in RG03. If Open VSX is selected,
verify one publication and a safe retry against the agreed extension bytes.

### Release Engineering — Enforced Merge Gates and a GitHub-First Contributor Workflow

**Status:** In Progress (v0.23.0: RG01–RG04 built, proofs left in `TODO.md` 1.2 and 2.1)  
**Horizon:** Next (v0.23.0)  
**Authoritative design:** none yet; follows the
[release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md) and its
"CI certifies, the workstation does not" rule.

**Problem and intended outcome:** A process review (Codex, 2026-11-15) found that releases are
gated, but merges are not. Release branches (`release/v0.23.0`) have no branch protection. `main`
requires 5 named checks (CodeQL C# and JS/TS, Build & Test, Build VS Code Extension, MSI In-Place
Upgrade) and omits browser contracts, Portal journeys and native package certification. Enterprise
Certification was dropped from that list on 2026-11-16: its matrix job is skipped on PRs before it
expands, so it never reported under its required names and every PR needed an admin bypass. The
required review was also set to 0 that day, because the only code owner is the author of every PR;
restore it when a second maintainer gets write access. Engine Coverage also
skips on PRs, so an ordinary new unit test outside Engine Smoke and the selected certification
suites may first run after merge. Contributor docs disagree with the workflow: `CONTRIBUTING.md`
omits Node 22 and browser-asset preparation (a clean-clone build fails) and describes an obsolete
formatting hook, the PR template asks for an unrestricted `dotnet test`, and the pre-push hook
still needs the full toolchain even for documentation changes. Dependabot covers the extension and
UI npm projects but not `scripts/typecheck`, `scripts/lint`, `scripts/codemirror` or `scripts/vsix`,
and third-party Actions are pinned by tag, not commit SHA. The outcome: one always-running gate
decides every merge, PRs run the tests their changes affect, and local tooling is light and optional.

**Why next:** none of this blocks v0.22.0: Release requires exact-candidate CI, CodeQL and Release
Evidence whatever was merged. It does let a defect merge and surface only on the candidate, which
costs a release restart.

**Boundaries:**
- One required aggregate check per branch, not a growing list of job names to keep in sync.
- Skipped is not passed: the gate fails when a lane its changes select did not run.
- Expensive scale, soak and installer certification keep their own schedules (CI push, Release
  Evidence, nightly); the PR lane covers deterministic tests only.
- Local hooks stay seconds-fast and optional; GitHub is the authoritative gate.

**Dependencies:** `ci.yml` and `ci-impact.json` lane selection, `select-ci-lanes.mjs`,
`hosted-release-policy.json`, `.github/dependabot.yml`, the pre-push hook (`Test-PrePush.ps1`),
`CONTRIBUTING.md`, `AGENTS.md` and the PR template.

Selected for v0.23.0 (owner, 2026-11-16): these task rows moved to [`TODO.md`](TODO.md).

**Acceptance evidence:** a PR that skips a selected lane or adds a failing ordinary test is
blocked by the single required check on `main` and `release/*`; a clean clone builds by following
`CONTRIBUTING.md`; Dependabot opens update PRs for all six npm projects and the pinned Actions.

### Windows Installer — The MSI Copies Files and Registers Services Only

**Status:** Accepted (slice 5 deferred; slices 1–4 shipped in v0.23.0 and the acceptance evidence is met)  
**Horizon:** Later (revisit only if MSI runner losses return)  
**Authoritative design:** [Release reliability plan](docs/architecture/roadmaps/release-reliability-plan.md)
section 10 (runner-loss evidence and the 2026-11-23 close).

**Shipped in v0.23.0 (`MI01`–`MI04`, see `CHANGELOG.md`):** the MSI only copies files and
registers, starts and stops the two services; the services create and migrate their own
configuration; no script custom actions and no `CloseApplication`; `ETL-SQL admin restore
--in-place` for rollback and `ETL-SQL admin purge-data` for clean data. The Portal-profile
certification (fresh, upgrade, rollback) passed on its first attempt on five nightly runs.

**Deferred slice 5 (`MI05`):** run the MSI certification inside a Hyper-V-isolated Windows Server
Core container that the runner only drives (host supervisor, `docker run --isolation=hyperv` child
on `windows-2022`). Proven feasible on branch `diag/nested-virt-probe` (run 42418358058, 7 of 7);
`windows-latest` (Server 2025) runs no Docker engine. No runner loss since the 2026-11-08 Restart
Manager fixes, so the gain no longer pays for the cost (pinned `windows-2022`, a less realistic
install). Pick it up again if a real MSI runner loss recurs.

### File Operations — Managing Remote File Trees over SFTP, FTP, and Cloud Connections

**Status:** In Progress (RT03–RT09 shipped in v0.23.0; RT11 decision proposal awaiting owner approval)

**Horizon:** Later  
**Authoritative design:** none yet; [SFTP](docs/reference/connectors/services/sftp.md),
[FTP](docs/reference/connectors/services/ftp.md), and [TRANSFER](docs/reference/file-operations/transfer.md)
are the references. [`IRemoteFileSystem`](src/ETL-SQL.Core/Data/IRemoteFileSystem.cs) is the contract.

**Problem and intended outcome:** Checked against source on 2026-11-13. `SFTP`, `FTP`/`FTPS`, `S3`,
`AZURE_BLOB`, and `SHAREPOINT` implement `IRemoteFileSystem` (list, upload, download, delete, exists,
rename, create and delete directory). `SEND FILE`/`RECEIVE FILE` take `*` and `?` wildcards, and
`DELETE`, `MOVE`, `RENAME` work with `AT conn`. That covers sending and fetching a file. It does not
cover managing a remote tree: `COPY FILE ... AT conn` and every remote directory operation except
create and delete fail with "not supported on remote systems", `SYNC DIRECTORY` has no remote handling,
wildcard receive does not descend into subfolders, and listings carry only name, path, size, modified
time, and a directory flag. The outcome: a script can copy, mirror, and verify a remote tree with the
same statements it uses locally, and the connector pages describe the options the code reads.

**Why this horizon:** The FTP documentation, `USE_SSL`, timeout, TLS-certificate and rename
defects shipped as fixes in v0.22.0. What is left is feature work with working alternatives
(download and re-upload, `FOREACH` over a listing).

**Boundaries:**
- No new protocol or library. FluentFTP and SSH.NET provide what is needed; a new third-party
  dependency follows the third-party dependency policy.
- Every remote operation counts toward the file-operation limit and honors `SET WHAT_IF ON`, as the
  local operations do.
- No remote command execution (SSH shell or `exec`). It is a different security boundary and needs
  its own decision.
- Metadata a server does not report returns `NULL`, not an error.
- Each statement and function change ships with its reference page, following the
  [documentation standard and templates](docs/templates/statement-reference-template.md).

**Dependencies:** none. `REMOTE_FILE_SIZE`, `REMOTE_FILE_MODIFIED` and `REMOTE_DIRECTORY_EXISTS`
(v0.22.0) read the same listing metadata RT05 extends.

**IDs:** this family was `RF` until 2026-11-23; it became `RT` so it does not clash with the v0.23.0 review follow-ups `RF01`–`RF05` in `CHANGELOG.md`. `RF10` in the v0.22.0 notes is the old name of a shipped task.

**Shipped in v0.23.0:** `RT03`–`RT09` (SFTP/FTP dot-entry filtering, remote `COPY FILE`, extended `FileMetaData`, recursive `REMOTE_FILE_LIST`/`RECEIVE FILE` and uniform non-empty `DELETE DIRECTORY`, remote `RENAME`/`MOVE`/`COPY DIRECTORY`, remote `SYNC DIRECTORY`, and `VERIFY = ON` transfer verification; resume of large downloads was recorded out of scope on 2026-11-24).

**Backlog (select IDs, not the family):**

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| RT11 | **Decision: remote command execution.** Whether a connection type that runs a single approved command over SSH belongs in the product, and under what allow-list and audit rules. Output is a proposal and an estimate only. | — | D |

**Decision proposal — RT11 (2026-11-23, awaiting owner decision):**
- *Verified current state:* `SftpConnector` already uses `SSH.NET` (`Renci.SshNet`), and `CREATE TOOL ... EXECUTABLE` (`ToolCatalog.cs`, `ExecuteToolStatementHandler.cs`) models allow-listed local execution with timeouts and path guards.
- *Risk & trust rules:* Arbitrary remote shell execution (`EXECUTE ssh BEGIN ... END`) conflicts with ETL-SQL's Zero-Trust model (shell injection, lateral movement, un-auditable side effects). If remote SSH command execution is ever shipped, it must enforce: (1) host-admin-only command templates in `appsettings.json` (template ID, fixed executable, typed argv slots, no `sh -c` shell interpolation); (2) mandatory pinned `HOST_KEY_FINGERPRINT` (no unpinned opt-out) and key-only auth; (3) hard timeout + bounded stdout/stderr capture; (4) `SET WHAT_IF ON` reporting template ID and redacted arguments without connecting; (5) structured audit logging (identity, host, template ID, exit code, duration); and (6) disabled in Shared SaaS / multi-tenant profiles.
- *Recommendation & estimate:* **Reject / defer** unless a concrete enterprise deployment cannot use SFTP + API/Webhook/remote scheduler. If approved later: **`M–L` (~3–4 days)**, zero new NuGet dependencies (reuses `SSH.NET`).

**Acceptance evidence:** An SFTP and an FTP listing contain only real entries, proven against a test server; `COPY FILE`
and the directory operations work with `AT conn` on SFTP, FTP, and one cloud connection; a remote
`SYNC DIRECTORY` plan under `WHAT_IF` matches what the real run does; and the transfer verification
option fails a truncated download.

### Database Connectors — Documentation Parity, Write-Path Clarity, and Credential Gaps

**Status:** In Progress. DB01–DB06, DB09, DB10, DB12, DB13, DB14, and DB15a–c shipped in v0.23.0.
DB07 was measured and not adopted (2026-11-24). DB08 and DB11 await an owner decision.

**Horizon:** Next (DB01–DB03), Later (the rest)  
**Authoritative design:** none yet; [Database connectors](docs/reference/connectors/databases/README.md)
are the references and [Connector Standards](docs/architecture/standards/connectors-standards.md)
govern option naming. The connector classes under `src/ETL-SQL.Connectors.Databases/` own the accepted
option lists.

**Problem and intended outcome:** Checked against source on 2026-11-13 (a review of the ten database
connector pages against each connector's option allowlist). The connectors work, but the pages
disagree with the code, and a developer cannot tell from the docs how each target writes data. The
page text describes options the code does not accept (`ROLE`, `PRIVATE_KEY_PASSPHRASE`, `SSL_CA`,
`WALLET_LOCATION`, `SID`, `READ_ONLY`, `BUSY_TIMEOUT_MS`, `KEY_FILE`, `ENCRYPTION`) and omits options it
does (`ACCOUNT`, `PORT`, `PROTOCOL`, `ENCRYPT`, `TIMEOUT_SECONDS`, and several ODBC keys). Bulk-write
paths exist in the code (`SqlBulkCopy` for MSSQL, array binding for Oracle, `MySqlBulkCopy` for MySQL)
but only MySQL documents one. The outcome: every connector page matches what the connector reads, a
developer can pick a write strategy per target from one place, and the credential gaps below are
either closed or recorded as decisions.

**Why this horizon:** DB01–DB03 are documentation defects, and following them wastes a developer's
time or silently ignores an option. The new capabilities have working alternatives (`ODBC`, staging
through `#temp`) and are Later.

**Boundaries:**
- No new native warehouse connector. Redshift, Synapse, Databricks, Trino, and Dremio stay on `ODBC`
  per the [data-lake connectors strategy](docs/architecture/roadmaps/data-lake-connectors-strategy.md).
  DB11 only re-opens that decision with evidence.
- DuckDB is not shipped. The strategy document lists it as an unchecked, low-priority Phase 5 item;
  this entry does not change that.
- Option names stay `UPPERCASE_WITH_UNDERSCORES` with no aliases, per Connector Standards.
- `TEST CONNECTION` and `MERGE` already ship and are not roadmap work. Per-connector `MERGE` pushdown
  behavior is documented under DB04, not rebuilt.

**Dependencies:** DB08 follows the third-party dependency policy for any new package.

**Backlog (select IDs, not the family):**

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| DB08 | **Decision: credential providers.** No source references Azure Key Vault or AWS Secrets Manager; secrets come from `ENC:`, `SECRET:`, and allow-listed environment variables. Decide whether a vault-backed `SECRET:` source belongs in the product and under which trust rules. Output is a proposal and an estimate only. | — | D |
| DB11 | **Decision: native warehouse connectors.** Whether Databricks or Redshift justify a native connector over `ODBC`, given that `ODBC` needs a vendor driver installed on the host. Gather evidence (install friction, feature loss through the bridge, type fidelity) and propose or reject. | — | D |

Shipped in v0.23.0: DB01–DB06, DB09, DB10, DB12, DB13, DB14, and DB15a–c. Decision proposals for DB08 and DB11 remain awaiting owner approval below; DB09, DB12, and DB15 were approved on 2026-11-23 and shipped in v0.23.0.

**Decision proposals (2026-11-23):**

- **DB08 — Vault-backed `SECRET:` credential providers (2026-11-23, awaiting owner decision):**
  - *Verified current state:* `ISecretProvider` (`SecretProviders.cs`) already abstracts `SECRET:<name>` resolution across `EnvironmentSecretProvider`, `OsSecretStoreProvider`, and HTTP vault providers; `Directory.Packages.props` already includes `Azure.Core`/`Azure.Identity` (via `Microsoft.Data.SqlClient.Extensions.Azure`) and `AWSSDK.Core` (via `AWSSDK.S3`), but neither `Azure.Security.KeyVault.Secrets` nor `AWSSDK.SecretsManager` is referenced.
  - *Trust rules:* (1) **Host-configured only** via `appsettings.json` (`Governance:Secrets`), never script-selectable or per-connection (prevents SSRF/exfiltration to attacker vaults); (2) **Caching & rotation:** short TTL in-memory cache (default 5 min, zero disk/snapshot persistence) with refresh on auth failure; (3) **Audit & logging:** log secret name, provider, correlation ID, and outcome (`Resolved`/`NotFound`/`Denied`) — never secret values or raw payloads.
  - *Dependency cost (§11):* `Azure.Security.KeyVault.Secrets` (MIT, reuses `Azure.Core`/`Azure.Identity`) and `AWSSDK.SecretsManager` (Apache-2.0, reuses `AWSSDK.Core`).
  - *Recommendation & estimate:* **Approve** host-configured `AzureKeyVaultSecretProvider` (**`M`, ~2 days**) and `AwsSecretsManagerSecretProvider` (**`M`, ~2 days**) as `ISecretProvider` implementations when a target cloud deployment requests them (no script syntax changes).

- **DB11 — Native Databricks / Redshift connectors vs `ODBC` (2026-11-23, awaiting owner decision):**
  - *Evidence:*
    - **Redshift:** Wire-compatible with PostgreSQL in part, but the `POSTGRES` connector is **not verified** against it (review correction, 2026-11-24): it never sets Npgsql's `ServerCompatibilityMode.Redshift`, so Npgsql's type loading can fail on connect, and every cross-source write goes through `COPY ... FROM STDIN`, which Redshift does not support. Reads and `EXECUTE ... BEGIN ... END` might work once the mode is set; writes would need S3 staging plus Redshift `COPY ... FROM 's3://...'` or multi-row `INSERT`. `ODBC` works today with the Amazon driver but loads row by row.
    - **Databricks SQL:** `ODBC` requires installing and configuring the Simba/Databricks ODBC driver on every Windows/Linux host, flattens `STRUCT`/`ARRAY`/`MAP` types to strings, and only writes row-by-row `INSERT`. Databricks exposes the **SQL Statement Execution REST API v2** + Unity Catalog Volumes upload over plain `HttpClient` + `System.Text.Json` (zero new NuGet packages, zero native OS driver install).
  - *Recommendation & estimate:* **Reject a separate native `REDSHIFT` connector for now**; if Redshift is prioritized, add a `POSTGRES` Redshift mode (sets the Npgsql compatibility mode, refuses `COPY FROM STDIN` writes with a message pointing to S3 staging, plus a container-free test against a recorded handshake or a real cluster) — `S–M`, ~1–2 days, not the 0.5-day cookbook-only estimate; **Propose native `DATABRICKS` connector (`L`, ~4–5 days)** over the REST Statement Execution API when a Databricks deployment is prioritized, keeping `ODBC` as the interim bridge.

- **DB09 — Cloud identity authentication (Approved 2026-11-23):**
  - *Verified current state:* `BIGQUERY` already supports Application Default Credentials (ADC) when `CREDENTIAL_FILE` is omitted. `MSSQL` supports `USER`/`PASSWORD` and `TRUSTED_CONNECTION=TRUE` only; `POSTGRES`, `MYSQL`, and `ORACLE` support `USER`/`PASSWORD` only; `SNOWFLAKE` supports `PASSWORD` or RSA key-pair JWT (`PRIVATE_KEY_FILE` + `PRIVATE_KEY_PASSPHRASE`). Neither `Azure.Identity` nor `AWSSDK.RDS` is referenced in `Directory.Packages.props`.
  - *Options & estimate:*
    1. **MSSQL Entra auth (`S–M`, ~2 days):** Add `AUTH_MODE` (`ENTRA_DEFAULT`, `ENTRA_MANAGED_IDENTITY`, `ENTRA_SERVICE_PRINCIPAL`, `ACCESS_TOKEN`) to `MSSQL` via `Microsoft.Data.SqlClient`'s built-in `Authentication=` keywords / `SqlConnection.AccessToken` (confirming §11 dependency status for `Azure.Identity`).
    2. **Postgres & MySQL cloud IAM tokens (`M`, ~2–3 days per cloud):** Add `AUTH_MODE = 'AWS_IAM'` (+ `AWS_REGION`, requiring `AWSSDK.RDS` under §11) or `AUTH_MODE = 'ENTRA_MANAGED_IDENTITY'` (requiring `Azure.Identity`) wired into Npgsql's `UsePeriodicPasswordProvider` and MySqlConnector's `ProvidePasswordCallback`.
    3. **Oracle OCI IAM (`L`, ~3–4 days):** Defer; requires OCI SDK/wallet integration.
  - *Recommendation:* Approve **Option 1 (`MSSQL` Entra `AUTH_MODE`)** for the next release; defer `AWS_IAM` (Postgres/MySQL) and Oracle OCI IAM until a concrete deployment requires adding `AWSSDK.RDS` / `Azure.Identity`.

- **DB12 — Connector resilience options (Approved 2026-11-23):**
  - *Verified current state:* Transient retries **already exist** globally in `ConnectorRetryPolicy.cs` (Polly exponential backoff with jitter) for `MSSQL`, `POSTGRES`, `ORACLE`, `SNOWFLAKE`, `BIGQUERY`, `ODBC`, and `MYSQL`, configured via `appsettings.json` (`Connectors:Retry:MaxAttempts`, `Connectors:Retry:BaseDelaySeconds`). `SQLITE`, `MONGODB`, and `NEO4J` do not use `ConnectorRetryPolicy`. No connector exposes retry, isolation-level, `search_path`, or time-zone options on `CREATE CONNECTION`.
  - *Options & estimate:*
    1. **Retries:** Keep in `appsettings.json` (operational host policy, not per-script); document `Connectors:Retry` on `databases/README.md` (`S`, ~0.5 day). Do not add per-connection retry options unless mixed flaky-WAN + fail-fast-local scripts require an override.
    2. **Isolation level:** Belongs in script (`BEGIN TRANSACTION ISOLATION LEVEL ...` or native `EXECUTE ... BEGIN SET TRANSACTION ISOLATION LEVEL ...; END;`), not on `CREATE CONNECTION` (`S`, ~1 day if added to `BEGIN TRANSACTION`).
    3. **Session setup (`SEARCH_PATH`, `TIME_ZONE`):** Because pooled connections reset or vary outside transactions, `SEARCH_PATH` on `POSTGRES` (via `NpgsqlConnectionStringBuilder.SearchPath`) belongs on `CREATE CONNECTION` (`S`, ~0.5 day). Keep ad-hoc session settings (`TIME_ZONE`, etc.) in `EXECUTE ... BEGIN ... END` inside a transaction.
  - *Recommendation:* Keep retries global in `appsettings.json` (and document them); add `SEARCH_PATH` to `POSTGRES` (`S`, ~0.5 day) and defer `BEGIN TRANSACTION ISOLATION LEVEL` until requested.

- **DB15 — MongoDB and SQLite parity (Approved 2026-11-23):**
  - *Verified current state:* `MongodbDataSource` implements `IDataSource` only (full-collection `FindAsync({})` reads; `InsertManyAsync` writes with `DropCollectionAsync` on `APPEND=FALSE`; no `EXECUTE` pass-through and no upsert key). `SqliteDataSource` accepts `DATABASE`, `TIMEOUT_SECONDS`, and `TABLE` only; `PRAGMA busy_timeout` is per-connection and does not persist across separate statements outside a transaction.
  - *Per-item recommendation & estimate:*
    1. **MongoDB `EXECUTE mongo BEGIN ... END` filter/pipeline pass-through — Add (`M`, ~2–3 days):** Implement `IDatabaseSource` with `SupportsSqlPushdown => false` (matching `Neo4jDataSource`), accepting a JSON filter or aggregation pipeline so large collections do not require full-collection scans into engine memory.
    2. **MongoDB `KEY_COLUMNS` upsert — Add (`S`, ~1–1.5 days):** Mirror `NEO4J`'s `KEY_COLUMNS` option to switch `WriteBatches` to `BulkWriteAsync` with `ReplaceOneModel(..., IsUpsert = true)`, enabling idempotent incremental loads without dropping the collection.
    3. **SQLite `JOURNAL_MODE` and `BUSY_TIMEOUT_MS` — Add (`S`, ~1 day); reject generic `PRAGMA` option:** Apply `JOURNAL_MODE` (`DELETE`, `WAL`, `MEMORY`, `OFF`) and `BUSY_TIMEOUT_MS` on connection open (solving per-connection `SQLITE_BUSY` timeouts), while keeping one-off pragmas in `EXECUTE ... BEGIN PRAGMA ...; END;` rather than adding an open-ended `PRAGMA` option.

**Acceptance evidence:** A test fails when any database page's option table differs from the matching
connector allowlist; each page lists `TIMEOUT_SECONDS` with its default and the mutually exclusive
options; the capability matrix and write-path reference match the data source classes, checked line
by line against the source; and each decision item (DB08, DB09, DB11, DB12, DB15) has a written
proposal and estimate.

## Later Reporting and Presentation Work

### Reporting & Presentation — Grammar-of-Graphics Semantic Extensions

**Status:** Incremental
**Horizon:** Later
**Scheduling:** Backlog. This sprint's GoG expansion is finished; no further combination is scheduled.
**Authoritative design:** [Native Grammar-of-Graphics Contract](docs/architecture/decisions/grammar-of-graphics-spec-ir.md),
[Native Advanced Chart Authoring](docs/architecture/decisions/native-advanced-chart-authoring.md),
[Connected mark conditions](docs/architecture/decisions/connected-mark-conditions.md), and
[Reporting semantic contracts](docs/architecture/reporting-semantic-contracts.md).

**Implemented baseline:** The renderer-neutral spine, bounded polar ARC radial stacking,
continuous quantitative transposed POINT/TEXT/LINE/AREA/RULE/RECT aspect forms, their supported
placement/interpolation and intervals, and source-owned Cartesian/transposed LINE/AREA/ribbon
conditions are implemented. Connected forms include GAP/CONNECT/ZERO, row decorations,
interpolation, categorical series and facets. Exact supported combinations remain owned by the
validator, capability matrix and focused references; implementation history remains in
`CHANGELOG.md` and the linked contracts.

**Problem and intended outcome:** Add a specific chart combination when a representative report
needs it. Authors should receive consistent geometry, ownership and accessible meaning across
backends, with explicit diagnostics for combinations outside the chosen scope.

**Why later:** No current catalog visual or renderer retirement depends on these extensions.
The former two-checkbox task had no bounded completion point. Each increment below can close
independently; finishing it creates no obligation to implement adjacent combinations.

**Boundaries:** Preserve raw values, source-owned conditions, domains and existing baseline,
ribbon and confidence semantics. Keep transformations in the script. Do not add renderer-specific
syntax, arbitrary paths, a second chart schema or new map providers/projections. Existing focused
native layouts remain valid. Confidence conditions and arbitrary AREA baselines are not implicitly
added to any task below.

**Dependencies:** Versioned ChartSpec/PlotPlan contracts, shared scale and axis ownership, native
SVG/static export, semantic terminal/accessibility fallback, lossless authoring, capability matrix
and the existing deterministic cross-backend tests.

**How a chunk is selected:** Move only one named increment into `TODO.md`. Name its mark, coordinate
orientation, scale kinds, placement unit, null policy and interpolation; include one representative
report and explicit exclusions. An implementation increment includes resolution, validation and
all applicable backends together. A decision increment finishes with an accepted contract and a
bounded implementation proposal, or an explicit decision to retain rejection. It does not enable syntax.
Facets, extra series, additional policies or orientations are separate follow-ups unless named in
that increment. Do not open a replacement umbrella checkbox for "all remaining combinations".

#### Connected placement — one mark and one unit per increment

**Status:** Accepted
**Horizon:** Later
**Outcome:** Displace a conditioned path and its row decorations together without changing raw
coordinates or source ownership. Start with one unstacked Cartesian layer, quantitative linear
primary X/Y, GAP and LINEAR interpolation. AREA uses either the explicit zero baseline or the
existing ribbon form. Retain the corresponding current decoration semantics.
**Dependencies:** Existing connected geometry and ordinary placement arithmetic; BAND and JITTER
also need fitted plot dimensions, and DATA needs a scale-mapped anchor contract.

- [ ] **LINE EM nudge.** One display displacement for the path and its decorations.
- [ ] **Zero-baseline AREA EM nudge.** Translate upper boundary, baseline and decorations together.
- [ ] **Ribbon AREA EM nudge.** Translate both authored bounds and decorations together.
- [ ] **LINE BAND nudge.** Use fitted plot dimensions; prove resize behavior.
- [ ] **Zero-baseline AREA BAND nudge.** Translate the complete strip using fitted dimensions.
- [ ] **Ribbon AREA BAND nudge.** Keep the displayed interval span intact through resize.
- [ ] **LINE DATA nudge.** Map each vertex's target through its original primary scales.
- [ ] **Zero-baseline AREA DATA nudge.** Define the scalar Y anchor and move its baseline with it.
- [ ] **Ribbon AREA DATA nudge.** Use authored Y_START as the anchor for both bounds.
- [ ] **LINE deterministic JITTER.** Keep stable-key displacement consistent across adjacent connections.
- [ ] **Zero-baseline AREA deterministic JITTER.** Share each cross-section displacement with its baseline.
- [ ] **Ribbon AREA deterministic JITTER.** Share each cross-section displacement between both bounds.

**Completion evidence:** Common implementation evidence below, plus complete-path/strip and decoration
alignment, raw-value/domain invariance, missing-coordinate behavior, resize and invalid targets.
JITTER additionally proves seed/key stability and that presentation changes do not change offsets.
Transposition, CONNECT/ZERO, non-linear interpolation, series and facets are excluded from these first
increments. Add a separately bounded follow-up for one of those combinations only when selected.

#### Stacking — separate connected and physical-aspect increments

**Status:** Accepted
**Horizon:** Later
**Outcome:** Preserve stacked boundaries and source-owned styles without changing stack totals.
**Dependencies:** Existing ordinary stack resolution; agree signed/normalized domain and baseline
rules before enabling a new form. Each first increment uses nonnegative values, linear primary axes,
LINEAR interpolation, GAP, IDENTITY and no facets or secondary axes.

- [ ] **Conditioned Cartesian AREA ZERO stack.** One scalar AREA definition partitioned by categorical
  series; resolve outgoing strips after stacking. No ribbons, placement or transposition.
- [ ] **Conditioned Cartesian AREA NORMALIZE stack.** Extend the previous form with explicit zero-total
  behavior and normalized bounds. No additional geometry forms.
- [ ] **Ordinary transposed fixed-aspect AREA ZERO stack.** One scalar AREA definition with categorical
  series and continuous quantitative X/Y; preserve physical units. No conditions or placement.
- [ ] **Ordinary transposed fixed-aspect AREA NORMALIZE stack.** Extend the preceding form and prove
  normalized scale/aspect behavior. No additional marks.

**Completion evidence:** Common evidence plus exact cumulative boundaries, shared cross-section edges,
series order, zero-total cases and unchanged totals under conditions. NORMALIZE depends on its ZERO
increment. Signed stacks and stacking on other marks require separately accepted tasks.

#### Secondary axes — one geometry form at a time

**Status:** Accepted
**Horizon:** Later
**Outcome:** A connected layer uses its declared axis without borrowing another layer's scale.
**Dependencies:** Explicit resolved axis ownership and current multi-axis scale/layout contracts.
Start with a Cartesian chart containing one primary ordinary layer and one secondary conditioned
layer, linear axes, IDENTITY, GAP and LINEAR interpolation; no facets or stacking.

- [ ] **Secondary-axis conditioned LINE.** Resolve connections, symbols and captions through that layer's Y scale.
- [ ] **Secondary-axis conditioned zero-baseline AREA.** Include zero in its owning Y domain and render its strips.
- [ ] **Secondary-axis conditioned ribbon AREA.** Keep both bounds on one declared secondary scale.
- [ ] **Decide physical aspect with secondary axes.** Define which scale owns the physical unit ratio,
  how the other axis is mapped, and which combinations remain invalid.
- [ ] **Transposed fixed-aspect POINT with a secondary axis.** After that decision, implement only its
  accepted minimal point form. No connected marks, stacking, placement or intervals.

**Completion evidence:** Common evidence plus deliberately different primary/secondary domains,
unused declarations, correct axis labels and resize. The physical-aspect decision is a prerequisite
for its POINT increment; other transposed marks remain separately selectable.

#### Temporal and categorical aspect — semantics before implementations

**Status:** Exploring
**Horizon:** Later
**Outcome:** Authors can predict the physical spacing of discrete or time coordinates.
**Dependencies:** Explicit time units or category spacing, existing time/band scales and fitted layout.

- [ ] **Decide temporal physical units.** Define elapsed-time units, supported date/time kinds and
  timezone behavior using one representative report; retain rejection for unspecified combinations.
- [ ] **Transposed aspect POINT with temporal X and quantitative Y.** After that decision, implement
  one accepted time kind with IDENTITY, primary axes and no conditions, intervals, series or facets.
- [ ] **Decide categorical physical spacing.** Define what an aspect ratio means for one ordered
  category axis, including empty/singleton categories. Declining support is a valid recorded outcome.
- [ ] **Transposed aspect POINT with categorical X and quantitative Y.** Only if the spacing contract
  is accepted; one layer with IDENTITY and no conditions, intervals, series or facets.
- [ ] **Transposed aspect TICK with categorical X and quantitative Y.** Depends on the categorical
  contract; one ordinary layer with IDENTITY. Other mark/encoding forms remain rejected.

**Completion evidence:** Decision tasks record exact units/spacing and exclusions. Implementation
chunks use common evidence plus independent pixel-spacing oracles, reversal, resize, empty inputs
and the chosen temporal or category edge cases. LINE/AREA and mixed-type compositions are separate work.

#### Polar extensions — separate aspect and connected-path decisions

**Status:** Exploring
**Horizon:** Later
**Outcome:** Extend the current ARC-only polar slice only where a report establishes a concrete need.
**Dependencies:** The shipped polar/radial contract; new path marks require accepted angular
connectivity and wrap semantics before any condition support.

- [ ] **Decide polar physical aspect.** Define angular/radial units and plot fitting for an unstacked
  ARC example; decide whether a physical aspect option is useful at all.
- [ ] **One polar ARC physical-aspect form.** After acceptance, implement that exact unstacked ARC
  example with IDENTITY, no conditions, facets or custom radius bounds.
- [ ] **Decide a polar connected LINE form.** Specify base LINE geometry, angular wrap, gaps and
  source ownership. Polar currently accepts ARC only, so this is a new-mark decision first.

**Completion evidence:** Accepted decisions identify one implementable form or retain rejection.
The ARC increment uses common evidence plus independent angular/radial geometry and resize.
Polar connected LINE implementation and AREA forms are not commitments of these decisions; add
one narrowly scoped task only after the underlying geometry is accepted.

#### Geographic extensions — projection meaning before geometry expansion

**Status:** Exploring
**Horizon:** Later
**Outcome:** Make one geographic distance/aspect or route-condition behavior explicit and portable.
**Dependencies:** Existing server-resolved maps/routes and the bounded equirectangular/Mercator
projection contract. No new projection, provider or remote map-loading behavior.

- [ ] **Decide geographic physical aspect.** Distinguish projected-coordinate units from ground
  distance and specify behavior near projection limits using one representative map.
- [ ] **One geographic POINT physical-aspect form.** After acceptance, implement one existing
  projection, one point layer with IDENTITY and no conditions or facets.
- [ ] **Source-owned COLOR/OPACITY on one geographic LINE route.** Accept the route connection
  contract first, including gaps and antimeridian crossings; then implement one existing projection.
  Exclude placement, decorations, interpolation, AREA and multiple routes from the first increment.

**Completion evidence:** Decisions state the projection, units and exclusions. Implementation uses
common evidence plus an independent projection oracle and exact route endpoint/ownership checks.
Geographic AREA remains outside the currently supported mark set and requires its own design decision.

#### Logarithmic connected scales — explicitly decide whether to extend scope

**Status:** Exploring
**Horizon:** Later
**Outcome:** Decide whether a real report needs conditioned connections on logarithmic scales.
**Dependencies:** Positive-domain rules, connected null semantics and interpolation coordinate space.

- [ ] **Decide one logarithmic connected LINE form.** Compare raw versus display-space geometry and
  establish positive-value/null requirements. If accepted, create a separate task for one Cartesian
  LINE with linear X, logarithmic Y, GAP, LINEAR and IDENTITY. Do not inherit scalar AREA ZERO,
  ribbons, transposition or smooth curves as part of that decision.

**Common implementation completion evidence:** Matching Core/ChartSpec validation and explicit
negative cases for every excluded combination; authoring/designer round trips, lineage and LSP rename;
guarded wire compatibility; deterministic plan/SVG fixtures with independent geometry assertions;
browser and static PDF behavior; terminal and accessible fallback preserving semantic values;
resize/reversal and missing-data cases; and unchanged payload, render-time and bundle budgets.
Update the capability matrix and focused reference for only the accepted form. Finish that increment
before selecting the next; preserve all existing fixtures for earlier supported forms.

### Reporting & Presentation — Fixed-Canvas Report Pages

**Status:** Exploring
**Horizon:** Later
**Authoritative design:** [Page options](docs/reference/visuals-reporting/report/page.md#page-options-and-styling)
describe the existing screen-fit behavior.

**Problem and intended outcome:** Authors who need a fixed design aspect ratio could use
`FIT = 'CANVAS'` and `CANVAS_SIZE` to scale a whole grid uniformly to the available window.
The proposed default is 1280×720, with explicit dimensions and 16:9 or 4:3 presets.

**Why later:** This is separate from the screen-fit page sizing prepared for v0.23.1.

**Boundaries and dependencies:** Keep the existing screen-fit behavior. Reuse the report grid
and resize lifecycle. A CSS transform is one option to evaluate.

**Delivery and acceptance:** Expose the fixed-size option and verify proportional scaling and
letterboxing at different viewport sizes. Confirm that tooltips, popups, and dragging use the
correct coordinates when the canvas is scaled.

### Reporting & Presentation — Client-Side 1:1 GoG Chart Painter (Fluid Resize without Letterboxing)

**Status:** Exploring
**Horizon:** Later
**Authoritative design:** none yet; builds on the existing `ChartSpec` → `PlotPlanResolver` → `PlotPlan` boundary in [Reporting Architecture](docs/architecture/reporting.md) and [Reporting Semantic Contracts](docs/architecture/reporting-semantic-contracts.md).

**Problem and intended outcome:** Native charts currently render server-side through `PlotPlanSvgRenderer` onto a bounded set of fixed canvases (`Compact`, `Standard`, `Wide`, plus aspect-aware variants such as `StandardShort`, `WideShort`, and `UltraWideShort`). When a chart tile is resized to an arbitrary aspect ratio or container size, scaling a static `<svg viewBox="0 0 W H">` scales fonts, stroke widths, margins, and aspect ratio together, producing either letterboxing or undersized text between discrete tiers. The intended outcome is fluid, zero-letterboxing chart rendering at any container `width × height` — readable CSS-pixel typography (`11–12px`), crisp `1px` gridlines, native wrapping HTML/CSS legends (including click-to-toggle series), smooth animated transitions on slicer/cross-filter updates, and zero `/layout` server round-trips on resize — pairing GoG's script-first composability with modern presentation polish that stands clearly apart from rigid legacy BI visuals.

**Why later:** Aspect-aware server canvases, grid-aware initial layout selection (`ApplyFitPageInitialLayouts`), and single-visual `/layout` patches cover the immediate `FIT = 'SCREEN'` pain without duplicating renderer code. This entry captures the next architectural step for review before committing to a second (TypeScript) painter while GoG semantics are still maturing.

**Boundaries:**
- **Avoid parity tax:** Keep all GoG semantics (`ChartSpec`, `PlotPlanResolver`, stacking, scale domains, ticks, formatting, palettes, aggregations, interaction contracts, and Zero-Trust data shaping) in C#. The client painter must stay a stateless, dumb coordinate mapper with zero data or aggregation logic.
- **Keep Phase 1 narrow:** Scope the initial client painter strictly to the 5 core composable Cartesian primitives (`Rect`, `Line`, `Area`, `Point`, `Rule`) plus `Arc`, which cover ~80–85% of real dashboards (BAR, HBAR, LINE, AREA, COMBO, SCATTER, BUBBLE, PIE, DONUT, overlays, and reference bands).
- Keep `PlotPlanSvgRenderer.cs` as the authoritative renderer for PDF, Markdown, offline static exports, and deterministic golden tests.
- Do not use `preserveAspectRatio="none"` stretching on server SVGs (distorts circles, text, and corners).
- Any visual type not yet supported by the client painter (`MAP`, `SANKEY`, `SUNBURST`, `NETWORK`, `GANTT`, etc.) falls back to `visual.nativeSvg` until demand justifies porting it.

**Dependencies:** Compact, versioned browser-safe `PlotPlan` render DTO (scales, ticks, legend entries, and normalized mark data without baked pixel coordinates), `ReportRuntime` `ResizeObserver` lifecycle, and existing `rt-detail.ts` / cross-filter `[data-row-index]` interaction hooks.

**Delivery slices:**
1. **Decision & DTO design (D):** Define the minimal browser-safe `PlotPlan` render payload and measure payload size vs. pre-rendered SVG on the kitchen-sink fixture.
2. **Cartesian + Arc 1:1 painter (M):** Paint `Rect`, `Line`, `Area`, `Point`, `Rule`, and `Arc` marks into an `<svg width="W" height="H" viewBox="0 0 W H">` sized to the tile's exact `ResizeObserver` box, with HTML/CSS legends, theme tokens, dynamic margin/tick thinning, and existing `[data-row-index]` hover/click/detail wiring.
3. **Host parity & high-density follow-up (S/M):** Verify parity across Portal, ReportPlayer, Studio Preview, and VS Code; evaluate optional `<canvas>` mark layer only if high-cardinality scatter/line fixtures (50k+ points) require it.

**Acceptance evidence:** Browser geometry tests asserting `1:1` SVG-to-container fit (zero letterboxing) and readable computed font sizes across narrow, square, and ultra-wide tiles; zero `/layout` network requests during window resize; preserved cross-filtering, hover detail popovers, and dark/light theme parity; unchanged golden/PDF/Markdown outputs for `PlotPlanSvgRenderer.cs`.

### Reporting & Presentation — Composable Visual Card Shell, CSS Token Cascade & Bookshelf Collapse

**Status:** Exploring
**Horizon:** Later
**Authoritative design:** none yet; extends [Reporting Architecture](docs/architecture/reporting.md) and the card shell in `src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-visual.ts`.

**Problem and intended outcome:** Today `.visual-card` places `<h3>`, `.visual-subtitle`, `.visual-toolbar`, and `.chart-wrapper` as flat siblings with an absolutely positioned toolbar and hardcoded `padding-right` offsets, writes RptSQL `STYLE (...)` properties as inline styles that override `SET REPORT CSS`, and emits literal hex colors inside card SVGs. Authors who want custom card layouts (such as a vertical left-side header bar with custom background/border, or a "bookshelf" side rail that folds horizontally into a vertical spine to give adjacent charts more width) have to fight the DOM and CSS cascade. The intended outcome is a clean semantic card shell (`.visual-header` + `.visual-body`) backed by scoped CSS custom properties that flow into both HTML chrome and GoG SVG marks, plus horizontal bookshelf collapse and container-query density steps so small KPI cards stay legible at any size.

**Why later:** Current `FIT = 'SCREEN'` vertical collapse and inline table headers handle immediate layout density. Refactoring `.visual-card` DOM and CSS tokens should be designed once alongside the 1:1 client painter so existing Playwright/CSS selectors migrate cleanly.

**Boundaries:**
- Keep existing `.visual-card[data-name]` and `[data-tag]` hooks working for `SET REPORT CSS` and tests.
- Keep `var(--etl-chart-*, #hex)` fallbacks in SVG attributes so static PDF/Markdown/offline exports render identically without a stylesheet.
- Do not break `CARD`/`KPI` maximize or existing `COLLAPSIBLE` container behavior.

**Dependencies:** `rt-visual.ts`, `rt-layout.ts`, `report-runtime.css`, and `PlotPlanSvgRenderer.cs` (for `var(--etl-chart-*, #fallback)` token emission).

**Delivery slices:**
1. **Semantic card shell & token cascade (S):** Group title, subtitle, and in-flow toolbar inside `.visual-header` and content inside `.visual-body`; expose `HEADER_POSITION = 'TOP' | 'LEFT' | 'BOTTOM' | 'NONE'` and scoped `--visual-*` / `--etl-chart-*` CSS variables instead of hard inline style overrides.
2. **Bookshelf horizontal collapse (S):** Allow left-header or side-column cards to collapse horizontally into a slim vertical spine (`writing-mode: vertical-rl` title + expand toggle), switching that grid column to `auto` so neighboring visuals expand across the freed width.
3. **Container-query responsive KPI cards (S):** Add `@container` size breakpoints on `.visual-card` with fluid `clamp()` typography so small KPI cards step down cleanly instead of clipping:
   - **Full (`>= 180px × 120px`):** label, subtitle, large number, target/goal, delta, and progress ring/bar.
   - **Compact (`< 180px` wide or `< 110px` tall):** scaled-down tabular value, label, and compact delta; hide subtitle and progress ring.
   - **Micro (`< 120px` wide or `< 70px` tall):** single-line label and tabular value only, with full goal/delta details available in the hover tooltip.

**Acceptance evidence:** Browser geometry tests verifying `HEADER_POSITION = 'LEFT'` vertical headers with custom CSS colors/borders, bookshelf column collapse/expand reflowing sibling widths, CSS variable theming propagating into GoG SVG axes/gridlines, and zero text/ring clipping on small KPI cards (down to ~120×70px).

### Presentation & Workspaces — Studio Authoring for Mobile Page Layouts

**Status:** Exploring
**Horizon:** Later
**Authoritative design:** [Page reference — `MOBILE_LAYOUT`](docs/reference/visuals-reporting/report/page.md)

The dialect already supports a separate small-screen layout per page:
`MOBILE_LAYOUT (STRUCTURE = ..., MAP (...), BREAKPOINT = <px>)` is parsed, compiled into the
manifest, and applied by the browser runtime below the breakpoint. Studio has no controls for it.
An author has to write the clause by hand and cannot see the result without resizing a browser.

**Why later:** Deferred on 2026-09-29 after the v0.20.0 dashboard audit. Analytics consumers work on
desktops, and deliberate mobile use of dashboards is rare. The script form covers the occasional
need, so a guided editor is not worth building ahead of demand.

**Boundaries:** Authoring only. No change to the `MOBILE_LAYOUT` grammar, the runtime breakpoint
behaviour, or native mobile applications. A hand-written clause must keep surviving designer edits
unchanged, as it does today.

**Dependencies:** The Studio page layout editor and the lossless designer patcher.

**Delivery slices:** (1) A mobile preview of the page at the declared breakpoint. (2) A guided editor
for the mobile grid that writes `STRUCTURE`/`MAP` from the desktop page's visuals. (3) Breakpoint
control and a warning for visuals the mobile layout leaves out.

**Acceptance evidence:** Designer round trips for pages with and without `MOBILE_LAYOUT`, and a
browser journey that authors a mobile layout and checks the rendered page at a narrow viewport.

### Presentation & Workspaces — ETL-SQL Studio (Report Studio, Script Editor, and Pipeline Studio)

**Status:** Incremental
**Horizon:** Next
**Scheduling:** Backlog for the next sprint; remaining Alpha work is outside the current sprint.
**Authoritative design:** [ETL-SQL Studio](docs/architecture/decisions/etl-sql-studio.md) and
[Portal ETL documents](docs/architecture/decisions/portal-etl-documents.md).

Studio shipped in v0.19.0 as an Alpha. It mounts in Portal and the self-installed WorkstationEditor
host, with the UI sandbox providing development coverage. Canvas ELSE authoring, task-field editing,
PARALLEL swimlanes, guided transforms, dashboard interactions, paginated bands and host-side recovery
drafts are implemented. v0.21.0 shipped Portal pipeline run, schedule, export/import and promotion
(ST04–ST11), private drafts (ST12–ST15), the beginner, reader, RLS and practice journeys (ST16–ST22,
ST21b), and the keyboard and zoom journeys (ST23–ST27). Those rows are retired. The remaining scope
is the backlog below; shipped details remain in `CHANGELOG.md` and the linked implementation contracts.

**Problem and intended outcome:** Studio helps users build ETL pipelines, reports and dashboards
while learning ETL-SQL. Visual helpers lead into confident script editing and remain available when
an author gets stuck. Support both production hosts without requiring VS Code or a TUI fallback,
including ordinary capability-scoped authors and readers. Learning must not require publishing or
administration rights.

**Why next:** Resume the remaining authoring, recovery and certification work next sprint. Completeness
is measured by successful report and ETL journeys across both hosts, with visual helpers and code
editing working together. Once Studio is feature complete, it replaces the Portal's older designer
(`designer.html`); the owner confirmed this direction on 2026-11-04, and the timing is a separate
future decision. Mobile authoring
retains its separate Later horizon.

**Boundaries:** Preserve standard `.rptsql` and `.etlsql` scripts as the source of truth, surgical
parser/patcher edits, per-document state, caller identity and row-level security, bounded previews,
and policy-governed host drafts. Keep native SQL an explicitly labelled advanced escape hatch.
Do not add a vendor SQL builder, proprietary project format or browser-side secret persistence.

**Dependencies:** Shared typed authoring contracts, designer parser/patcher, CodeMirror workbench,
connection/schema discovery, report runtime, Portal catalog capabilities and leases, recovery drafts,
and Orchestrator execution and scheduling.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

#### Unified authoring experience and completeness criteria

Studio combines ReportBuilder visual authoring and WorkstationEditor code editing into one complete
authoring experience across desktop and Portal. Users familiar with Power BI, Tableau, SSRS, and SSIS
should be able to build useful reports and pipelines while learning the generated SQL. SQL remains
the authoritative artifact; visual helpers preserve hand edits. Host differences follow local files,
permissions, and execution boundaries. These components are foundations of Studio; retiring their
entry points is a separate future decision, not the measure of success.

**Working inventory:** [Studio authoring completeness](docs/architecture/decisions/studio-authoring-completeness.md).

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST03 | **Unified beginner-experience acceptance — assessed, not yet accepted.** [Assessment, gaps and host limits](docs/architecture/decisions/studio-beginner-acceptance-assessment.md); Portal ETL, paginated and learner-observation evidence remain open. Assess completed report and ETL journeys, helper/code transitions without lost work, and the learner’s understanding of generated SQL. Record remaining gaps and host-specific limits. | ST01, ST02; required journey evidence | S |
| OR09 | **Complete the `/designer` to `/studio/` cutover in Portal.** Once the Studio beginner/authoring completeness gate (`ST03`) passes, redirect `/designer` and `/designer/new` in Portal to `/studio/` so Report and Pipeline authoring share one workbench, draft/approval pipeline, and capability boundary. | ST03 | S |

#### Author, reader and beginner practice journeys

ST16/ST17 automated report sequences passed on 2026-10-11; the learner's own explanation is ST16b.
See [beginner report evidence](docs/architecture/decisions/studio-beginner-report-evidence.md).

ST16b is deferred, not scheduled: it waits on a person, not on engineering. Run it when someone new to
Studio is available — familiarity with SQL or another BI tool is fine. Until then no release claims a
learner-validated experience.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST16b | **Observed learner walkthrough — deferred until a learner is available.** One learner new to Studio does the report sequence and the ETL exercise in each host while an observer records, per step, what they did unaided and how they explained the generated SQL and the repair. Needs a person; automation cannot close it. [Script and record sheet](docs/architecture/decisions/studio-learner-walkthrough.md), not yet run. | ST16, ST17, ST21, ST22 | S |

#### Keyboard and assistive technology

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST28 | **Portal screen-reader pass — deferred until needed (owner, 2026-10-17).** Needs a real screen reader; none is installed. [Groundwork and pass sheet](docs/architecture/decisions/studio-keyboard-assistive-evidence.md#screen-reader-pass-sheet-st28-st29). One named screen reader/browser combination completes the keyboard document journey with meaningful labels, errors and announcements; keep the observed transcript. | ST23 | M |
| ST29 | **Desktop screen-reader pass — deferred until needed (owner, 2026-10-17).** Record the same bounded journey in one supported desktop/browser combination. | ST24 | M |

#### Execution scope, cancellation and bounded helper previews

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST30 | **Portal execution-scope journey.** Exercise no selection, unresolved statement and mutating-prefix consent refused/accepted; actual statements and displayed scope agree under the permitted execution path. | ST04 when pipeline submission is used | M |
| ST31 | **Desktop execution-scope journey.** Prove the same cases against the desktop host, using real execution responses. | — | M |
| ST32 | **Portal cancellation and lost response.** Confirm server cancellation and one lost-response case; retain the run ID for support and distinguish unknown outcome from stopped work. | ST30 | M |
| ST33 | **Desktop cancellation and lost response.** Prove the same bounded failure cases and diagnostic ownership on the desktop host. | ST31 | M |
| ST34 | **Portal native-dialect helper preview.** One named connector/dialect fixture executes a bounded read-only native task and accepts an empty result without running arbitrary predecessors. | — | M |
| ST35 | **Desktop native-dialect helper preview.** Run that same fixture through the desktop preview path. | ST34 fixture contract | M |
| ST36 | **Portal staged-context helper preview.** One staged-data task and required variable preview correctly; unavailable predecessor context is explained and never silently executed. | — | M |
| ST37 | **Desktop staged-context helper preview.** Prove the same variable/staging and unavailable-context cases on the desktop host. | ST36 fixture contract | M |

#### Degraded editor and failed session recovery

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST38 | **Portal degraded-editor recovery.** Force a module-load failure, type and switch two tabs, then retry successfully; buffers survive and unsupported actions are disabled or explained. | — | M |
| ST39 | **Desktop degraded-editor recovery.** Prove the same failure/retry sequence in the desktop host. | — | M |
| ST40 | **Portal session startup timeout/retry.** Bound an unanswered session request and cover HTTP 500/disconnect; display an actionable error and recover on Retry without granting fallback capabilities. | — | M |
| ST41 | **Portal expired-session retry.** Cover access-token expiry and failed refresh during startup; re-authentication and retry restore the correct identity/capabilities and kept drafts. No new draft storage. | ST40 | M |

#### Delivery budgets, catalog scale and large documents

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST54 | **Measure and decide Studio transfer budgets.** Inventory the full production import closure, designer assets and CodeMirror bundle; record raw/gzip baselines, accepted ceilings and the gate's implementation estimate. No asset optimization or gate implementation. | — | D |
| ST42 | **Studio transfer-size gate.** Enforce the accepted raw/gzip ceilings for that import closure, with a negative oversized fixture. Preserve existing timing gates; new optimization work gets separate estimates. | ST54 | M |
| ST43 | **Portal cold-load evidence.** Measure first usable editor on a declared uncached Portal fixture, record the environment and set a defensible regression ceiling. No broad performance optimization. | ST42 | M |
| ST44 | **Desktop cold-load evidence.** Measure the matching desktop fixture with its own host ceiling. | ST42 | M |
| ST45 | **Paged Portal catalog discovery.** One bounded server-side search/paging path feeds Home while preserving folder/tenant visibility; verify a declared large-catalog fixture. No real-time synchronization. | — | M |
| ST46 | **Fresh catalog and stale-open recovery.** Refresh the paged list after external create/rename/move/delete; a stale deleted/moved selection fails clearly without disturbing dirty buffers. | ST45 | M |
| ST53 | **Measure and decide the large-document contract.** Pin one several-thousand-line Portal fixture and the existing server byte limit; measure editing/analysis, define client limit/degraded behavior and estimate implementation. No editor changes. | — | D |
| ST47 | **Portal large-document limits.** Implement the accepted client ceiling/degraded behavior on that fixture and verify the server-limit boundary without losing text. No general editor rewrite. | ST53 | M |
| ST48 | **Desktop large-document contract.** Measure the same bounded document in the desktop host and verify its limit/degraded behavior. | ST47 fixture contract | M |
| ST49 | **Portal analysis throttling recovery.** One controlled designer-rate-limit rejection preserves the buffer, explains stale diagnostics and resumes analysis after permitted retry; no arbitrary predecessor execution. | ST47 | M |

#### Release-versioned Studio guidance

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| ST50 | **Beginner report guide.** Document the certified report exercise, code/helper round trips and both host entry points, referring to release versions. | ST16, ST17 | S |
| ST51 | **Pipeline run/schedule guide.** Document the certified pipeline flow, grants, status/log/Stop and missing-Orchestrator behavior. | ST07, ST10 | S |
| ST52 | **Draft, publishing and recovery guide.** Explain kept versus unavailable drafts, private/public permissions, conflicts and retry/support diagnostics; include desktop policy limits. | ST14, ST15, ST40, ST41 | S |

#### Small pickup options for sprint planning

These are selectable outcomes, not committed sprints. The ranges sum the initial task sizes for one
developer and exclude external waits. Re-estimate and split again if the selected fixture exposes
more work; do not fill a sprint by assuming every M task takes three days.

| Outcome | Select first | Initial engineering range | Follow-on work |
| :--- | :--- | :--- | :--- |
| Set measurable delivery limits | ST53, ST54 | 2–4 days | Schedule ST47 and ST42 independently after the decisions. |

**Separate Later backlog:** [Mobile page-layout authoring](#presentation--workspaces--studio-authoring-for-mobile-page-layouts)
retains its own horizon. Its preview, grid editor and breakpoint/omitted-visual checks are three
separately selectable increments outside the remaining Alpha backlog.

**Shared completion evidence:** Each selected journey uses the named production host and intended
identity, real parser/patcher/execution responses, exact saved/reopened bytes and expected output.
Add focused regression evidence for defects found within that task. Preserve existing typed contracts,
asset sync, lint/type gates, isolation, draft policy and performance ceilings. Sandbox/source-pattern
checks alone do not certify a production-host journey. The ST03 assessment identifies which task IDs
must complete before the unified beginner experience can be accepted.

---

## Platform, SaaS, and Governance

### Product Coverage — Measure the Need for External ETL Tools

**Status:** Exploring

**Horizon:** Later

**Authoritative design:** No new architecture accepted; use the current
[syntax index](docs/syntax-index.md), [file operations](docs/reference/file-operations/README.md),
[connectors](docs/reference/connectors/README.md) and [function reference](docs/reference/functions/README.md).

**Problem and intended outcome:** Built-in file handling, transfers, APIs, transformations,
validation and orchestration plausibly cover most traditional SSIS script-task needs. The review's
80% estimate is a hypothesis, not measured parity. Establish which recurring tasks still force users
to request an external tool and choose small built-in additions from demonstrated demand.

**Why later:** Coverage measurement does not block the security fixes and should use real workflow
examples, not a feature-count target. **Boundaries:** No general-purpose scripting runtime, promise
to replace every SDK, or automatic implementation of the remaining 20%.

**Dependencies:** A representative, sanitized set of prior ETL/SSIS script tasks or approved-tool
requests, plus the existing capability references and focused tests.

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| XC01 | **Measure coverage and rank actual gaps.** Classify representative tasks as built-in, composable with built-ins, or external-required; prove representative mappings with runnable fixtures. Record sample size, task frequency and effort weighting separately. Produce a short ranked proposal for recurring gaps; specialized SDKs, formats, OCR/ML and OS administration may remain approved external tools. | Sanitized workflow sample | D |

**Acceptance evidence:** Publish the sample definition and reproducible task mappings, distinguish
functional correctness from operational suitability, and report the observed coverage without
claiming universal SSIS parity. Schedule any resulting feature separately.

### SaaS Operations — Shared Lifecycle, Metering, and Hosted Launch Evidence

**Status:** Incremental
**Horizon:** Launch Gate
**Scheduling:** Backlog; outside the current sprint. Required before the corresponding hosted-production claims.
**Authoritative design:** [SaaS Tenant Isolation Architecture](docs/architecture/saas-tenant-isolation.md), [Deployment Profile Certification](docs/administration/platform/deployment-profile-certification.md), and [Provider-Neutral Fault Certification](docs/architecture/decisions/provider-neutral-fault-certification.md)

Managed Dedicated and Shared SaaS profile lanes have passed their topology-specific isolation gates,
and more of the operational closure has landed than this entry previously claimed: the Gateway,
storage and scheduler metering producers all write to the tenant ledger with their connector class,
and queued admission is reconciled after scheduler-process loss by
`SandboxAdmissionReconciliationService`. What remains is sandbox measurement and failure/recovery
evidence, certified Shared lifecycle transitions, and physical runtime and hosting evidence bound to
production claims. Scheduled sandbox attempts already carry
CPU, process peak-memory and spill-I/O counters through the CLI completion envelope into the
Scheduler ledger event. That existing wiring is the baseline for SA01–SA05.

**Why launch gate:** Contract and deterministic-adapter evidence proves product invariants, but it
does not prove an untested hardened runtime, cloud service, HA topology, or production region. Those
claims must be bound to the provider and candidate commit that actually ran them.

**Boundaries:** Do not reopen the passing Shared hostile-isolation profile or infer a Shared
transition from Managed Dedicated evidence. Provider-specific fault activation stays behind the
provider-neutral contract. Metering remains observational and cannot become an execution-policy or
authorization input.

**Dependencies:** The Shared tenant lifecycle saga, ledger-backed sandbox admission, immutable
scheduler workload identity, object-native artifact storage, tenant metering ledger, hardened
sandbox provider, production canaries, HA soak tooling, and release claims index.

**Planning:** Use the [backlog sizing and readiness rules](#sprint-planning-for-backlog-tables).
Select individual IDs when this initiative is scheduled; task sizes exclude external waits.

**Scope for every hosted task:** Instantiate an ID for one named topology, runtime/provider version,
region and candidate configuration. Passing one instance does not certify other providers, regions
or topologies. SA12 records the selected instance and infrastructure prerequisites before physical
work is estimated. Missing prerequisites mean pending work. Soak and certification run durations are
calendar time in addition to the engineering size below. Repeat the relevant tasks as separate
instances when another topology is selected.

#### Sandbox metering producer

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA01 | **Review and pin sandbox measurement semantics.** Map the existing Scheduler event and completion envelope to attempt identity, terminal statuses, CPU/process-peak-memory/spill-I/O units and unavailable-counter behavior; identify gaps and estimate their fixes. No billing prices or authorization changes. | — | D |
| SA02 | **Completed-attempt sandbox metering evidence.** Exercise the existing wiring on one declared runtime for success, failure and cancellation. Verify tenant/history identity, approved counters and the agreed unavailable-counter behavior; correct only identified gaps. Do not add a duplicate producer/event. | SA01 | M |
| SA03 | **Meter interrupted attempts.** Record worker loss or termination with an explicit incomplete/ambiguous outcome; recovery never invents counters or attributes a stale attempt to a new run. | SA02 | M |
| SA30 | **Decide metering delivery failure/retry policy.** Specify observable ledger-outage behavior, retry ownership, retention and delivery identity; estimate implementation using the existing ledger. Preserve execution admission, authority and reported workload outcome. No retry implementation. | SA01 | D |
| SA04 | **Metering delivery failure/retry.** Implement the accepted policy for one ledger-outage/recovery fixture; verify observable failures and idempotent retries without altering execution admission, authority or reported workload outcome. | SA02, SA30 | M |
| SA05 | **Sandbox producer isolation/replay evidence.** Interleave two tenant attempts, duplicate deliveries and restart; verify correct partitioned counts and no double accounting or secret/script data. | SA02, SA03, SA04 | M |

#### Shared lifecycle transitions — one journey per task

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA06 | **Select supported Shared transitions.** Name one source/target/version fixture for each supported upgrade, promotion/import, restore and exit journey; record unsupported routes and the portable/host-owned boundary. | — | D |
| SA07 | **Shared in-place upgrade.** One accepted N-to-N+1 fixture preserves tenant state, rejects stale scheduler authority and proves its rollback point while another tenant remains isolated. | SA06 | M |
| SA08 | **Promotion into Shared.** One accepted source topology imports into a disabled Shared tenant, verifies ownership/bindings and rejects collisions or cross-tenant activation without partial changes. No additional source topologies. | SA06 | M |
| SA09 | **Shared portable import.** One approved bundle-to-tenant import round-trips declared state and rejects tampering, replay and another tenant's bindings; no new package format. | SA06 | M |
| SA10 | **Shared tenant backup/restore.** Restore one approved tenant fixture, validate usable state and prove another tenant is unaffected; include hostile target/tamper cases. No whole-platform disaster recovery claim. | SA06 | M |
| SA11 | **Shared customer exit.** One Shared-to-self-hosted Enterprise fixture verifies/decrypts its portable bundle without source-operator contact and states required target bindings before mutation. Preserve backward-promotion refusal. | SA06 | M |
| SA12 | **Select one hosted evidence target.** Pin topology, provider/runtime versions, region, image digest, database/artifact providers and access prerequisites; declare fault fixtures, soak duration and measurable claim limits. Produce per-task estimates. | — | D |
| SA13 | **Publish Shared transition evidence through the runner.** Register only supported completed transition fixtures; emit topology-specific claim rows and fail missing/skipped/dirty evidence. Uncovered transitions remain NotCertified. | SA07–SA11 for selected supported routes | M |

#### Physical hardened runtime

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA14 | **Provision one pinned hardened target.** Prepare the selected runtime and worker image, verify required controls and record reproducible setup. Standard container evidence cannot satisfy the hostile-tenant claim. | SA12 | M |
| SA15 | **Certify one physical hardened runtime.** Run the existing lifecycle/isolation fixtures on that target and retain version/digest-bound hostile negative evidence and clean teardown. No inference from deterministic adapters. | SA14 | M |

#### Physical fault activation — one scenario per task

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA16 | **Process/worker loss.** Activate one real loss point, run the catalog's required repetitions and retain safe outcome/checkpoint evidence. | SA12, SA15 | M |
| SA17 | **Lease expiry/fencing race.** Activate the selected target's real race and prove stale owners cannot commit alongside a new owner. | SA12, SA15 | M |
| SA18 | **Database disconnect.** Activate one physical provider disconnect and prove explicit failure/recovery with no silent loss. | SA12, SA15 | M |
| SA19 | **Partial artifact operation.** Interrupt one physical write and prove that partial state cannot masquerade as a committed artifact. | SA12, SA15 | M |
| SA20 | **Storage outage.** Make the selected storage provider unavailable and verify visible failure/recovery under the existing operation contract. | SA12, SA15 | M |
| SA21 | **Network partition.** Partition one declared authority path and prove single mutation authority and bounded recovery. | SA12, SA15 | M |
| SA22 | **Duplicate delivery.** Repeat one real workload delivery and prove at most one committed result for its operation identity. | SA12, SA15 | M |
| SA23 | **Clock skew.** Activate one declared physical clock-skew fixture and prove the lease/fencing invariants. Do not perturb unrelated shared hosts. | SA12, SA15 | M |
| SA24 | **Disk exhaustion.** Fill only the isolated test capacity declared in the fixture and prove failure, cleanup and safe retry eligibility. | SA12, SA15 | M |

Each fault task must implement or verify its physical provider action and emit the unchanged
provider-neutral evidence contract. Adapter-only tests do not close it. Use the runner's required
repetitions and checkpoint rules. If SA12 finds a missing provider-control capability, create and
estimate that prerequisite before assigning the affected M-sized task.

#### HA, soak, canary and final claims

| ID | Chunk and finish line | Depends on | Size |
| :--- | :--- | :--- | :--- |
| SA25 | **One topology failover drill.** Lose one active node during the declared workload and prove lease fencing, output continuity or explicit safe failure, and measured recovery within the stated claim. | SA12, SA15; relevant completed fault tasks | M |
| SA26 | **One declared soak run.** Execute the pinned workload/duration, retain raw resource and correctness measurements, and evaluate the predefined pass criteria. Run duration is separate calendar time. | SA25 | M |
| SA27 | **One canary rollout/rollback drill.** Upgrade one permitted canary, verify drain and workload results, then exercise rollback/stop on a failed health check without lowering isolation. | SA12, SA15 | M |
| SA28 | **One clean-candidate evidence closure.** Re-run and validate every required selected-target lane against the exact clean candidate commit; hash/link complete bundles and reject stale, dirty, missing or skipped proof. Engineering estimate excludes execution time. | SA05, SA13 for claimed Shared routes, SA15–SA27 for the target claims | S |
| SA29 | **Reconcile one hosted claim set.** Align the claims index, operator guidance and release wording with that target's actual evidence; list unsupported topologies/transitions/regions explicitly. | SA28 | S |

#### Small pickup options for sprint planning

Select one track or a capacity-sized subset. Physical work is ready only after SA12 records access,
provider controls and revised estimates. These engineering ranges exclude provisioning waits and
run durations; tasks may span calendar sprints even when implementation is small.

| Outcome | Select first | Initial engineering range | Follow-on work |
| :--- | :--- | :--- | :--- |
| Account for one sandbox attempt | SA01, SA02 | 4–7 days | SA03 interruption, then SA30/SA04 delivery recovery and SA05 isolation/replay proof. |
| Establish one Shared transition | SA06, one of SA07–SA11 | 4–7 days | Register that supported route through SA13; other routes remain separate. |
| Establish one physical hardened target | SA12, SA14, SA15 | 7–12 days | Select individual fault scenarios SA16–SA24 after checking provider prerequisites. |

**Shared completion evidence:** Verified tenant partitioning and idempotent measurements;
positive and hostile-negative transition outcomes with continuity/rollback artifacts; physical
provider actions, exact versions and complete fault observations; validated HA/soak/canary reports;
and a claims index bound to the same clean candidate commit. Shared evidence must name Shared.
Managed Dedicated results cannot close Shared tasks, and a passed profile does not certify an
unexecuted lifecycle transition. Deterministic contract evidence remains useful development proof.
