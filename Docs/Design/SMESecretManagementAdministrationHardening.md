# SME Secret Management and Administration Hardening (v0.15.0 Phase 7) - Design

**Status:** Draft for implementation planning.
**TODO items covered:** v0.15.0 Phase 7 (SME-friendly secret storage; named-secret syntax parity;
sensitive connection metadata; Portal Connection Catalog; native admin background services).
**Completion gate:** small and midsize deployments can use enterprise-style secret and
administration features without operating a separate vault product, while zero-trust redaction,
audit, and governance boundaries remain intact.

---

## 1. Goal

ETL-SQL already supports named secret providers (`Environment`, `OsSecretStore`, `HttpsVault`) and
`SECRET:name` references for connector passwords and sensitive connection-string fields. Phase 7
fills the SME gap between "put secrets in environment variables" and "operate a dedicated external
vault." The product should provide a supported low-dependency path for local, single-node, and simple
HA deployments without weakening enterprise controls.

The target is:

- a first-class administrative workflow for creating, rotating, verifying, and disabling named
  secrets;
- a Portal-managed encrypted secret store for HA deployments that cannot operate an external vault;
- a governed Portal Connection Catalog so approved endpoints do not need to be repeated in scripts;
- consistent secret-reference syntax and redaction for passwords and sensitive connection metadata;
- native Portal/Orchestrator background services for admin digests and capacity reporting that are
  currently shipped as sample scripts.

---

## 2. Non-Goals

- **No hard dependency on a third-party vault.** `HttpsVault` remains optional for organizations that
  already operate one.
- **No plaintext fallback.** Missing or unavailable secret providers fail closed for operations that
  need the secret.
- **No secret material in exports.** Metadata export/import may carry aliases, provider names, and
  references, but never resolved values.
- **No bypass of script governance.** Cataloged connections expand at execution time and still pass
  policy, audit, path, and connector validation.
- **No hidden scheduler.** Native admin background services must expose schedule, status, audit, and
  failure information instead of silently replacing sample scripts.

---

## 3. Existing Machinery

| Mechanism | Current role | Phase 7 change |
| :--- | :--- | :--- |
| `ISecretProvider` / secret providers | Resolve `SECRET:name` from environment, OS store, or HTTPS vault | Add admin lifecycle commands and Portal-managed encrypted store provider |
| `SecretRedactor` | Redacts raw values, `ENC:`, and `SECRET:` references | Extend tests to connection metadata and catalog expansion paths |
| Connector sensitive option lists | Mask password/token-like fields in display contexts | Add governance/catalog classification for non-password sensitive metadata |
| Portal database and key material | Stores Portal operational state and encrypted assets | Store encrypted secret values and connection catalog metadata with audit/RBAC |
| `samples/admin_operations/*` | Templates for failure digest, backup reporting, capacity reporting | Graduate selected workflows into native background services |

---

## 4. Secret Storage Model

Supported SME paths:

| Path | Deployment fit | Storage authority | Notes |
| :--- | :--- | :--- | :--- |
| `Environment` | containers, simple automation, CI | process environment | Read-only from ETL-SQL; no lifecycle management beyond host tooling |
| `OsSecretStore` | single-node SME/server install | OS account or protected local directory | CLI writes under admin context; services should have read-only access |
| Portal encrypted store | Portal/Orchestrator HA without external vault | Portal database plus cluster-wide keys | Encrypted at rest; requires identical key ring/data-protection material across nodes |
| `HttpsVault` | enterprise | external vault service | Optional; existing integration remains supported |

**Decision (2026-07-10): machine-scoped encryption.** `OsSecretStore` values are encrypted with
`CryptoUtils.ProtectMachine` (`DPAPI-M:` prefix, DPAPI `LocalMachine` on Windows; the existing
`MACHINE:` machine-id-derived AES-256-GCM elsewhere) so an administrator-written secret is readable
by a differently privileged service account on the same machine. User-scoped DPAPI (the pre-Phase-7
format) would have made the admin-writes/service-reads split impossible on Windows. Confidentiality
against other local accounts comes from filesystem ACLs on the store directory, which install
tooling must set. Legacy `DPAPI:` (user-scoped) values remain readable by the account that wrote
them and upgrade to machine scope on rotate. The store fails closed on unrecognized file contents —
there is no plaintext read path.

`OsSecretStore` CLI workflow:

```powershell
etl-sql admin set-secret --name sales_db_password --value <prompted-or-stdin>
etl-sql admin verify-secret --name sales_db_password
etl-sql admin rotate-secret --name sales_db_password --value <prompted-or-stdin>
etl-sql admin disable-secret --name sales_db_password
```

The CLI must avoid shell-history leaks by preferring masked prompt or stdin over command-line values.
If `--value` remains supported for automation, docs must label it as less safe.

Portal encrypted store workflow:

- create/edit/disable secrets through Admin UI and API;
- encrypt values before persistence using cluster-wide Portal keys;
- audit create/update/disable/delete/verify attempts;
- never return secret values after write;
- support backup/restore validation that proves encrypted values are decryptable by the restored
  environment without printing them.

---

## 5. Syntax and Resolution

Canonical connector examples should use quoted expressions:

```sql
CREATE CONNECTION sales AS MSSQL(
  SERVER = 'sql01',
  DATABASE = 'Sales',
  USER = 'etl_worker',
  PASSWORD = 'SECRET:sales_db_password'
);

CREATE CONNECTION archive AS S3(
  BUCKET = 'archive-bucket',
  ACCESS_KEY = 'SECRET:archive_access_key',
  SECRET_KEY = 'SECRET:archive_secret_key'
);
```

`SECRET:` references resolve only on credential fields (`PASSWORD`, `TOKEN`, `ACCESS_KEY`,
`SECRET_KEY`, and similar). A reference on any other field — `BUCKET`, `HOST`, `DATABASE` — is
rejected with a clear error instead of silently reaching the connector as literal text. Extending
resolution to classified sensitive metadata is Section 6 work; until it ships, non-credential
fields take literal values only.

**Decision (2026-07-10): quoted canonical form.** Quoted `'SECRET:name'` and quoted `'ENC:...'`
are the canonical, documented forms (the second option below). Unquoted secret-reference literals
are not added: `SECRET` is already a keyword token (`GENERATE JWT SECRET`), quoted references are
what the resolver, redactor, and every shipped example already use, and unquoted forms would touch
lexer, formatter, linter, and highlighting for no capability gain. Shipped with the decision:
the parser reports a targeted "must be quoted" diagnostic when an unquoted `SECRET:`/`ENC:`/
`DPAPI:` value appears in a CREATE/ALTER CONNECTION option, the `SecretReferenceUsage` lint rule
flags references on non-resolvable fields, and the shipped examples were swept for consistency
(all use quoted references on credential fields only).

- Accept unquoted `SECRET:name` anywhere `ENC:...` is accepted today, then normalize formatting to
  quoted canonical output when serializing scripts. **Rejected — see decision above.**
- **Chosen:** document quoted `'SECRET:name'` and quoted `'ENC:...'` as canonical, add lint/help
  diagnostics for unquoted forms that are not accepted, and keep examples consistent.

The chosen path must include parser, formatter, linter, help, syntax-highlighting, and connector tests.

---

## 6. Sensitive Metadata

Passwords are not the only sensitive connection fields. Phase 7 adds a policy-driven way to mark
connector metadata as sensitive without globally hiding every host or path.

Examples:

- `HOST`, `SERVER`, `ENDPOINT`, `URL`
- `DATABASE`, `SERVICE_NAME`, `WAREHOUSE`
- `PATH`, `ROOT_PATH`, `BUCKET`, `CONTAINER`, `SHARE`
- tenant IDs, account identifiers, region-specific endpoints

Rules:

- sensitivity can come from connector metadata defaults, organization policy, or catalog entry
  classification;
- sensitive metadata is masked in `SHOW CONNECTION`, diagnostics, support bundles, report manifests,
  logs, audit payloads, lineage exports, and cached execution state;
- redaction should preserve enough shape for troubleshooting, such as provider and field name, without
  revealing values;
- resolved sensitive metadata must not be written back into scripts or generated artifacts.

---

## 7. Portal Connection Catalog

The Connection Catalog stores approved connection definitions centrally so developers can reference
known endpoints without repeating host/user/database details in scripts.

Catalog record shape:

| Field | Purpose |
| :--- | :--- |
| alias | Stable script-facing name |
| connector type | MSSQL, POSTGRES, SFTP, S3, etc. |
| environment/tenant scope | Dev/Test/Prod or department boundary |
| non-secret options | Host, port, database, path, default connector options |
| secret references | `SECRET:name` references only; never resolved values |
| owner/steward | Responsible team or administrator |
| RBAC policy | Who may use, test, edit, rotate, or export metadata |
| sensitivity classification | Which metadata fields are masked |
| audit metadata | created/updated/disabled/last-used/last-tested |

Execution behavior:

- a script may reference an approved catalog alias using the existing connection naming model;
- expansion happens at execution time under the caller/service identity;
- policy checks run after expansion and before connector creation;
- audit records include alias, connector type, decision, and masked metadata, not secrets;
- failed secret resolution fails the connection creation before query execution.

Open syntax decision:

- reuse `CREATE CONNECTION alias AS CATALOG('name')`;
- or allow catalog aliases to be pre-bound by Portal/Orchestrator execution context.

The implementation should prefer the least surprising form after parser review.

---

## 8. Lifecycle, RBAC, and Audit

Secret and catalog operations require:

- create, update, rotate, disable, delete, verify, and rebind;
- export/import metadata without secret material;
- per-secret and per-connection owner, environment, and RBAC scope;
- approval/audit on create/update/delete and permission changes;
- last-used and impact inventory before disable/delete;
- test-connection diagnostics with masked fields and bounded timeout;
- HA key compatibility checks for Portal encrypted store;
- clear fail-closed behavior when the configured provider is unavailable.

Every mutation should produce durable audit events. Security-sensitive mutations should follow the
existing fail-closed audit behavior when that policy is enabled.

---

## 9. Native Admin Background Services

Move the current sample-script operational templates into native services:

- daily failure digest;
- backup outcome reporting;
- capacity/free-disk report;
- Portal operational digest integration where applicable.

Required production controls:

- enable/disable per service;
- schedule/interval and timezone;
- HA singleton lease/fencing;
- retry/backoff and max attempts;
- retention and history;
- audit trail for configuration and delivery attempts;
- notification targets via configured SMTP/Portal channels;
- migration path from `samples/admin_operations/*`.

Sample scripts should remain as examples, but the supported production path should be native
configuration.

---

## 10. Delivery Plan

1. **Slice A - secret lifecycle CLI.** Add admin commands for `OsSecretStore` set/verify/rotate/
   disable/delete with masked prompt support and unit tests.
2. **Slice B - syntax parity and redaction.** Decide quoted/unquoted canonical behavior, update docs,
   parser/linter/help where needed, and extend redaction tests across metadata surfaces.
3. **Slice C - Portal encrypted store.** Add database schema, encryption/decryption service, Admin UI/API,
   audit, HA key checks, and backup/restore validation.
4. **Slice D - Connection Catalog.** Add catalog schema, RBAC, execution-time expansion, masked
   diagnostics, import/export metadata, and impact inventory.
5. **Slice E - native admin services.** Convert capacity/failure/backup reporting into managed
   Portal/Orchestrator background services with leases and operational history.

---

## 11. Test Plan

| Test | Proves |
| :--- | :--- |
| Secret provider lifecycle tests | Set/verify/rotate/disable/delete fail closed and never print values |
| Parser/linter/help fixtures | `SECRET:name` and `ENC:...` canonical behavior is consistent |
| Redaction matrix | Passwords and sensitive metadata are masked in every display/persistence surface |
| Portal store integration | Encrypted values survive HA/backup/restore only with correct cluster keys |
| Catalog execution tests | Catalog alias expansion respects RBAC, audit, policy, and secret resolution |
| Admin service lease tests | Exactly one HA node sends each digest/report per schedule |
| Migration tests | Existing sample-script workflows have equivalent native service behavior |

---

## 12. Completion Criteria

- SMEs can provision and rotate named secrets without an external vault.
- Multi-node Portal deployments have a supported encrypted secret-store path with documented key
  requirements.
- `SECRET:name` examples and parser behavior are consistent across connectors and docs.
- Sensitive connection metadata can be classified and redacted without hiding every deployment's
  host/path fields globally.
- Portal Connection Catalog entries are governed, audited, and usable without exposing credentials.
- Capacity, failure digest, and backup reporting have native managed-service equivalents with HA-safe
  scheduling.
