# GEMINI-003 — Pin the Shared Backup Surface Inventory

## Objective

Create one machine-readable inventory of tenant-owned Shared state and a drift test that forces every
new persisted surface to declare its backup/restore disposition. This is groundwork for the open
Shared backup/PITR TODO; it is not an implementation of export or restore.

Today tenant deletion, portability export, Portal EF entities, Orchestrator relational tables, and
artifact roots encode overlapping inventories in different places. Before implementing a safe
Shared restore, make omissions visible and reviewable.

## Read first

- `AGENTS.md`
- `TODO.md` Shared backup/recovery and portability entries
- `docs/architecture/TenantPortability.md`
- `src/ETL-SQL.Portal.Data/PortalDbContext.cs`
- `src/ETL-SQL.Portal.Data/PortalEntities.cs`
- `src/ETL-SQL.Portal/Services/SharedTenantLifecycleService.cs`
- `src/ETL-SQL.Orchestrator/Storage/SharedTenantLifecycleStore.cs`
- `src/ETL-SQL.Orchestrator/Storage/RelationalSandboxAdmissionLedger.cs`
- `src/ETL-SQL.Orchestrator/Storage/RelationalTenantMeteringLedger.cs`
- `src/ETL-SQL.App/App/Portability/`
- Existing Shared lifecycle, provider, migration-convergence, and portability tests

## Required model

Add a small provider-neutral inventory model in the narrowest shared project that both the inventory
tests and future backup code can consume without introducing a circular dependency. Each entry must
contain fixed metadata only:

- stable surface identifier;
- owning host (`Portal`, `Orchestrator`, or `Artifact`);
- physical table or artifact area;
- partition mode (`DirectTenantColumn`, `TenantRootJoin`, `TenantPrefix`, `Global`, or `Tombstone`);
- authoritative tenant column/root;
- backup disposition (`Required`, `OptionalContent`, or `Excluded`);
- restore disposition (`RestoreDisabled`, `Rebind`, `Rebuild`, or `Exclude`);
- deletion disposition;
- concise reason for every exclusion.

Do not put SQL, paths, credentials, payload samples, or tenant values in this inventory. This is a
schema/surface declaration, not runtime authority.

## Classification rules

- Direct tenant tables must name their immutable `TenantId` column.
- Dependent tables without `TenantId` must name the tenant-qualified root used for the join.
- Global identity/configuration tables must not be casually labeled tenant-owned.
- Lifecycle/audit tombstones intentionally retained after deletion must be explicit.
- Secrets, resolved key material, refresh tokens, leases, active/retained admissions, caches, and
  checkpoints require explicit exclusions or restore behavior; silence is a test failure.
- Artifact entries must distinguish scripts, datasets, snapshots, maps, keys, scratch/spill,
  checkpoints, and temporary decrypted content.
- The inventory does not authorize reads. Future export code must still require a verified or
  platform-authorized server-derived tenant context.

## Drift tests

Add tests that fail with an actionable list when persisted state is missing from the inventory:

1. Enumerate all `PortalDbContext` EF entity table mappings, including Identity tables, and require
   each table to be classified.
2. Enumerate or centrally expose all Orchestrator tables created by the provider-neutral relational
   store components and require each to be classified. Avoid brittle regex over arbitrary source if
   a small table-name catalog can be introduced cleanly.
3. Require every artifact area/root used by Shared execution, Portal storage, and tenant lifecycle
   deletion to be classified.
4. Assert that every surface deleted by Shared lifecycle is in the inventory and that every
   tenant-owned `Required` surface has a declared deletion action.
5. Assert that excluded secrets/key material/in-flight authority cannot accidentally be marked
   restorable.
6. Assert stable unique surface IDs and no duplicate physical surface declaration.

The test may reveal existing unclassified surfaces. Classify them from actual source behavior; do
not change deletion/export semantics in this packet merely to make the inventory prettier. If the
correct classification is unclear, leave the test failure and report the exact surface for senior
review rather than guessing.

## Documentation

Add an architecture-facing generated-or-maintained table derived from the inventory showing the
surface, partition mode, and disposition. Do not expose implementation secrets or turn the table
into a claim that Shared restore works.

The document must state:

- inventory coverage is not backup correctness;
- PITR filtering, consistent snapshots, encrypted export, collision-safe import, disabled-work
  restore, cache rebuild, and hostile cross-tenant recovery remain open;
- EF-managed Portal identifiers retain their existing PascalCase convention; hand-authored store
  identifiers are not renamed in this task.

## Tests to run

At minimum, run the new inventory suite plus:

```powershell
dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj `
  --filter "FullyQualifiedName~SharedTenantLifecycleServiceTests|FullyQualifiedName~MigrationConvergenceTests" -m:1

dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj `
  --filter "FullyQualifiedName~SharedTenantLifecycleStoreTests|FullyQualifiedName~TenantBundle" -m:1
```

## Prohibited shortcuts

- Do not implement restore in this packet.
- Do not infer tenant ownership from a caller-provided ID.
- Do not mark every unknown table Global or Excluded.
- Do not rename EF tables or columns.
- Do not edit `TODO.md`, claim PITR safety, or mark Shared backup complete.

## Acceptance criteria

- One authoritative inventory represents Portal, Orchestrator, and artifact surfaces.
- Drift tests fail when a new persisted surface lacks a disposition.
- Existing lifecycle and portability tests remain green.
- Ambiguous classifications are reported rather than guessed.
- The result is one focused commit suitable for senior security review.
