# ETL-SQL Development TODO List

Use this list to track and prioritize outstanding roadmap items, architecture modernization tasks, and documentation improvements.

---

## DATASET Hardening + Permutation/Security Verification

> Status: **planned, not started.** Design agreed; pick up Phase 1 first.
> Goal: make every DATASET permutation (machine/portal-at-rest, password/keyfile transport,
> PUBLIC/PRIVATE) work as intended, with the security boundaries proven by tests. This is
> feature-hardening first, verification second — the current code does not yet match the model below.

### Target model (decided)

- **At rest in a portal a dataset is always encrypted with a portal-managed key** ("machine"), bound to
  the portal's **service account** (Windows DPAPI CurrentUser under the service identity; Linux key file
  `chmod 600` owned by the service account) and **backed up deliberately** so it survives host
  move/restore/failover. Consumers never supply a credential.
- **Password / keyfile = a transport credential only**, to make a dataset *movable* between
  machines/portals. Supplied **at export and at publish only — never written to disk / a sidecar**. On
  publish the portal decrypts once and **re-encrypts with its at-rest key**; after publish the portal
  copy is **not movable** — the author keeps the original file. (Surface this warning at publish.)
- **Identity:** datasets get a **stable ID** + a **globally unique name**; `USE DATASET &x` resolves by
  name portal-wide. Folder is *mutable metadata* (datasets can be moved later).
- **Access:** **PUBLIC = any authenticated user with read permission on the dataset's folder** (reuse
  `FolderPermissionService`); **PRIVATE = owner + explicit dataset grants only** (ignores folder read).
- **Refresh:** transparent stale-cache refresh serves **stale-with-warning** to readers and never
  re-materializes under a consumer's identity. A forced `REFRESH DATASET` requires
  **refresh/editor/owner**; editing the source query requires **editor/owner**. This lets a user group
  operate refreshes without receiving metadata or query-edit rights. **Scheduled/system refresh jobs
  keep admin rights**.
- Threat model: at-rest encryption + compression (already SNAPPY parquet) protects moved files and other
  local users; an attacker with code-exec **as the service account** is out of scope.

### Current state vs target (gaps to close, with file:line)

- DSL/parse + crypto primitives are solid: `MachineBoundCrypto.cs`, `CryptoUtils.cs` (PBKDF2-SHA256 600k
  for PASSWORD; RSA-OAEP+AES hybrid for KEYFILE), `EncryptionOptions.cs`. Parse-level coverage in
  `tests/ETL-SQL.Tests/Reporting/DatasetPhase{2,3,4}Tests.cs`.
- **Engine bypasses ACL:** all four handlers pass a literal `"IsAdmin=true"` —
  `UseDatasetStatementHandler.cs:51`, `CreateDatasetStatementHandler.cs:59`,
  `RefreshDatasetStatementHandler.cs:37`, `ShowDatasetsStatementHandler.cs:41`. `SHOW` lists everything,
  `REFRESH` is unrestricted, PRIVATE is only folder-matched.
- **Cross-folder consumption is broken today:** `UseDatasetStatementHandler` looks up by
  `(name, consumer's folder)` (lines 35, 51) and `DatasetRegistryService.Lookup` filters
  `Name == name && FolderPath == folderPath` (line 69) — a dataset created in folder A can't be consumed
  from folder B at all, PUBLIC or not. Global-unique-name resolution fixes this.
- **Consume/refresh hard-codes `ENCRYPT=MACHINE`** (`UseDatasetStatementHandler.cs:120,168`,
  `RefreshDatasetStatementHandler.cs:81`) with no transport/publish step. Under the target model the
  at-rest read is correct; the transport concern moves to an explicit export/publish.
- **Sidecar leaks the password in cleartext:** `CreateDatasetStatementHandler.WriteSidecarScript`
  (lines 260-275). Target: never write the credential to disk.
- **Folder linkage mismatch:** `Dataset.FolderPath` is a *string* (`PortalEntities.cs:292`) but
  `FolderPermissionService` keys on `FolderId` (`FolderAcls.FolderId`). PUBLIC-via-folder-permission
  needs datasets linked to a folder by ID. Engine can reuse the identity-agnostic overload
  `FolderPermissionService.GetEffectivePermissionAsync(folderId, ISet<int> groupIds)` (line 41) with the
  threaded caller's group IDs.

### Phase 1 — Core model correctness & security (default path; independently shippable)

- [x] **1a. Stable ID + globally unique name.** *(done — branch v0.11.0)* `Name` now carries the unique
  index (`PortalDbContext.cs`; migration `20260610143113_DatasetGlobalUniqueName`). Registry
  `Lookup`/`Exists`/`SetStale`/`Delete` are **by name**; `RegisterOrUpdate` returns the stable Id;
  `BuildDatasetFilePath(int datasetId, string name)` keys the parquet filename on the Id so a folder
  move/rename never rewrites the file. `CreateDataset` registers-first to allocate the Id. The four
  handlers + `DatasetController` + tests updated; new cross-folder regression in `PortalIntegrationTests`
  (`DatasetRegistry_ResolvesByGlobalNameRegardlessOfFolder`). CREATE rejecting a duplicate name now
  surfaces as the DB unique-constraint error — a friendly pre-check is deferred to 1b/1c. EF migration
  drops `(FolderPath, Name)`; note: a catalog with the same name in two folders must be de-duped first.
- [x] **1b. Link datasets to a folder by ID + folder-permission access (PUBLIC).** *(done — branch v0.11.0)*
  Added `Dataset.FolderId` (nullable FK, migration `DatasetAddFolderId`). The dataset→folder link is
  derived from the **executing report**: the report id is threaded into the engine
  (`Evaluator.DatasetOwningReportId`, set by `DashboardService`/`SessionCache`/`ExecutionJobService`
  like the 1c caller context), `CreateDataset` stamps `OwningReportId`, and `RegisterOrUpdate` resolves
  `FolderId = Report.FolderId`. `CanReadAsync` PUBLIC branch now requires Read on `FolderId` via
  `FolderPermissionService.GetEffectivePermissionAsync`; PUBLIC with no folder → any authenticated
  caller (unauthenticated/unset denied). This also **revived the PRIVATE owner check** (`OwningReportId`
  is now populated). `Folder.Path` is logical, not the script dir, so the link could not come from
  `FolderPath`. Tests: `DatasetRegistry_PublicGatedByFolderReadPermission` + updated
  `DatasetRegistry_FiltersPrivateDatasetsByOwnerAclAndAdmin` (no-folder PUBLIC requires auth).
- [x] **1c. Thread caller identity into the engine (close the ACL bypass).** *(done — branch v0.11.0)*
  Added `Evaluator.DatasetCallerContext` beside `DatasetRegistry`; the four handlers now forward it to
  `Lookup`/`ListAll` instead of the literal `"IsAdmin=true"`, so `DatasetRegistryService.CanReadAsync`
  (owner + `DatasetAcl` grants) is the access authority for PRIVATE. The **1a interim folder guard is
  removed**. Portal wiring: `DashboardService` takes a caller-context ctor arg and sets it where it
  assigns the registry; `SessionCache` passes `"UserId={userId}"` (interactive viewing as the real user);
  `ExecutionJobService` snapshot path passes `"IsAdmin=true"` (trusted server-side refresh — the HTTP
  trigger is already permission-gated, so the user-vs-scheduled refresh *write* split stays 1d). Unset =
  fail-closed (PRIVATE denied, PUBLIC allowed); non-portal standalone unchanged (registry null). Tests:
  `DatasetPhase4Tests.UseDataset_PrivateWithoutAccess_Denied` + `ShowDatasets_ForwardsCallerContextToRegistry`.
  (PUBLIC is still an unconditional allow in `CanReadAsync` — the folder-permission gate is **1b**.)
- [x] **1d. Refresh split + serve-stale-with-warning (option a).** *(done — branch v0.11.0)* `USE DATASET`
  is now read-only: a stale cache is served with a yellow staleness warning and **never re-materialized
  under the consumer's identity** (`RematerialiseAndRefresh` deleted from `UseDatasetStatementHandler`);
  a never-materialized dataset errors instead of re-running the source. `REFRESH DATASET` requires the
  independent Refresh/Editor/Owner capability via `IDatasetRegistry.CanRefreshAsync`;
  `CREATE OR ALTER DATASET` (over an existing dataset) remains Editor/Owner-only via
  `IDatasetRegistry.CanEditAsync`. The four-level ACL hierarchy is Viewer < Refresh < Editor < Owner,
  with a migration preserving existing Editor/Owner grants.
  `SHOW DATASETS` already caller-filtered (1c). Re-materialization now happens only via the producing
  report's `CREATE` (owner or scheduled/admin job). Tests: `DatasetPhase4Tests` refresh/create-or-alter
  denial + serve-stale + never-materialized; `PortalIntegrationTests.DatasetRegistry_CanEdit_OnlyOwnerEditorAndAdmin`.
- [x] **1e. Portal-managed at-rest key.** *(done — branch v0.11.0)* Dataset parquet is now encrypted at
  rest with a portal-managed key — `Portal:Dataset:AtRestKey` (base64 config secret, like
  `Portal:Jwt:Secret`), threaded into the engine as `Evaluator.DatasetAtRestKey` (set by
  `DashboardService`/`SessionCache`/`ExecutionJobService`). The three implicit-MACHINE sites
  (`Use`/`Refresh`/`Create` handlers) route through a shared `DatasetAtRestOptions.Apply`: a configured
  key → `ENCRYPT=PASSWORD` with that key (reuses the existing AES-256/PBKDF2 `CryptoUtils` path — no new
  primitive); **unset → falls back to host `ENCRYPT=MACHINE`** (dev/standalone unchanged). The cache is
  portal-bound and portable: back the key up with config and move it with the portal; losing it makes
  caches unreadable (re-materialise to recover). Tests: `DatasetPhase4Tests` at-rest round-trip +
  wrong-key-fails. NOTE: explicit `ENCRYPT=PASSWORD|KEYFILE` on `CREATE` (transport) and the
  scheduled-refresh-job key embedding are revisited in **Phase 2**.

### Phase 2 — Portable move (the "movable" story)

> **Phase 2 COMPLETE (2a-2d) on branch v0.11.0.** Commits: 2d 8796ffb8, 2a 588220c0, 2b 9aa6594d, 2c <this>.

- [x] **2a. EXPORT DATASET** *(done)* `&x TO '<file>' ENCRYPT = PASSWORD|KEYFILE [PASSWORD=… | KEYFILE=…]` —
  decrypts the at-rest cache, re-encrypts to the target with the transport credential (supplied at export,
  never persisted). AST `ExportDatasetStatement` + EXPORT dispatch → `ReportParser.ParseExportDataset` +
  `ExportDatasetStatementHandler` (reuses `EncryptionOptions`/`CryptoUtils`).
- [x] **2b. PUBLISH/IMPORT** *(done)* `PUBLISH DATASET FROM '<file>' AS &x [INTO '<folder>'] [ACCESS …]
  ENCRYPT = …` — decrypt once with the credential, re-encrypt with the portal at-rest key, register.
  Published copy is at-rest-bound (not movable); keep-your-original warning. New `Dataset.CreatedBy`
  (publisher owner; migration `DatasetAddCreatedBy`); `CanReadAsync`/`CanWriteAsync` fall back to
  `CreatedBy`; folder resolved from target logical path.
- [x] **2c. Repurpose `ENCRYPT=PASSWORD|KEYFILE` on `CREATE DATASET` to transport-only** *(done)* — in a
  portal the at-rest cache always uses the portal key (`BuildParquetOptions` ignores the statement's
  ENCRYPT clause when an at-rest key is set); the CREATE credential throw now only applies in non-portal
  mode. Lint realigned: `DatasetEncryptionModeRule` reworded (transport-only/ignored-at-rest/use EXPORT),
  `DatasetEncryptWithoutKeyRule` repointed to EXPORT/PUBLISH (where the credential is required).
- [x] **2d. Remove the cleartext-credential sidecar** *(done)* — deleted `WriteSidecarScript` +
  `EncryptLabel` from `CreateDatasetStatementHandler`.

### Phase 2 follow-up — Security, metadata, and lifecycle correctness

> The portable EXPORT/PUBLISH flow is implemented, but the items below are required before the target
> model can be considered production-hardened.

- [x] **2e. Keep the portal at-rest key out of persisted scheduled-job SQL.** *(done — v0.11.0)*
  Scheduled dataset jobs now persist only a no-secret trigger, so neither the portal key nor source
  credentials enter job SQL. The runtime **`ENCRYPT = PORTAL`** mechanism remains available for other
  persisted connector definitions: `EncryptionOptions` resolves the key from
  **`ETLSQL_DATASET_ATREST_KEY`**, which the portal exports from `Portal:Dataset:AtRestKey`.
  `DatasetRefreshJobSecurityTests` covers PORTAL round-trip/env failure and the no-secret trigger.
- [x] **2L. Make scheduled dataset refresh functional.** *(done — v0.11.0)*
  Replaced the serialized `BEGIN … END` placeholder with a parseable, credential-free orchestrator
  trigger. `IDatasetRegistry.RegisterRefreshJobAsync` maps that trigger to the owning report in
  `DatasetJobs`; `OrchestratorPollerService` observes successful completion and queues the report through
  the portal's keyed `ExecutionJobService`, preserving connection setup, report identity, registry access,
  and at-rest encryption context. Repeated report runs upsert the mapping. A dataset without an owning
  report logs that durable `REFRESH EVERY` scheduling is unavailable instead of creating a false job.
- [x] **2f. Centralize portal/engine dataset authorization.** *(done — v0.11.0)*
  Added `DatasetPermissionService` as the shared authority used by `DatasetRegistryService` and every
  `DatasetController` endpoint. PUBLIC datasets now require folder Read when linked to a portal folder
  (with the documented authenticated-user fallback for legacy rows without a folder); PRIVATE datasets
  require owner/publisher status or an explicit grant; admins remain owners; and ACLs consistently elevate
  eligible readers to Editor/Owner. Added controller/registry regressions for folder-gated PUBLIC access
  and `Dataset.CreatedBy` publisher ownership; the existing seeded dataset permission matrix also passes.
- [x] **2g. Fix portal-key dataset viewing — decrypt by config, not the stored mode.** *(done — v0.11.0)*
  `DatasetViewerService.LoadCachedAsync` no longer decides decryption from `Dataset.EncryptionMode` (which
  records the CREATE transport clause and is unreliable at rest). New `ResolveAtRestDecryptOptions`: when
  `Portal:Dataset:AtRestKey` is set every cache decrypts with the portal key (ENCRYPT=PASSWORD); else the
  stored mode applies (MachineBound→MACHINE, None→plaintext); a legacy Password/KeyFile record with no key
  surfaces a clear error. New `DatasetViewerServiceTests` (portal-key/wrong-key/MACHINE/plaintext/publish-
  shape) over a direct temp-SQLite `PortalDbContext`; existing `DatasetControllerTests` unchanged.
  Remaining bit of the original item — explicit at-rest **version** metadata + migrating legacy
  Password/KeyFile rows + key **rotation** — folded into **2i**. (`ColumnSchema`/`RowCount` not populated
  for PUBLISH is a small separate follow-up; rows still read from the parquet's own schema.)
- [x] **2h. Make CREATE/REFRESH/PUBLISH/EXPORT failure-atomic.** *(done — v0.11.0)*
  Added a shared same-directory dataset file transaction. All four write paths now produce a uniquely
  named `.parquet` staging file, reject missing/empty output, and atomically replace the destination only
  after the write succeeds. A same-directory backup remains until the registry update commits, so failed
  CREATE/REFRESH metadata updates restore the previous readable cache; EXPORT failures preserve an
  existing target. Failed PUBLISH removes its allocated row and partial files so the global name is
  immediately retryable. Dataset deletion and report deletion remove only path-guarded managed files
  inside `DatasetRootPath`. Portal startup reconciliation removes abandoned staging/backup files,
  missing-file catalog rows, and unreferenced managed `<name>_<id>.parquet` files while leaving unrelated
  exports untouched. Failure-injection tests cover refresh rollback, publish credential failure, direct
  and report-owned deletion, and orphan reconciliation.
- [x] **2i (core). Fail closed on a missing/weak at-rest key.** *(done — v0.11.0)* The portal no longer
  silently falls back to host MACHINE encryption. New `DatasetAtRestKeyValidationService` (an
  `IHostedService` mirroring `JwtSecretValidationService`) validates `Portal:Dataset:AtRestKey` at startup
  via the pure `DatasetAtRestKeyValidator`: a set key must be base64 and decode to ≥ 32 bytes; an unset key
  is **Fatal** (the app `StopApplication()`s) unless the new `Portal:Dataset:AllowMachineFallback=true` dev
  opt-in is set (then a Warn). `PortalWebFactory` strips hosted services, so tests are unaffected. Tests:
  `DatasetAtRestKeyValidatorTests`.
- [x] **2i (follow-up). Version + rotate the at-rest key.** *(done — v0.11.0)* Added nullable
  `Dataset.AtRestKeyVersion` with an EF migration; portal writes stamp the configured non-secret
  `AtRestKeyVersion`, and version-aware registry/viewer reads resolve either the current key or a
  configured `PreviousAtRestKeys` entry. `LegacyAtRestKeyVersion` explicitly identifies unversioned rows
  during the first rotation; leaving it unset stamps existing current-key rows without rewriting them.
  Admin-only `POST /api/admin/datasets/rotate-at-rest-key` re-encrypts one guarded managed file at a time,
  atomically updates its version, continues past failures, and is safe to rerun. Rotation also normalizes
  stale Password/KeyFile metadata to portal-managed at-rest semantics. Startup validation checks current,
  previous, and legacy version mappings. The administrator guide now defines first-run provisioning,
  coordinated backup/restore, rotation, resume, verification, and old-key retirement. Tests cover
  validation, previous-version reads, successful re-encryption, legacy stamping, and resumability.
- [x] **2j. Authorize PUBLISH target folders and define system ownership.** *(done — v0.11.0)*
  Added a registry publish preflight that resolves the target folder and requires folder `Manage` before
  `PUBLISH DATASET` allocates a row. Interactive publications set `CreatedBy` to the caller; trusted
  admin/system publication falls back to the target folder owner, so ownership is never null. Successful,
  denied, and post-authorization failed attempts write sanitized `PUBLISH_DATASET` audit events without
  credentials. Tests cover missing/Read-only/Manage targets, system ownership, denial before allocation,
  audit sanitization, and the cross-portal export/publish round-trip.
- [x] **2k. Implement dataset move semantics.** *(done — v0.11.0)*
  Added `POST /api/datasets/{id}/move`. The caller must have folder `Manage` on both the current and
  destination folders (admins may recover legacy rows with no folder); the destination must exist.
  The operation updates `FolderId` and `FolderPath` together, preserves the stable dataset Id and Parquet
  path, invalidates the owning report's sessions, and writes a `MOVE_DATASET` audit event. Regression
  coverage proves denial without destination rights and verifies the successful move after permission is
  granted.

### Phase 3 — Verification deck (scripts + xUnit)

- [x] **Runnable example deck** `samples/08_Reporting/datasets/` + `README.md` (tiny inline/CSV seed; no
  external deps; reuse keyfile at `samples/10_Kitchen_Sinks/test_key/`). Datasets deployed **separately**
  from the reports that consume them:
  - `01_deploy_datasets.etlsql` — CREATE `&sales_public` + `&sales_private`; ends with `SHOW DATASETS`.
  - `02_report_public_consumer.etlsql` — different folder; `USE DATASET &sales_public` → succeeds.
  - `03_report_private_allowed.etlsql` — owner/granted → succeeds.
  - `04_report_private_denied.etlsql` — non-owner, no grant → PRIVATE error.
  - `05_export_then_publish.etlsql` (+ runbook) — EXPORT w/ password/keyfile, then PUBLISH → consume by
    ACL only; shows "not movable after publish."
  - `README.md` — manual portal walkthrough: 2nd user sees PUBLIC (folder read), 403 on PRIVATE, grant
    flips it, refresh permission, "copy the portal .parquet elsewhere → fails."
  *(done — v0.11.0)* Added five parser-verified scripts with inline seed rows, separate producer and
  consumer deployment instructions, an expected PRIVATE-denial case, and PASSWORD plus RSA KEYFILE
  export/publish round trips. The portal runbook covers folder/read/grant behavior, independent Refresh
  permission, at-rest-file non-portability, wrong-credential cleanup, secret-persistence inspection, and
  the distinction between local syntax checks and identity-aware portal execution.
- [ ] **Automated xUnit** — new `tests/ETL-SQL.Tests/Reporting/DatasetSecurityMatrixTests.cs` + extend
  `tests/ETL-SQL.ReportPortal.Tests/DatasetControllerTests.cs`. Build on `PortalIntegrationTests.cs`
  (real registry, ~920-1006) and crypto round-trips in `DatasetPhase2Tests.cs`:
  *(in progress — v0.11.0)* Added the named security-matrix test file with deterministic portal-key,
  PASSWORD transport, and generated RSA KEYFILE transport round trips. It asserts ciphertext differs
  from plaintext and verifies swapped passwords plus missing/wrong private keys fail. Existing Phase 4,
  controller, integration, storage-maintenance, viewer, validator, and rotation suites already cover
  substantial access, refresh, publish, atomicity, folder lifecycle, and key-lifecycle rows below. The
  controller suite now also proves that deleting an owning report leaves its PRIVATE dataset row in
  place but removes the former report owner's implicit access. The remaining work is to consolidate the
  uncovered portal/engine parity and persistence cases rather than duplicate those tests under new names.
  1. **Crypto portability (in-process — no 2nd machine):** at-rest key decrypts locally, swapped key
     throws; transport PASSWORD right/wrong; transport KEYFILE right/missing/wrong; ciphertext ≠
     plaintext. (Deterministic CI assertion on the Linux/keyfile path; Windows binds via DPAPI.)
  2. **Default round-trip:** CREATE folder A → `USE` from folder B by global name → rows match (red today).
  3. **Access model (1b/1c):** PUBLIC consumable with folder read, denied without; PRIVATE denied to
     non-owner, allowed to owner + explicit `DatasetAcl` grant; non-admin `SHOW` lists only visible.
  4. **Refresh split (1d):** non-owner stale → cached + warning, no re-run; `REFRESH` denied to viewer
     and allowed to refresh/editor/owner; query edits remain editor/owner-only; scheduled/system
     (admin) refreshes.
  5. **Export→Publish (Phase 2):** export w/ password/keyfile, re-import, decrypt once, consume by ACL
     with no credential; assert published copy is at-rest-encrypted and credential never sidecar'd.
  6. **Portal/engine parity:** matrix every HTTP dataset endpoint against registry/`USE DATASET` for
     PUBLIC folder-read/no-read, PRIVATE owner/publisher/grants/no-grant, and admin. The same identity
     must receive the same decision through both paths.
  7. **Negatives:** duplicate global name rejected; orphaned `OwningReportId` → PRIVATE inaccessible to
     former owner; export missing credential → clear error.
  8. **Secret non-persistence:** scheduled job definitions, SQLite job rows, logs, exceptions, snapshots,
     and generated scripts never contain the portal at-rest key or transport credentials.
  9. **Metadata/viewer parity:** CREATE with MACHINE/PASSWORD/KEYFILE and PUBLISH with PASSWORD/KEYFILE
     all produce portal-managed at-rest files that the web viewer/API can read without transport creds;
     migrate a legacy metadata row and prove it remains readable.
  10. **Failure atomicity:** wrong publish password, invalid keyfile, encryption failure, registry failure,
      and cancelled refresh leave no blocking row/partial export/orphan plaintext; failed refresh keeps
      the previous cache readable; concurrent readers see either the old or new complete snapshot.
  11. **Key lifecycle:** missing/invalid/weak production key fails startup; backup/restore with the same
      key works; wrong key fails cleanly; key rotation re-encrypts resumably and records the new version.
  12. **Folder lifecycle:** publish to missing/unauthorized folder denied before row allocation; dataset
      move requires source/destination rights, updates both folder fields, and does not rename/rewrite the
      parquet file; delete/report cleanup removes only managed files inside `DatasetRootPath`.
  - Run: `dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT"`
    (Portal tests use WebApplicationFactory — no Docker).

### Phase 4 — Docs / residual decisions

- [ ] Update `Docs/Architecture/Reporting.md` (already stale) + user-facing portal docs: at-rest-vs-
  transport model, "not movable after publish / keep your original," PUBLIC=folder-read /
  PRIVATE=grant, at-rest key backup requirement.
- [x] Document `EXPORT DATASET` and `PUBLISH DATASET` in `Docs/Reference/Grammar.md`,
  `Docs/Report_SQL_Guide.md`, keyword help, language-server/VS Code completion and syntax surfaces.
  *(done — v0.11.0)* Added complete PASSWORD and KEYFILE signatures/examples, read and destination
  `Manage` authorization requirements, global-name behavior, atomic failure/retry semantics, and explicit
  transport-credential non-persistence. Corrected the stale Report-SQL claim that portal CREATE
  PASSWORD/KEYFILE modes make the managed cache portable: portal storage always uses the portal at-rest
  key, and portability is EXPORT→PUBLISH only. Added `$export-dataset` and `$publish-dataset` snippets,
  expanded VS Code highlighting for dataset transport clauses, regenerated the syntax index, and updated
  snippet-library coverage.
- [ ] Document portal at-rest key provisioning, validation, backup/restore, key-version metadata,
  rotation/recovery, and the explicitly supported development fallback. Add an operator runbook for
  orphan reconciliation and interrupted rotation.
- [ ] Confirm scheduled-refresh-as-admin is the only standing "trusted" path.
- [ ] Decide and document ownership/audit semantics for datasets published by admin, scheduled, or
  system identities, and the required permission level for publishing/moving into a folder.

### Files to modify / add (representative)

- Schema/registry: `src/ETL-SQL.ReportPortal/Data/PortalEntities.cs` (add `FolderId`, unique `Name`),
  new EF migration, `src/ETL-SQL.ReportPortal/Services/DatasetRegistryService.cs` (lookup-by-name,
  centralized access rule), `src/ETL-SQL.Core/Data/IDatasetRegistry.cs` (signatures).
- Engine: `src/ETL-SQL.Engine/Evaluator.cs` (caller-context field), the four
  `Handlers/{Use,Create,Refresh,ShowDatasets}StatementHandler.cs` (threaded caller, refresh split,
  at-rest read), `src/ETL-SQL.ReportHosting/DashboardService.cs` + portal `ExecutionJobService` (set
  caller). Remove sidecar secret.
- At-rest key + transport: `CryptoUtils`/`EncryptionOptions`; new EXPORT/PUBLISH AST + parser
  (`ReportAst.cs` / `ReportParser.cs` / `SystemParser.cs`) + handler(s).
- Security/lifecycle follow-up: central dataset permission service shared by registry/controller;
  `DatasetViewerService` at-rest metadata support; portal key validation/version/rotation service;
  atomic dataset file writer and orphan reconciliation; publish/move authorization and audit.
- Lint: `Analysis/Linting/Rules/DatasetEncrypt*Rule.cs` realign to transport-only.
- Examples: `samples/08_Reporting/datasets/*`. Tests: new `DatasetSecurityMatrixTests.cs`, extend
  `DatasetControllerTests.cs` + `DatasetPhase3/4Tests.cs`.

### Verification

1. `dotnet build ETL-SQL.slnx` — clean.
2. `dotnet test … --filter "Category!=Integration&Category!=Performance&Category!=SLT"` — matrix green;
   the cross-folder global-name `USE`, the PRIVATE cross-user denial, and the export→publish round-trip
   (all red before Phase 1–2) pass.
3. Headless deck:
   `dotnet run --project src/ETL-SQL.App -- run samples/08_Reporting/datasets/01_deploy_datasets.etlsql`
   then `02_`–`05_`.
4. Optional manual portal pass via the deck README checklist.
5. Inspect persisted job definitions and portal logs for known test credentials/key markers — zero
   matches. Force publish/refresh failures and cancellation; verify no plaintext temp files, partial
   ciphertext, orphan registry rows, or lost last-good cache remain.
6. Start portal with missing/invalid production key (must fail), restore with the backed-up key (datasets
   readable), then execute the rotation runbook and verify every dataset records the new key version.

> Convention: INT/TINYINT/BIGINT all materialize as `decimal` at runtime — dataset row assertions use
> `m` suffixes / `Convert.ToDecimal`, never int/long literals.
