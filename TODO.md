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
- **Refresh:** transparent stale-cache refresh serves **stale-with-warning** to readers — only the
  **owner / scheduled job** re-materializes (never under a consumer's identity). Editing the source
  query or a forced `REFRESH DATASET` requires **editor/owner**. User runs are ACL-gated; **scheduled /
  system refresh jobs keep admin rights**.
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
  a never-materialized dataset errors instead of re-running the source. `REFRESH DATASET` and
  `CREATE OR ALTER DATASET` (over an existing dataset) require editor/owner via new
  `IDatasetRegistry.CanEditAsync` (admin/owner/Editor-or-Owner grant — mirrors `DatasetController.CanEdit`).
  `SHOW DATASETS` already caller-filtered (1c). Re-materialization now happens only via the producing
  report's `CREATE` (owner or scheduled/admin job). Tests: `DatasetPhase4Tests` refresh/create-or-alter
  denial + serve-stale + never-materialized; `PortalIntegrationTests.DatasetRegistry_CanEdit_OnlyOwnerEditorAndAdmin`.
- [ ] **1e. Portal-managed at-rest key.** Introduce an at-rest key abstraction bound to the service
  account, persisted where it can be backed up (config/keystore), replacing the implicit host-DPAPI
  assumption in the consume path. Engine parquet read for portal datasets uses this key (no credential at
  `USE`). Document the backup requirement.

### Phase 2 — Portable move (the "movable" story)

- [ ] **2a. EXPORT DATASET** `&x TO '<file>' ENCRYPT = PASSWORD|KEYFILE (PASSWORD='…' | KEYFILE='…')` —
  portable parquet encrypted with the transport credential (supplied at export, never sidecar'd). Reuse
  `CryptoUtils`/`EncryptionOptions`. New AST + parser case + handler.
- [ ] **2b. PUBLISH/IMPORT** a portable file into a portal — decrypt once with the supplied credential,
  re-encrypt with the portal at-rest key, register, mark **not movable**, surface the keep-your-original
  warning.
- [ ] **2c. Repurpose `ENCRYPT=PASSWORD|KEYFILE` on `CREATE DATASET` into a portal** (currently conflates
  transport with at-rest). Realign lint rules `DatasetEncryptWithoutKeyRule` / `DatasetEncryptionModeRule`
  to transport-only semantics.
- [ ] **2d. Remove the cleartext-credential sidecar** (`CreateDatasetStatementHandler` 260-275); any
  refresh sidecar carries no secret.

### Phase 3 — Verification deck (scripts + xUnit)

- [ ] **Runnable example deck** `samples/08_Reporting/datasets/` + `README.md` (tiny inline/CSV seed; no
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
- [ ] **Automated xUnit** — new `tests/ETL-SQL.Tests/Reporting/DatasetSecurityMatrixTests.cs` + extend
  `tests/ETL-SQL.ReportPortal.Tests/DatasetControllerTests.cs`. Build on `PortalIntegrationTests.cs`
  (real registry, ~920-1006) and crypto round-trips in `DatasetPhase2Tests.cs`:
  1. **Crypto portability (in-process — no 2nd machine):** at-rest key decrypts locally, swapped key
     throws; transport PASSWORD right/wrong; transport KEYFILE right/missing/wrong; ciphertext ≠
     plaintext. (Deterministic CI assertion on the Linux/keyfile path; Windows binds via DPAPI.)
  2. **Default round-trip:** CREATE folder A → `USE` from folder B by global name → rows match (red today).
  3. **Access model (1b/1c):** PUBLIC consumable with folder read, denied without; PRIVATE denied to
     non-owner, allowed to owner + explicit `DatasetAcl` grant; non-admin `SHOW` lists only visible.
  4. **Refresh split (1d):** non-owner stale → cached + warning, no re-run; `REFRESH`/query-edit denied to
     viewer, allowed to editor/owner; scheduled/system (admin) refreshes.
  5. **Export→Publish (Phase 2):** export w/ password/keyfile, re-import, decrypt once, consume by ACL
     with no credential; assert published copy is at-rest-encrypted and credential never sidecar'd.
  6. **Portal/engine parity:** a user forbidden by the HTTP API is also denied by `USE DATASET`.
  7. **Negatives:** duplicate global name rejected; orphaned `OwningReportId` → PRIVATE inaccessible to
     former owner; export missing credential → clear error.
  - Run: `dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT"`
    (Portal tests use WebApplicationFactory — no Docker).

### Phase 4 — Docs / residual decisions

- [ ] Update `Docs/Architecture/Reporting.md` (already stale) + user-facing portal docs: at-rest-vs-
  transport model, "not movable after publish / keep your original," PUBLIC=folder-read /
  PRIVATE=grant, at-rest key backup requirement.
- [ ] Confirm scheduled-refresh-as-admin is the only standing "trusted" path.

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

> Convention: INT/TINYINT/BIGINT all materialize as `decimal` at runtime — dataset row assertions use
> `m` suffixes / `Convert.ToDecimal`, never int/long literals.
