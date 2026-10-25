# Breaking Changes

This file is the canonical record of every behavioral change that could cause an existing script to produce different results or fail to execute. Maintained by the protocol in [AGENTS.md §16](AGENTS.md#16-breaking-change-protocol).

## Format

```
### vX.Y.Z — Category: Short description
- **What changed**: One sentence describing the old vs. new behavior.
- **Who is affected**: Scripts using [syntax / feature / connector].
- **Migration**: What a script author must change.
- **Diagnostic**: Stable compatibility diagnostic code, or `N/A` when this is not detectable by linting.
- **Earliest removal**: Version where the deprecated behavior may be removed, or `N/A` for immediate breaking changes.
```

Categories: `Syntax` | `Semantic` | `TypeSystem` | `Runtime` | `Connector` | `Parser`

---

### v0.22.0 — Runtime: Complete portable builds preserve accepted releases
- **What changed**: Portable packaging builds under `artifacts/portable-release/<attempt>/output` and replaces `release/` only after ZIP, VSIX, certification, SBOM and checksum work succeeds. Previous releases and failed attempts are retained. Failed promotion restores the previous directory; unresolved rollback and concurrent attempts block a new build.
- **Who is affected**: Local portable-release producers, especially retries after a partial or failed build.
- **Migration**: Continue consuming accepted files from `release/`. Inspect each retained attempt's `transaction.json`, `output/` and `previous/` when diagnosing a failure. Resolve `RecoveryRequired` before retrying. Keep enough disk space for staging and retained releases; remove reviewed attempts when no longer needed.
- **Diagnostic**: Lock acquisition or unresolved recovery stops before publishing binaries. Promotion/rollback errors identify the retained previous directory. SBOM output must exist and its component version must match the release version.
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Portable release inputs and ZIP contents are verified
- **What changed**: The portable producer rejects empty, duplicate or unsupported platforms and unsafe version strings before cleanup. Required host files must be nonempty regular files. ZIP promotion requires matching file inventories and SHA-256 hashes, and publish folders cannot contain runtime state. ZIP creation uses .NET and includes hidden files in its verified inventory.
- **Who is affected**: Release automation using unsupported platforms, malformed versions, contaminated publish output or an archive writer that drops or changes files.
- **Migration**: Select each supported platform once and use a three-part version. Publish clean binaries and rerun failed packaging. Invalid input and failed build attempts leave accepted release output intact. ZIP-stage failures preserve an existing destination ZIP.
- **Diagnostic**: Invalid options fail before source preparation. Runtime state and archive inventory/hash mismatches stop packaging; no incomplete ZIP is promoted.
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Native packaging rejects runtime state
- **What changed**: DEB and DMG builders reject protected secrets (including staging/recovery copies), saved installer settings, first-run credentials, SQLite databases/sidecars, key rings and runtime directories before staging cleanup. Broken links to these artifacts also fail.
- **Who is affected**: Native packaging from a publish directory that was used to run a host or installer.
- **Migration**: Publish into a clean directory and build from that output. Preserve runtime state outside packaging inputs; the builder does not delete or inspect its contents.
- **Diagnostic**: `Runtime state in the publish folder. Publish again into a clean folder.` Prior staging and package outputs remain intact on this rejection.

### v0.22.0 — Runtime: Native package staging protects program permissions
- **What changed**: DEB payloads and macOS application staging strip group/other write access from program trees without changing executable or read-only bits. Builders use strict Bash, require their native packaging tool, and bind cleanup to repository staging paths, rejecting linked ancestors/outputs.
- **Who is affected**: Packaging from writable publish directories, from another current directory, with missing packaging tools, or through linked staging directories.
- **Migration**: Run builds with `dpkg-deb` on Linux or `hdiutil` on macOS and keep staging inside the repository. The permission regression builds a synthetic DEB and app bundle; installed-product and real macOS DMG acceptance still use native release lanes.
- **Diagnostic**: Missing packaging tools and linked staging paths fail before cleanup.
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Packaging checks failures and scopes host output
- **What changed**: `Master-Release.ps1` validates source inputs before packaging, uses the pinned UI lockfile with `npm ci`, and stops on command or missing-artifact failures. It collects the current host's fresh nonempty MSI/DEB/Intel DMG in `release/`, restores caller location/version environment state, and reports packaging completion. Linux/macOS builders execute on their own hosts; WSL is no longer dispatched implicitly.
- **Who is affected**: Packaging automation relying on ignored command failures, stale artifacts, source-directory native output, implicit WSL execution, or leaked caller state.
- **Migration**: Use Node.js 22 and a three-part release version with optional prerelease/build metadata. Run native platform lanes separately and read native packages from `release/`. `-SkipTests` and `-IncludeSampleValidation` cannot be combined. Separate certification and publication gates remain required.
- **Diagnostic**: The failed command or missing native package produces a nonzero result.
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Retired source-only installation helpers
- **What changed**: Removed `scripts/install.ps1`, `scripts/install.sh` and the Orchestrator project's `install-service-windows.ps1`/`install-service-linux.sh`. The workstation templates reported success without downloading or extracting tools. The service helpers used obsolete binary names; the Windows helper could stop/delete an existing service after a failed publish and still report success.
- **Who is affected**: Source-checkout automation calling these helpers.
- **Migration**: Use the maintained MSI workstation/server features on Windows, Linux packages and their service units, or native workstation ZIPs/Intel DMG on macOS. See [Installation](docs/administration/platform/installation.md). `Master-Release.ps1` runs the installer source gate before packaging.
- **Diagnostic**: Installer source validation rejects any restored retired helper.
- **Earliest removal**: N/A

### v0.22.0 — Runtime: MSI configuration failures stop installation
- **What changed**: MSI settings capture, restoration and service configuration now return checked failures. PowerShell no longer swallows configuration errors. Protected files are fully staged before replacement, existing keys survive failed writes, and retained recovery files block retries that could generate new keys.
- **Who is affected**: Windows installs/upgrades with inaccessible settings, denied writes, locked key files or incomplete protected-file recovery.
- **Migration**: Fix the reported installation failure before retrying. Preserve protected `.recovery` files and have an administrator restore the original secrets if needed. Successful installs retain SYSTEM/Administrators-only secret-file permissions. Full installed MSI upgrade certification remains required.
- **Diagnostic**: `ETL-SQL installer configuration failed.`
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Saved views store one resolved-state envelope
- **What changed**: Saved-view APIs reject top-level `parameters`/`filters`, including nulls, and responses no longer contain the flat maps. `CREATE SAVED VIEW` sends a resolved-state envelope and preserves string-literal values. SQLite and PostgreSQL migrations backfill old maps into `StateJson` before dropping `parameters_json`/`filters_json`.
- **Who is affected**: Clients using flat saved-view fields and catalogs containing old flat-only views.
- **Migration**: Send `stateJson` for create/update or `state` for the default-view route. Stop older Portal hosts before upgrading; mixed-version rolling operation is unsupported. Existing envelopes remain unchanged. Flat string maps gain `@` prefixes and filters override parameters; numeric/boolean-looking strings keep their contents instead of being guessed into native types. Invalid old maps stop the upgrade and must be repaired or replaced with a valid envelope. Downgrading restores empty flat columns while retaining the envelopes.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: JSON spill rows carry explicit scalar types
- **What changed**: JSON spill rows now use a versioned typed representation. Text such as `'001'` and `'2026-10-25'` stays text; native numbers, dates, offsets, GUIDs and binary values retain their types. Earlier untyped JSON rows are rejected instead of inferring types from their contents.
- **Who is affected**: Persistent sessions containing JSON spill chunks written before this change, including unencrypted chunks.
- **Migration**: Recreate affected saved sessions by rerunning their source statements. Empty chunks remain readable. Arrow's representation and complete-file authentication are unchanged.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Alert delivery uses named notifications
- **What changed**: Alert entities, API responses and create/update requests no longer contain inline `Recipient`/`SmtpAlias` fields. SQLite and PostgreSQL migrations drop the two obsolete columns from `report_alerts`. Requests reject the retired fields, including explicit nulls, and other unknown fields.
- **Who is affected**: API clients submitting `recipient` or `smtpAlias`, and catalogs retaining old inline alert delivery values.
- **Migration**: Attach a named Orchestrator notification with `ALTER ALERT <name> ADD NOTIFICATION <orchestrator>.<notification>` or the notification attachment API. Remove the retired fields from requests. Stop older Portal hosts before applying this migration; mixed-version rolling operation across it is unsupported. The migration preserves alert definitions and notification attachments. Downgrading recreates empty inline columns; recovering their discarded values requires a pre-upgrade database backup.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Syntax: Retired forms use canonical commands
- **What changed**: The `FILE` connector name, `DROP CHART`, `DOCKER <verb>`, `LINEAGE EXPORT`, `@sensitivity` stewardship alias, comma-less TOOLTIP parts, and `ui old` command are removed. Connection completion no longer offers options for the unsupported `ON TYPE … WITH` constructor.
- **Who is affected**: Scripts and editor workflows using these retired forms.
- **Migration**: Use `AS FLATFILE(...)` for file connections (the `FILE` table name remains valid), `DROP VISUAL`, `START/STOP/PAUSE/CLOSE DOCKER`, `EXPORT LINEAGE … AS OPENLINEAGE TO …`, `@classification`, commas between TOOLTIP parts, and `ui edit`. Write connection options inside `AS TYPE(...)`. The supported `LINEAGE <table> EXPORT AS …` query form is unchanged.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Encrypted spill files require authentication
- **What changed**: JSON and Arrow spill files now use a versioned AES-256-CBC/HMAC-SHA256 envelope with separate derived keys. Readers authenticate the complete file before exposing rows. Earlier unauthenticated encrypted files are rejected, and missing chunks fail instead of returning an empty result.
- **Who is affected**: Persistent sessions containing encrypted spill files written before v0.22.0, or sessions with missing chunks.
- **Migration**: Recreate affected sessions by rerunning their source statements. Newly written empty partitions remain readable. `Security:SpillFormat` still supports `Arrow` and `Json`; neither provides a legacy encrypted-file fallback.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Connector: An unquoted TRUE turns boolean connection options on
- **What changed**: `CREATE CONNECTION` stores an unquoted `TRUE` as `ON`, and most connectors only accepted `TRUE`, so `USE_SSL = TRUE` or `ENCRYPT = TRUE` turned the option off. MSSQL connections were built with `Encrypt=False` and Active Directory bound over plain LDAP. Connectors now accept `TRUE`, `ON`, `YES` and `1`.
- **Who is affected**: Connections that set boolean options without quotes: MSSQL `ENCRYPT`/`USE_SSL`/`TRUST_SERVER_CERTIFICATE`/`TRUSTED_CONNECTION`/`MULTI_SUBNET_FAILOVER`/`POOLING`, Postgres/MySQL/Oracle `POOLING` and `TRUST_SERVER_CERTIFICATE`, MySQL `ALLOW_PUBLIC_KEY_RETRIEVAL`/`ALLOW_USER_VARIABLES`, Active Directory and SMTP `USE_SSL`, S3 `FORCE_PATH_STYLE`, SFTP `ALLOW_UNPINNED_HOST_KEY`, REST `VALIDATE_JSON_BODY`, Neo4j boolean options.
- **Migration**: A connection that worked only because encryption was silently off now encrypts; a server without TLS needs `USE_SSL = FALSE`, and one with a self-signed certificate needs its certificate trusted or `TRUST_SERVER_CERTIFICATE = TRUE`.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Connector: Kafka SECURITY_PROTOCOL applies without SASL and refuses unknown values
- **What changed**: `SECURITY_PROTOCOL` was read only when SASL credentials were set, so `SECURITY_PROTOCOL = 'Ssl'` alone connected in plaintext, and any unrecognized value (including librdkafka's own `SASL_SSL`) became `SaslPlaintext`. It now always applies, accepts `SASL_SSL`-style names, and an unknown value is an error.
- **Who is affected**: Kafka connections that set `SECURITY_PROTOCOL` without SASL credentials, or with a misspelled value.
- **Migration**: Use one of `Plaintext`, `SaslPlaintext`, `SaslSsl`, `Ssl`; a broker without TLS must not be given `Ssl`.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Connector: SMTP USE_SSL = TRUE requires TLS on the port's own terms
- **What changed**: `USE_SSL = TRUE` meant implicit TLS on every port, which fails on 587; `FALSE` meant STARTTLS only if offered. `TRUE` now means implicit TLS on 465 and required STARTTLS on any other port; `FALSE` is unchanged.
- **Who is affected**: SMTP connections with `USE_SSL = TRUE` on a port other than 465 that speaks implicit TLS.
- **Migration**: Use port 465 for implicit TLS. A server on a custom implicit-TLS port must move to 465 or offer STARTTLS.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Semantic: LIKE with a NULL operand is UNKNOWN
- **What changed**: `LIKE` returned false for a NULL operand, so `NOT LIKE` returned true and `WHERE col NOT LIKE 'A%'` kept the rows where `col` is NULL. Both now return NULL, as in SQL and in queries pushed down to a database.
- **Who is affected**: Engine-side queries using `NOT LIKE` or `NOT (... LIKE ...)` over nullable columns.
- **Migration**: Add `OR col IS NULL` where the NULL rows were wanted.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — TypeSystem: Arithmetic on doubles outside decimal's range stays numeric
- **What changed**: A double beyond decimal's range (about 7.9e28), NaN or infinity could not be converted for arithmetic: `+` returned the two values concatenated as text and `-`, `*`, `/`, `%` and unary minus returned NULL. They now compute as doubles; a non-finite result is NULL, as decimal overflow already is.
- **Who is affected**: Expressions over very large `FLOAT`/`DOUBLE` values.
- **Migration**: None expected; code that matched the concatenated text must use the numeric value.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Scripts cannot open the application's own files
- **What changed**: The installers approve the install folder as a safe zone, which let a script read the Portal database, the Data Protection key ring and `appsettings.secrets.json`, or rewrite the service configuration. Configuration, secrets, the Portal and Orchestrator databases and binaries in the application folder, any `appsettings.secrets.json` and any `.portal-keys` folder are now refused in every protection mode.
- **Who is affected**: Scripts that read or write those files directly.
- **Migration**: Use `etl-sql admin backup` for the databases and keys; keep script data in a subfolder or another safe zone.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: `${NAME}` in CREATE/ALTER CONNECTION needs an allow-listed variable
- **What changed**: A `${NAME}` placeholder read any host environment variable. It now reads only names in `Security:AllowedEnvVars`, the list `ENV()` already uses, and any other name is a security error.
- **Who is affected**: Scripts that build connections from environment variables, such as `PASSWORD='${REDSHIFT_PASSWORD}'`.
- **Migration**: Add each variable name to `Security:AllowedEnvVars` (or `"*"` on a host where scripts are trusted). Prefer `SECRET:` references for credentials.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: EXECUTE TOOL keeps a parameter value as one argument
- **What changed**: Parameter values were substituted into the `ARGS` template before it was split on spaces, so a value with spaces became several arguments. The template is now split first; a value is always one argument.
- **Who is affected**: Tool definitions whose `ARGS` relied on a parameter expanding into several arguments.
- **Migration**: Give each argument its own placeholder in `ARGS`.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: GENERATE JWT_SECRET no longer edits appsettings.json
- **What changed**: The statement wrote `Security:JwtSecret`, which nothing reads, into the running application's `appsettings.json`. It now only returns the secret and its encrypted value.
- **Who is affected**: Nobody relied on the written key; operators who expected the file to change.
- **Migration**: Run `etl-sql config setup-jwt --update` on the Portal host, or set `Portal:Jwt:Secret`.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: USE DOCKER accepts official database repositories only
- **What changed**: The database was picked by a substring of the image name, so any image containing `postgres` or `mssql` was pulled and run. The repository name (registry, tag and digest ignored) must now be `mssql/server`, `postgres`, `mysql`, `mariadb`, `oracle-xe` or `oracle-free`. Containers get generated passwords, Oracle included, and publish ports on loopback only. `<prefix>.CONNECTION_STRING` resolves a partial image name only when it names one container.
- **Who is affected**: Scripts using a mirrored or custom image under another repository name, scripts that hard-coded `oracle`/`oracle`, and tools that connected to a container from another machine.
- **Migration**: Mirror the image under its official repository name; read credentials from `<alias>.CONNECTION_STRING`.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: LDAP sign-in refuses names qualified with another domain
- **What changed**: `alice@other.example` and `OTHER\alice` were stripped to `alice` and signed in as this domain's `alice`. With `Identity:Ldap:Domain` set, a qualifier must match it (UPN suffix, or NetBIOS name equal to its first label).
- **Who is affected**: Multi-domain or trusted-forest directories where users sign in with another domain's qualifier.
- **Migration**: Sign in with the configured domain or no qualifier. A Portal serving several domains needs one account per domain, which this release does not provide.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Share links and embed tokens are shown once
- **What changed**: The catalog stored each share link and embed token in plain text and listed it back. It now stores a SHA-256 hash; the token and URL are returned only when the link is created.
- **Who is affected**: Anyone who copied a link from the Portal list or `eng.share_links()` / `eng.embed_tokens()` after creating it, and API clients that revoke by the listed token.
- **Migration**: Copy the URL from the create response or `CREATE SHARE LINK ... INTO #t`. Revoke with `DELETE .../share-links/by-id/{id}` (or `REVOKE SHARE LINK ... FOR REPORT`). A lost link is revoked and created again.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Unauthenticated encryption formats are no longer read
- **What changed**: `ENC:` values (version 1 and unversioned), password-encrypted files, SSH-encrypted files and machine-bound payloads written in the old AES-CBC formats without an authentication tag were still decrypted. They are now refused; only the authenticated formats are read.
- **Who is affected**: Values and files encrypted by releases before the authenticated formats were introduced.
- **Migration**: Encrypt the value again with `etl-sql encrypt`, or the file with `ENCRYPT FILE`.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Upgrade paths for data written by earlier releases are removed
- **What changed**: Startup no longer upgrades state written by earlier releases. Removed: the Orchestrator table and column renames and every add-missing-column step (job store, throttle slots, sandbox admission and metering ledgers, security event outbox); plaintext JSON report snapshots and the snapshot migration service; the plaintext PII column upgrade; the plaintext Orchestrator settings sidecar; the Orchestrator database lookup beside the Portal database; the Gateway `outcomes.json` import; `Portal:Dataset:LegacyAtRestKeyVersion`; JSON temp-table session restore; and Studio drafts kept in browser storage.
- **Who is affected**: Installations with data from an earlier release. There are none.
- **Migration**: Start from empty state.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.22.0 — Runtime: Report formats from earlier releases are no longer read
- **What changed**: The report runtime and the VS Code preview no longer read the pre-v0.19 `interactions` map; a visual without the resolved `interaction` contract takes part in no interaction. Version-one ChartSpec and PlotPlan envelopes are refused instead of migrated. Studio no longer offers to upgrade the single-value selection filter it wrote before v0.20.0. Data-quality trends report only structured per-rule failures; the counts-only rows parsed from the compact history string, and `countsOnly` on `DataQualityRuleFailureDto`, are gone.
- **Who is affected**: Snapshots, chart contracts and job history produced by earlier releases.
- **Migration**: Rebuild reports and rerun jobs.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Portal and Orchestrator databases use snake_case names
- **What changed**: Portal and Orchestrator tables and columns were PascalCase (for example `AspNetUsers`). The first v0.21.0 start renames them to snake_case on SQLite and PostgreSQL. Earlier releases cannot open a renamed database.
- **Who is affected**: Operators who roll back to v0.20.x, and SQL, reports, or tools that query these databases directly.
- **Migration**: Back up the databases before upgrading; rolling back means restoring that backup with the previous release. Update direct queries to the snake_case names in the [database schema standards](docs/architecture/standards/database-schema-standards.md).
- **Diagnostic**: N/A — a previous release fails at start with `no such table`.
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Studio bounds automatic editor services
- **What changed**: Desktop automatic editor services previously sent documents of any length. Studio now pauses automatic analysis, completion, hover, and canvas work above 200,000 characters on desktop or the configured Portal character limit. Text remains editable and is never truncated.
- **Who is affected**: Studio authors editing documents above the host ceiling.
- **Migration**: Reduce the document below the displayed limit to resume automatic services. Portal continues enforcing its existing save limit.
- **Diagnostic**: `DOCUMENT_LIMIT` in editor diagnostics; N/A for language lint.
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Degraded Studio refuses helper writes
- **What changed**: Visual and helper edits could change a document without updating its basic textarea after CodeMirror failed. Those writes now refuse with an explanation. Retry reloads the failed module and preserves both typed buffers.
- **Who is affected**: Authors using Studio after an editor module failure.
- **Migration**: Type directly in basic mode, or retry loading the editor before using visual and helper writes.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Studio helper previews require read-only attempts
- **What changed**: The desktop query helper previously forwarded native bodies without a preview-specific write check. Helpers now validate read-only targets and explicit staging context before execution. Native previews require a direct SQLite connection opened with `Mode=ReadOnly`. Desktop helper result rows also have a 1 MiB serialized byte budget and report truncation.
- **Who is affected**: Authors previewing writes or other native dialects from a helper.
- **Migration**: Use the SQLite read-only fixture for native previews. Use the host's normal, authorized pipeline execution path for writes. Select explicit declaration/staging context for engine previews.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Arrow spill preserves decimal values
- **What changed**: Arrow spill previously widened decimal scale and rounded precision to nine places. Row and native-column spill now retain each decimal value and its original scale using invariant text with decimal type metadata. Spilled sorts also stay streaming for percentage limits.
- **Who is affected**: Queries using disk-backed sorting, aggregation, windows, or percentage replay.
- **Migration**: Remove workarounds for spill rounding. No syntax change is required. Existing Decimal128 spill files remain readable.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Reports with script includes run per viewer
- **What changed**: Reports containing `RUN SCRIPT`, or text whose dependencies cannot be scanned, are treated as identity-sensitive. They execute with the viewer's permissions instead of sharing a cached snapshot.
- **Who is affected**: Reports that include other scripts, even when the parent has no identity references.
- **Migration**: Expect live execution for these reports. Keep self-contained reports when shared snapshots are required.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Connector: Ambiguous BigQuery inserts are not replayed
- **What changed**: Streaming inserts previously retried failures using best-effort insert IDs. Connector and SDK transport retries are now disabled for inserts; errors require reconciliation before another attempt. Engine decimals serialize as invariant numeric strings instead of failing SDK validation.
- **Who is affected**: BigQuery streaming writes.
- **Migration**: Reconcile destination rows after an ambiguous failure before resubmitting. Do not assume an error means no rows were accepted.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Connector: ODBC and Snowflake table views share transaction lifetime
- **What changed**: Table clones copied transaction references and could retain completed transactions or roll back their owner during disposal. They now share live state; only the owner disposes it.
- **Who is affected**: Connector consumers using table views across transactions.
- **Migration**: Commit or roll back explicitly through the transactional source; do not rely on disposing a table view to roll back its owner.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Semantic: NULL keys do not collide in uniqueness fallback scans
- **What changed**: The fallback duplicate scan could reject repeated NULL keys even though the index builder allowed them. Both paths now exclude NULL keys from collisions.
- **Who is affected**: Unique constraints over nullable engine columns, including connector DBNull values.
- **Migration**: Add a NOT NULL constraint when NULLs must be rejected.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: The default machine key is a random installation key
- **What changed**: Without `ETLSQL_MACHINE_KEY_FILE`, the machine key was a digest of the host and account name. It is now derived from random material generated on first use and kept in `machine.key` under the account's local application data (`etl-sql` folder). `ENC:` values written under the old key can no longer be read.
- **Who is affected**: Installations that do not mount a machine key. A container whose local application data is not on a volume gets a new key on every start, so `ENC:` values it writes do not survive a restart. Any `ENC:` value or session checkpoint written before the upgrade must be recreated.
- **Migration**: Mount a key with `ETLSQL_MACHINE_KEY_FILE`, or keep the local application data folder on persistent storage. Re-enter and re-encrypt old `ENC:` values.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Semantic: `GENERATE JWT_SECRET` returns a result set
- **What changed**: The statement printed the plain-text secret and the encrypted value as log messages. It now returns one row with `Secret` and `EncryptedConfigValue` columns and logs neither.
- **Who is affected**: Anyone reading the secret from a job log or from captured message output.
- **Migration**: Read the `Secret` column of the result. `ETL-SQL config setup-jwt` is unchanged.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Numeric dataset filters exclude null cells
- **What changed**: Inclusive numeric comparisons and ranges in the Portal dataset viewer previously treated null as equal to a threshold, including it in matching rows, statistics, and exports. Numeric comparisons now exclude null cells.
- **Who is affected**: Dataset API consumers and viewer users filtering nullable numeric columns with `gte`, `lte`, or `between`.
- **Migration**: Use the explicit `is_null` filter to select null cells. Refresh filtered views and recreate exports affected by the old counts.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Dataset cache text serializes as scalar strings
- **What changed**: The Portal dataset data and distinct-value APIs previously serialized Parquet character memory as character arrays. They now return text as strings, preserving Unicode, empty strings, and nulls.
- **Who is affected**: Consumers of dataset viewer and column-value API responses. The viewer previously displayed `row20` as `r,o,w,2,0`; column-value validation could reject those arrays.
- **Migration**: Consume text cells and distinct values as strings. Remove workarounds that join character arrays. Parquet files and script syntax do not change.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.21.0 — Runtime: Scheduled starts use job identity and recheck the lease after queuing
- **What changed**: Jobs with the same name in different tenants previously shared a local start guard. They now start independently by immutable job ID. A job whose lease expires or is reclaimed while it waits for a throttle slot now refuses execution when it receives that slot.
- **Who is affected**: Multi-tenant schedulers and jobs waiting for concurrency capacity during a lease loss or storage outage.
- **Migration**: No script change. Restore lease-store availability before retrying a refused occurrence; keep scheduler versions aligned when sharing a job store.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Bounded HA runners reject manual certification plans
- **What changed**: Native large-job and fault runners previously accepted ManualCertification plans and labelled bounded local checks as manual evidence. They now reject those plans. Both validators reject relabelled native reports.
- **Who is affected**: Operators using `admin ha-soak large-job-run` or `fault-run` with manual plans.
- **Migration**: Use CiSmoke plans for native checks. Run and measure the physical topology separately for manual certification; regenerate misleading historical reports.
- **Diagnostic**: `evidence-scope-mismatch` for relabelled reports; native runners print the unsupported-plan reason and return nonzero.
- **Earliest removal**: N/A

### v0.20.0 — Semantic: Independent numeric facets retain explicit bounds
- **What changed**: Independent LINEAR/LOGARITHMIC facet domains previously replaced authored MIN/MAX with panel data extrema. Numeric facets now retain each supplied bound and derive only unspecified bounds from panel data. Existing INCLUDE_ZERO behavior remains in effect.
- **Who is affected**: CUSTOM charts combining numeric scale MIN/MAX with independent X, Y or COLOR facet resolution.
- **Migration**: No syntax change. Remove MIN/MAX on axes intended to fit each panel's data automatically. Regenerate reports to apply fixed authored bounds.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Semantic conformance compares fallback content
- **What changed**: Semantic conformance previously rejected an unchanged fallback after serialization because its immutable array was a different instance. It now compares fallback kind, heading, summary and ordered items by value.
- **Who is affected**: Chart backend conformance checks using deserialized PlotPlans.
- **Migration**: No script change. Round-trip projections now pass; altered or reordered fallback items remain failures.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Connected paths retain their full endpoint rows
- **What changed**: Sampling previously reduced the raw row array while retaining connection indices into the original array. Connected layers now retain all endpoint rows, preventing incorrect paths or index failures. Ordinary sampled layers keep their sampling behavior.
- **Who is affected**: CUSTOM LINE or AREA charts with CONDITIONS and SAMPLING enabled on dense data.
- **Migration**: No syntax change. Regenerate reports to restore every source-owned connection. Reduce the source dataset explicitly if fewer connections are needed.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Connected compositions respect point layer priority
- **What changed**: Ordinary POINT layers previously painted after every connected LINE/AREA layer regardless of Z_INDEX. Connected compositions now paint all marks in their authored priority order.
- **Who is affected**: CUSTOM conditioned LINE/AREA charts containing an ordinary POINT layer with a lower Z_INDEX.
- **Migration**: No syntax change. Set the POINT layer's Z_INDEX above the connected layer to keep points in front.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Chart contract migration checks exact envelope fields
- **What changed**: Legacy migration now requires an exact v1 schema and version at the JSON root. It preserves payload strings and handles compact JSON. Previously it rewrote matching text anywhere in the payload, including the v10 schema prefix, and could accept mismatched legacy versions.
- **Who is affected**: Serialized chart contracts containing legacy schema text or compact version-one envelopes.
- **Migration**: No script change. Use matching schema/version fields and regenerate malformed contracts. Titles and descriptions containing schema URLs remain unchanged.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Connected LINE decorations skip incomplete anchors
- **What changed**: Connected LINE rows missing X previously drew a symbol at an invented category position. Symbols and labels now require complete resolved coordinates, matching the connection paths. Raw rows remain in accessible fallback.
- **Who is affected**: CUSTOM LINE charts with CONDITIONS and incomplete positional values.
- **Migration**: No syntax change. Regenerate exported reports to remove the misplaced decorations.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Transposed AREA ribbons produce geometry
- **What changed**: Accepted TRANSPOSED_CARTESIAN AREA layers with Y_START/Y_END now draw gap-separated polygons through their shared bound scale. Previously they emitted no geometry. SVG titles, terminal rows and accessible fallback retain raw X and both Y bounds. The new fixed-aspect AREA form uses guarded ChartSpec v5 and PlotPlan v8 with explicit primary axis ownership.
- **Who is affected**: CUSTOM transposed AREA ribbons without ASPECT_RATIO.
- **Migration**: No syntax change. Regenerate exported reports to include the missing ribbon. Use a v8-capable renderer for new fixed-aspect AREA compositions.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Semantic: AREA ribbon bounds share one Y scale
- **What changed**: Ribbon endpoints on different effective Y scales now fail validation. Ribbons bound to a shared Y scale other than the first declared Y scale render through that selected scale, instead of the first scale. Such output uses guarded PlotPlan v6; other ribbon plans retain their existing envelopes. Scale references differing only in case now contribute to global and independent facet domains, matching validation.
- **Who is affected**: CUSTOM AREA ribbons with separately scaled bounds or a shared bound scale declared after another Y scale.
- **Migration**: Bind Y_START and Y_END to the same Y scale, or leave both unbound to use the inferred scale. Regenerate plans with a renderer supporting v6 when the shared scale is not the first Y scale.
- **Diagnostic**: RPT-CHART
- **Earliest removal**: N/A

### v0.20.0 — Connector: Excel extraction streams the selected sheet
- **What changed**: Excel extraction keeps one output batch in memory instead of materializing every worksheet before returning data. Cancellation is checked between reader calls and remains a cancellation exception.
- **Who is affected**: Large Excel reads and workbooks with unreadable sheets outside the selected sheet.
- **Migration**: No syntax change. Only the selected sheet is read; validate other sheets separately when needed.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Provider exceptions and sensitive log properties hide raw secrets
- **What changed**: Connector exception wrappers discard raw provider inner exceptions. Engine log templates mask arguments with sensitive property names, even when the value has no recognizable secret syntax.
- **Who is affected**: Diagnostics inspecting provider InnerException and logging passwords or tokens as structured properties.
- **Migration**: Diagnose failures from the sanitized connector message. Sensitive property values are intentionally unavailable in every log sink and UI callback.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Connector: REST request credentials and deadlines cover the full request
- **What changed**: Redirects and pagination preserve credentials only on the original scheme, host, and port. The shared HTTP client no longer stores server cookies automatically. Cross-origin redirects or continuations cannot replay a request body. TIMEOUT_SECONDS covers streaming response bodies, and caller cancellation remains a cancellation exception.
- **Who is affected**: API connections following cross-origin redirects/continuations, slow streaming responses, or cancelled requests.
- **Migration**: Configure separate connections for endpoints requiring different credential origins and explicit Cookie headers when needed. Complete each response within TIMEOUT_SECONDS or raise the configured deadline.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Studio save completion preserves later edits
- **What changed**: A save only clears dirty state and removes the recovery draft if the current document still matches the submitted content. Saves for one document are serialized, and shared Portal saves use tenant-relative artifact keys.
- **Who is affected**: Authors typing while a save is pending, submitting multiple saves, or saving catalog documents in a shared Portal deployment.
- **Migration**: No syntax change. Later edits stay dirty until their own save succeeds.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Connector: SharePoint list extraction returns complete, nonduplicated rows
- **What changed**: SharePoint reads all OData continuation pages and unwraps verbose d.results envelopes. SharePoint and Active Directory start an empty batch after yielding instead of copying previously returned rows. Unsupported SharePoint envelopes and unsafe or repeated continuations fail clearly.
- **Who is affected**: Paginated or verbose SharePoint list queries and SharePoint/Active Directory reads reaching a batch boundary.
- **Migration**: No syntax change. Remove any downstream workaround for repeated rows or missing pages. Continuations must remain on the configured SharePoint origin.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Semantic: Join keys honor the active case comparison
- **What changed**: Hash join keys use case-insensitive equality when CASE_SENSITIVE is OFF, including external partitioning and columnar build/probe paths. Merge ordering uses the same ordinal comparison as join equality. Matches no longer disappear when a join switches from LOOP to HASH or MERGE.
- **Who is affected**: Scripts joining string keys that differ only in case with CASE_SENSITIVE OFF.
- **Migration**: Use CASE_SENSITIVE ON when case must distinguish keys. No syntax change is needed to receive the corrected matches.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Scheduler evidence failures preserve execution outcomes
- **What changed**: A failed history, metrics, or resume-metadata write is logged separately and cannot turn a successful execution into a retry or erase a non-retryable failure.
- **Who is affected**: Scheduled jobs whose execution completes while history or metrics storage is unavailable.
- **Migration**: No syntax change. Repair the evidence store and inspect persistence errors; the scheduler no longer replays completed work to recover logging.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Remote UPDATE binds literals and variables correctly
- **What changed**: UPDATE uses the provider's canonical parameter names and merges each expression's parameters without renaming an already-renamed token. Remote updates containing literals or variables now execute with their intended values.
- **Who is affected**: Scripts that UPDATE a database connection with parameterized assignments or predicates.
- **Migration**: No syntax change. Values are bound instead of failing with missing parameters.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Transaction rollback covers nested and first-statement database mutations
- **What changed**: An inner COMMIT keeps database writes pending until the outermost COMMIT. UPDATE and DELETE enlist their target before execution, so an outer ROLLBACK also undoes mutations executed as the first operation on that connection.
- **Who is affected**: Scripts using nested transactions or starting a transaction with UPDATE or DELETE on a database connection.
- **Migration**: No syntax change. Use the outermost COMMIT to persist writes; writes that previously escaped an outer ROLLBACK now roll back.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — TypeSystem: A LIST given as text holds its items
- **What changed**: Text assigned to a `LIST` (a declaration's value, `--var`, or a value a report reader posts) is now split into items: a JSON array, otherwise comma-separated. Previously it stayed one text value, so `IN @list` compared the whole text and `FOREACH` ran once. A MULTISELECT bound to a LIST therefore matched no rows as soon as the reader picked anything.
- **Who is affected**: Scripts that assign text to a `LIST` and rely on it staying whole, such as `DECLARE @L LIST = 'a,b'` compared with `= @L`.
- **Migration**: Declare the variable as `VARCHAR` when the text must stay whole. Write a JSON array (`'["East, Coast"]'`) for an item that contains a comma.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: A page selection is posted as a JSON array
- **What changed**: A chart or table selection now reaches the host as a JSON array of the selected values (`["North","South"]`) instead of comma-joined text. A LIST parameter receives every value; any other parameter receives the value itself when one is selected.
- **Who is affected**: Custom hosts that read the report runtime's interaction batch.
- **Migration**: Decode the value as a JSON array.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Runtime: Formatting keeps a visual's interaction clauses
- **What changed**: Formatting a `CREATE VISUAL` now writes its `INTERACTIONS`, `EMIT_FILTER`, and `ROW_DETAIL` clauses. Previously the formatter omitted all three, so a format pass deleted cross-filter wiring and drill-through, and an `EMIT_FILTER` could come back as an unparseable `EMIT_FILTER:TARGETS` option.
- **Who is affected**: Anyone comparing formatter output for visuals that use these clauses.
- **Migration**: None. Re-format to restore clauses an earlier format pass removed.
- **Diagnostic**: N/A
- **Earliest removal**: N/A

### v0.20.0 — Semantic: Unfaceted terminal charts retain rows from every layer
- **What changed**: Terminal charts without facets use the union of all layer row IDs. Previously the first non-RULE layer supplied the row filter, silently omitting other color groups and reference rows.
- **Who is affected**: Multi-series or multi-layer charts rendered in the terminal without facets.
- **Migration**: No script change is needed. Terminal output now includes the rows already represented by the resolved plan and graphical output.
- **Diagnostic**: N/A — terminal rendering.
- **Earliest removal**: N/A.

### v0.20.0 — Semantic: Isolated transposed reference rules ignore unrelated color groups
- **What changed**: Single-axis RULE layers on transposed fixed-aspect charts no longer inherit color grouping from other layers. Previously a POINT color binding duplicated reference lines and accessible thresholds once per color group.
- **Who is affected**: CUSTOM charts combining these RULE layers with color-grouped marks.
- **Migration**: No script change is needed. Each rule layer now draws each distinct threshold once per facet.
- **Diagnostic**: N/A — reporting resolution.
- **Earliest removal**: N/A.

### v0.20.0 — TypeSystem: Tool output preserves exact JSON numbers
- **What changed**: Integer and decimal tool output no longer rounds through binary double precision before expected-schema conversion. Floating-point columns retain floating-point conversion.
- **Who is affected**: Scripts consuming large integers or high-precision decimals from tools.
- **Migration**: Remove workarounds for rounded tool output; use FLOAT or DOUBLE only for approximate values.
- **Diagnostic**: N/A — runtime data conversion.
- **Earliest removal**: N/A.

### v0.19.0 — Runtime: PIE and DONUT data-label leader lines default to OFF
- **What changed**: Arc data-label leader lines on `PIE` and `DONUT` visuals now default to `OFF` instead of rendering unconditionally.
- **Who is affected**: Existing `PIE` and `DONUT` charts that relied on default leader lines to connect outer arc slice labels to slices.
- **Migration**: To preserve or re-enable leader lines, specify `LEADER_LINE = ON` inside the visual's `DATA_LABELS` options: `OPTIONS (DATA_LABELS = ON WITH (LEADER_LINE = ON))`.
- **Diagnostic**: N/A — visual rendering option.
- **Earliest removal**: Immediate.

### v0.19.0 — Runtime: PlotPlan v3 removes the redundant per-datum tooltip string
- **What changed**: Serialized `PlotPlan` data no longer repeats a joined `tooltip` string on every datum. Tooltip and text content remain available through the datum's typed `channels` collection, which is the source used by production renderers.
- **Who is affected**: Diagnostic or server-side integrations that deserialize PlotPlan v2 JSON or read `layers[].data[].tooltip`.
- **Migration**: Regenerate stored plans from their `ChartSpec` and chart data, then read the `tooltip` or `text` channel's display value. PlotPlan v2 payloads are rejected because the resolved contract is versioned and not a browser persistence format.
- **Diagnostic**: N/A — contract validation reports the unsupported schema and version.
- **Earliest removal**: Immediate.

### v0.19.0 — Syntax: data-quality rules are an `EXPECT` clause, not `@expect`/`@fail` comment tags
- **What changed**: A column's data-quality rules are declared with `EXPECT <rule> [ON FAILURE THROW | WARN | QUARANTINE]`, repeatable per column, replacing `/* @expect: '…'; @fail: '…'; */` and the numbered `@expect_N`/`@fail_N` pairing. Rules combine with `AND`/`OR` rather than a comma (in a select list the comma separates columns), a `MATCHES` pattern is a quoted string literal, and rule values need no outer quoting or SQL-style doubling. A rule decides which rows leave a statement, so it belongs in the grammar where no formatter or comment stripper can remove it; comments keep carrying the tags that describe data.
- **Who is affected**: Every script declaring column rules with `@expect`/`@fail`.
- **Migration**: `/* @expect: 'NOT NULL, UNIQUE'; @fail: 'THROW'; */` becomes `EXPECT NOT NULL AND UNIQUE ON FAILURE THROW` after the column and its alias. Numbered pairs become repeated clauses. `@expect: 'MATCHES ^A.*'` becomes `EXPECT MATCHES '^A.*'`. `'IN (''NA'',''EMEA'')'` becomes `IN ('NA','EMEA')`.
- **Diagnostic**: Lint rule `ColumnRule` reports a rule left in a comment as an Error and prints the clause to write instead.
- **Earliest removal**: Immediate — the tag form is deleted, not deprecated.

### v0.19.0 — Syntax: `ASSERT JOB` uses stacked `ON FAILURE` actions
- **What changed**: `ON CRITICAL_FAILURE THROW` and `WITH (FAIL_ON_WARN = TRUE)` are replaced by stacked `ON FAILURE WARN | NOTIFY <notification> | THROW` blocks, the same vocabulary a rule-carrying `SELECT` uses. Severity is an action rather than a clause name, and `FAIL_ON_WARN` — which could fail a run with no severity clause present — is expressible as the predicate it always was.
- **Who is affected**: Scripts using `ON CRITICAL_FAILURE THROW` or `WITH (FAIL_ON_WARN = …)`.
- **Migration**: `ON CRITICAL_FAILURE THROW` becomes `ON FAILURE THROW`. `WITH (FAIL_ON_WARN = TRUE)` becomes the predicate `WARN_PERCENT = 0` plus `ON FAILURE THROW`; `FAIL_ON_WARN = FALSE` is simply deleted. Writing either retired form is a syntax error naming its replacement.
- **Diagnostic**: N/A — reported as a positioned syntax error.
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: an `ASSERT JOB` predicate on an unwritten column is an error
- **What changed**: A predicate naming a column no sink in the script writes was skipped at runtime with a warning, and the assertion passed — a typo such as `NULL_PERCENT(clean_users.Emial)` produced a guard that could never fail. It is now a lint Error. Skip-with-warning remains for what is genuinely unknowable until runtime: a run that observed no rows, and historical cold start.
- **Who is affected**: Scripts whose `ASSERT JOB` predicates name a column or target the script does not write. Those assertions were passing without evaluating.
- **Migration**: Correct the column or target name. A sink the analyzer cannot enumerate (`SELECT *`) is never reported.
- **Diagnostic**: Lint rule `JobMetricColumn`.
- **Earliest removal**: Immediate.

### v0.19.0 — Runtime: data-quality rule catalog renames `rule_tag` to `rule_clause`
- **What changed**: `eng.data_quality_rules`, `SHOW DATA QUALITY RULES`, and the Portal's data-quality API returned a `rule_tag` column holding `@expect` / `@expect_1`. The column is now `rule_clause` and holds `EXPECT` / `EXPECT #2`, naming a form that can still be written.
- **Who is affected**: Queries or dashboards selecting `rule_tag` (`RuleTag` in the Portal DTO) from those surfaces.
- **Migration**: Select `rule_clause` (`RuleClause`) instead, and expect `EXPECT`/`EXPECT #2` values rather than `@expect`/`@expect_1`.
- **Diagnostic**: N/A — an unknown column name error from the catalog query.
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: CUSTOM charts cross-filter on their resolved X binding
- **What changed**: A layered `CUSTOM` chart has no `MAPPINGS` clause, so the browser's `mapping:*` lookup found nothing and fell through to `visual.columns[0]` — every `CUSTOM` chart cross-filtered on whatever column the source query happened to list first. The selection key is now resolved server-side from the chart's encodings and delivered on `visual.interaction.key`. When the resolved key names no column in the visual's data, the click raises no filter at all instead of guessing.
- **Who is affected**: Reports with a `CUSTOM` visual and an `INTERACTIONS (ON_SELECT = ...)` clause whose X binding is not the first column of its source query. Those reports were filtering on the wrong column.
- **Migration**: None required — the filter now targets the column the chart is actually keyed on. A report that relied on the old column-zero behaviour should name that column explicitly with `INTERACTIONS (MATCHING = '<column>')`.
- **Diagnostic**: N/A.
- **Earliest removal**: N/A.

### v0.19.0 — Runtime: browser manifests no longer carry `chartSpec`, `chartData`, or `plotPlan`
- **What changed**: Every graphical visual serialized five representations of one chart to the browser; the runtime read two. Browser delivery now goes through an explicit projection that drops `chartSpec`, `chartData`, `plotPlan`, `microCharts[].plotPlan`, and the legacy `interactions` map, and adds the compact resolved `interaction` contract. Across the six representative fixtures the combined manifest fell from 170.1 KB to 65.9 KB raw (15.1 KB gzip). The full contracts remain on the server object for renderers, tests, and the explicitly authorized diagnostic projection.
- **Who is affected**: Anything outside this repo reading those fields off a served report manifest — the Portal report API, the ReportPlayer page payload, the LSP preview manifest, or a stored snapshot served to a browser.
- **Migration**: Read `visual.interaction` for interaction semantics and `visual.nativeSvg` for the rendered chart. A consumer that genuinely needs the semantic contracts must go through the authorized diagnostic projection; the browser options cannot produce them. The runtime keeps a tested fallback that reads the legacy `interactions` map, so manifests built before v0.19.0 — offline snapshots, cached artifacts — still render and still cross-filter.
- **Diagnostic**: N/A.
- **Earliest removal**: The legacy `interactions` fallback in `report-runtime.js` may be removed once pre-v0.19.0 snapshots are out of support.

### v0.19.0 — Parser: `SET REPORT` rejects unrecognised keys
- **What changed**: `SET REPORT <key> = '...'` accepted any identifier and the handler silently discarded anything outside its known set, so a typo produced a report that looked configured and was not. The key set is now closed and an unrecognised key is a syntax error naming the supported keys.
- **Who is affected**: Scripts containing a `SET REPORT` key outside `TITLE`, `DESCRIPTION`, `CSS`, `JS`, `HEAD`, `BODY`, `FOOTER`, `FAVICON`, `LOGO`, `BACKGROUND`, `THEME`, `NAVIGATION`, `TIME_ZONE`, `LOCALE`, `NULL_LABEL`. Such a statement already had no effect; the change is that it now fails instead of being ignored.
- **Migration**: Correct the key, or delete the statement. The error message lists every supported key.
- **Diagnostic**: N/A — reported as a positioned syntax error at the key token.
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: Report time and NULL rendering resolve through report formatting
- **What changed**: Chart date and time values were rendered from the engine's generic row strings, and an offsetless temporal string picked up the server's local offset — so the same report rendered different instants on two hosts. Temporal chart columns are now rendered by the report formatter in the resolved report time zone and locale, offsetless strings are anchored to UTC, and a NULL renders as the resolved NULL label (default `-`) instead of an empty string.
- **Who is affected**: Reports with a date/time chart binding, and any chart containing NULL measure values. Output text changes; geometry and plan structure do not.
- **Migration**: None required. To pin the previous NULL rendering, set `Reporting:DefaultNullLabel` to `""` or `SET REPORT NULL_LABEL = ''`. To pin a display zone or culture, use `SET REPORT TIME_ZONE` / `SET REPORT LOCALE`.
- **Diagnostic**: N/A.
- **Earliest removal**: N/A.

### v0.19.0 — Semantic: Invalid detail surfaces (TOOLTIP) now fail the build
- **What changed**: A `TOOLTIP` clause whose target could not be resolved — a missing container or visual, a container cycle, a nested detail surface, or a surface over its depth/visual/node/refresh/payload budget — previously still produced a manifest, which every renderer then ignored. The report published and the tooltip simply never appeared. Manifest building now rejects the report with an `RPT21xx` diagnostic instead.
- **Who is affected**: Reports whose `TOOLTIP` clause already pointed at something that does not exist or cannot be rendered. Such a report was already not showing its tooltip; the change is that the failure is now reported instead of silent.
- **Migration**: Correct or remove the named target. The diagnostic names the offending object and what to do about it.
- **Diagnostic**: `RPT2101`–`RPT2110` (missing container, missing visual, cycle, nested surface, depth, visual count, node count, refresh-query count, payload bytes, per-report surface count).
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: Detail popovers require an explicit, non-secret row-context mapping
- **What changed**: Opening a detail popover pushes one value from the activated row into `@hover_value`. The browser previously fell back to the first column when the visual declared none of the row-context mapping roles. That fallback is removed, and a secret-bearing column is now rejected outright.
- **Who is affected**: Visuals with a container or inline-`VISUALS` `TOOLTIP` that declare none of `X`, `LABEL`, `NAME`, `REGION`, or `Y` in `MAPPINGS`, or that map a column whose name indicates a credential.
- **Migration**: Add one of the row-context mapping roles naming the column that should reach `@hover_value`. If that column is secret-bearing, map a non-secret identifier or label instead; secret values must not reach refresh parameters, manifests, URLs, accessibility text, snapshots, or exports.
- **Diagnostic**: `RPT2114` (no explicit row-context mapping), `RPT2111` (secret-bearing column).
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: The formatter no longer deletes a visual's TOOLTIP clause
- **What changed**: `CREATE VISUAL` formatting omitted the `TOOLTIP` clause entirely, so formatting a report silently deleted the author's detail surface. Pages, containers, and buttons already formatted theirs. The clause is now emitted.
- **Who is affected**: Anyone who formats a `.rptsql` file containing a visual with a `TOOLTIP` — through the LSP, the designer, or `AstSerializer` directly. Formatted output changes, and previously lost clauses are now preserved.
- **Migration**: None. This restores authored content that was being discarded; re-add any `TOOLTIP` clause an earlier format pass removed.
- **Diagnostic**: N/A — detectable only by comparing formatted output against the source.
- **Earliest removal**: N/A.

### v0.17.0 — Connector: SFTP rejects unpinned host keys by default
- **What changed**: The SFTP connector previously connected and logged a warning when `HOST_KEY_FINGERPRINT` was unset, trusting whatever host key the server presented. It now rejects the connection unless the new `ALLOW_UNPINNED_HOST_KEY` option is set to `TRUE`. A fingerprint that is set but does not match is still rejected, as before.
- **Who is affected**: Scripts using the `SFTP` (or `SSH`) connector without `HOST_KEY_FINGERPRINT`.
- **Migration**: Pin the server key — `HOST_KEY_FINGERPRINT = 'SHA256:...'`, obtained from `ssh-keygen -lf <server_host_key>` — which is the recommended fix and gives man-in-the-middle protection. Where an unverified connection is genuinely intended (trusted network, migration window), set `ALLOW_UNPINNED_HOST_KEY = 'TRUE'` to restore the previous behavior explicitly.
- **Diagnostic**: Runtime connection failure logging that the host key is not pinned, naming both options.
- **Earliest removal**: Immediate.

### v0.14.0 — Security: Removal of generic .tmp from whitelisted file extensions
- **What changed**: The `.tmp` file extension has been removed from the whitelist of allowed extensions in `SecurityService`. Files with the `.tmp` extension are no longer readable or writable by default.
- **Who is affected**: Scripts that explicitly read from or write to `.tmp` files.
- **Migration**: Use `.txt` or other allowed extensions (e.g. `.dat`, `.csv`) for temporary script-driven operations.
- **Diagnostic**: Linter or runtime errors when accessing `.tmp` files.
- **Earliest removal**: Immediate.

### v0.12.0 — Runtime: Portal administration mutations require optimistic-concurrency versions
- **What changed**: Updates, deletes, ACL changes, and bulk administration operations for users, groups, folders, reports, datasets, subscriptions, SMTP definitions, and scheduled jobs require the version returned by the latest read; stale writes return `409 Conflict` with current state instead of silently overwriting a newer edit.
- **Who is affected**: Report Portal API and UI clients that mutate administrator-managed resources.
- **Migration**: Read the resource first, retain its `Version`/ETag, and send it with the mutation as documented; for bulk operations inspect each per-item result and retry only conflicted items after refreshing their current state.

### v0.12.0 — Runtime: Report Portal enforces a single-active-instance topology
- **What changed**: A portal process now holds an exclusive instance lock beside the portal database. A second process pointed at the same database fails startup. Execution jobs are persisted, active refresh ownership is database-enforced, and jobs abandoned by restart become `Cancelled` with an interruption reason.
- **Who is affected**: Deployments that started multiple Report Portal processes against one SQLite database or storage root.
- **Migration**: Run exactly one portal service instance per portal database and script/snapshot/dataset storage root. Use the separate Orchestrator service for distributed scheduled execution; multi-instance portal hosting remains unsupported until shared gates, sessions, and quotas are implemented.

### v0.11.0 — Runtime: Share links are anonymous creator-authorized capabilities
- **What changed**: Share links now resolve without caller authentication, but share and embed capabilities fail closed when their creator is disabled or loses current report permission; new capabilities expire after seven days by default.
- **Who is affected**: Portal clients that treated share links as authenticated shortcuts or created links/embeds without an explicit expiry.
- **Migration**: Treat the capability URL as a bearer secret, distribute it only to intended viewers, and ensure the creator retains report access; specify `ExpiresAt` when a lifetime other than seven days is required.

### v0.11.0 — Runtime: Portal sessions are invalidated on security changes
- **What changed**: Access tokens now require a current Identity security stamp, refresh tokens are stored as SHA-256 digests and rotate on use, and logout or security-sensitive account/permission changes revoke all sessions for affected users.
- **Who is affected**: Portal API clients holding tokens across role, group, ACL, password, active-state, logout, disconnect, or explicit revocation changes.
- **Migration**: Treat `401 Unauthorized` after a security change as a required reauthentication and replace stored refresh tokens after every successful refresh.

### v0.11.0 — Runtime: Subscription jobs are credential-free delivery triggers
- **What changed**: Scheduled subscription scripts no longer contain export, recipient, parameter, or SMTP credential data; they trigger portal-side delivery that reauthorizes the owner immediately before export and send.
- **Who is affected**: Administrators or integrations that inspect, edit, or execute generated `SUB:` job scripts outside the Report Portal.
- **Migration**: Keep the Report Portal running with access to the Orchestrator database and manage delivery configuration through the subscription and SMTP APIs instead of editing generated scripts.

### v0.10.0 — Runtime: Audit log exports are audited
- **What changed**: Exporting the Portal audit log now records an `EXPORT_AUDIT_LOG` audit event.
- **Who is affected**: Administrators and integrations that export or count Portal audit records.
- **Migration**: Account for the additional audit event when filtering or aggregating audit activity.

### v0.10.0 — Runtime: Subscription creation enforces report visibility
- **What changed**: Creating a Portal subscription now requires `READ` permission on the report's folder.
- **Who is affected**: Portal clients that attempted to create subscriptions for reports the current user could not view.
- **Migration**: Grant the user an appropriate folder permission or create the subscription as an authorized user.

### v0.10.0 — Connector: SMTP attachment MIME types
- **What changed**: SMTP attachments now use the MIME type inferred from their file extension instead of the generic default content type.
- **Who is affected**: Scripts that send attachments through an `SMTP` connection and consumers that inspect attachment MIME metadata.
- **Migration**: Accept the extension-appropriate MIME type, such as `text/csv`, `text/markdown`, or `application/pdf`.

## v1.0.0 (baseline)

All syntax and behavior documented in:
- [`docs/guides/onboarding/getting-started.md`](docs/guides/onboarding/getting-started.md)
- [`docs/administration/platform/README.md`](docs/administration/platform/README.md)
- [`docs/guides/onboarding/getting-started.md`](docs/guides/onboarding/getting-started.md)

as of this version constitutes the **v1.0 baseline**. No migration required from prior versions.


---

<!-- Add new entries above this line, most recent version first. -->
