# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment promise are defined in [`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## Enterprise Policy Enforcement & Monitoring

*Completes the enterprise controls for protected enrollment and authoritative client runtime. Standalone installations remain unenrolled, unrestricted by organization policy, and independent of network services.*

### Shipped Scope
- **Machine Enrollment:** Machine-level enrollment, protected bootstrap, trust key, machine identity, and enroll/status/unenroll CLI.
- **Signed Policy Retrieval:** Tenant-bound RSA-PSS signed policy retrieval, protected cache, rollback/expiry checks, final configuration precedence, diagnostics, dynamic reload, and fail-closed host refresh.
- **Policy Authority & Operation-Boundary Enforcement:** Administrator policy-authority API and Portal workflow for validating, versioning, publishing, superseding, activating, and rolling back signed organization policies.
- **Shared Runtime Enforcement:** Filesystem path traversal checks, network/connector destination rules, and process resource boundaries.

### Future Candidate Phases

#### Phase 4: Operations Control Plane

##### 4.4 Historical capacity planning and sizing
- [x] Produce sizing and trend reports that distinguish CPU, memory, storage, connector, database, and concurrency bottlenecks.
- [x] Add saturation indicators and forecast thresholds so administrators can identify when to scale up, scale out, repartition workloads, or adjust schedules.
- [x] Document benchmark-to-production sizing guidance and clearly state where measured workload history is required instead of synthetic estimates.

##### 4.5 Alerting and service objectives
- [x] Define recommended SLIs/SLOs for availability, queue wait, execution success/latency, freshness, policy availability, audit/security delivery, database health, and recovery.
- [ ] Add configurable alerts for queue age/depth, sustained CPU or memory pressure, repeated spills, failed/retried jobs, stale snapshots/datasets, policy/signature failures, certificate expiry, outbox backlog, disk pressure, storage growth, database connectivity/pool exhaustion, and unhealthy fleet nodes.
- [x] Support alert routing through standard observability systems rather than building a proprietary pager; include deduplication, severity, recovery notifications, and runbook links in emitted signals.
- [x] Provide baseline thresholds but require administrators to tune them from measured workload and business criticality.

##### 4.6 HA topology and failure certification
- [ ] Publish supported standalone, departmental, and HA reference topologies with exact requirements for PostgreSQL, load balancing, shared artifact storage, certificates, DNS, service supervision, and network trust boundaries.
- [ ] Certify node loss, process crash, network partition, PostgreSQL failover, shared-storage outage, duplicate scheduler leadership, orphaned work, and recovery without duplicate or lost mutations.
- [ ] Document which components ETL-SQL coordinates and which remain responsibilities of PostgreSQL, load balancers, object/file storage, Kubernetes, Windows Services/systemd, or other infrastructure.
- [ ] Add topology-aware health and readiness checks so load balancers remove unsafe nodes without hiding whole-environment failures.

##### 4.7 Disaster recovery objectives
- [ ] Define supported RPO/RTO targets for each reference topology and identify the state, artifact, key, certificate, policy, outbox, and external dependency included in each target.
- [ ] Add scheduled restore drills that verify database consistency, artifact references, encrypted data/key availability, policy enrollment, service accounts, audit/security continuity, and orchestrator recovery.
- [ ] Prevent cloned restores from silently reusing machine identity or client credentials in another environment; require deliberate re-enrollment and credential rotation.
- [ ] Produce a machine-readable recovery report with achieved RPO/RTO, missing dependencies, data loss window, and operator actions.
- [ ] Document regional/site failure, split custody, backup retention, immutable/offline backup, and emergency access procedures.

##### 4.8 Searchable Portal Documentation Hub
- [ ] Compile the repository's markdown document library (cookbooks, reference guides, manuals) into a unified, searchable static website using a static site generator (e.g., MkDocs or Docusaurus).
- [ ] Host the compiled documentation site natively inside the Report Portal (e.g. under a `/docs` route) to allow administrators, analysts, and business users to search and navigate documentation in their web browser.
- [ ] Reconcile the static site's theme and search indices with the Portal's user interface, ensuring sensitive configurations remain excluded from the compiled index.

##### Prioritization gates
- [ ] Rank each workstream using measured administrative pain, customer deployment scale, security impact, and dependency on external infrastructure.
- [ ] Preserve scoped read-only fleet aggregation by default; any future remote mutation or upgrade command requires a separate threat model, authorization design, approval workflow, and audit contract.
- [ ] Complete threat-model and senior security review with all high-severity findings resolved.
- [ ] Pass full functional, performance, migration, recovery, enterprise certification, and standalone regression suites.
- [ ] Confirm documentation never claims OS-level containment against administrators or arbitrary alternate executables; mandate WDAC/AppLocker or equivalent controls where that boundary is required.

## Adaptive Execution & Extended Large-Data Certification

*Improves streaming scan, filter, projection, low-cardinality aggregation, and spill-backed `#temp` staging efficiency and concurrency under bounded-memory behavior.*

### Shipped Scope
- **Allocation Budgets:** Budgeting memory and garbage collection targets at scale (10M / 50M rows and 1B scale certification).
- **Adaptive Execution Controller:** Adaptive worker admission, concurrency caps, batch/memory grant setpoints, and spill writes.

### Future Candidate Phases
- [ ] **Schema-Resilient Flat File Modes:** Extend the `FLATFILE` (CSV/Excel) connectors with runtime resilience options such as `IGNORE_EXTRA_COLUMNS = ON`, `NULL_MISSING_COLUMNS = ON`, and `MAP_BY_HEADER_NAME = ON` to gracefully handle vendor structure drift without throwing validation crashes.
  - *Scope notes:* `MAP_BY_HEADER_NAME` shifts column binding from positional to name-based — define behavior for missing, duplicate, reordered, and renamed headers explicitly; prefer a small set of strictness *levels* over independent booleans that can contradict one another (name-mapping implies tolerating reorder).
  - Resilience must not become invisible data loss: pair silent `NULL_MISSING_COLUMNS` / coercion with a diagnostic and a rejected/coerced **row count**, and consider an optional bad-row quarantine sink.
  - Define the interaction with the linter's `SchemaValidationRule` and with `EXPECT` (warn vs. error vs. accept).

---

## Shared Connection & Secret Governance

*Features per-connection use ACLs, connection/secret impact inventories, and sensitive metadata classification.*

### Shipped Scope
- **Connection Governance:** Shipped per-connection use ACLs to authorize which users/processes can request a connection from the catalog.
- **Impact Inventory:** Added dependency/impact inventories for shared connections and secrets to trace usages before deletion or rotation.
- **Sensitive Metadata:** Added organization-designated sensitive metadata controls.

### Future Candidate Phases
- [ ] **Catalog approval workflow (optional):** Propose-and-approve workflow on shared connection creation/update/deletion for organizations that need four-eyes control.
- [ ] **SSH & PGP Key Management Portal** *(candidate — may not make the cut; requires a threat model first)*: a Portal dashboard for administrators to generate, rotate, and bind PGP/SSH key pairs to connection catalog entries.
  - *Scope notes / risk:* this is the highest-risk item in the DX/governance backlog. A web "export private key" button directly contradicts the zero-trust posture (the secret vault never releases material; the policy authority never exports private keys).
  - Keep private keys in the vault / OS store and **never render them in the browser**; public-key export is fine; **private-key export must be disallowed or a separate, four-eyes-gated, audited operation**. Align with the shipped DPAPI-M secret store. Do not start design until the threat model is complete.

---

## Review Workflow & Data Stewardship

*Combines steward-facing governance with four-eyes review, certification, impact analysis, and tag-driven policy enforcement.*

Strategy: [`Docs/Strategy/Data_Stewardship_Strategy.md`](Docs/Strategy/Data_Stewardship_Strategy.md)

### Future Candidate Phases

- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
  - Add queries and documentation for missing owner, steward, contact, classification, and quality metadata.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, stale-lineage views, and per-steward queues.
- [ ] **Phase 3: Review, Approval & Certification Workflow**
  - Add review and certification state for datasets, reports, jobs, and key lineage targets.
  - Require four-eyes approval for configured critical actions, including report publication and production job changes.
  - Enforce segregation of duties so users cannot approve their own changes.
  - Re-evaluate pending and approved requests when permissions, ownership, or user status changes.
  - Audit requests, comments, decisions, certification changes, and rejections while keeping export/import script-first.
- [ ] **Phase 4: Tag-Driven Policy Enforcement**
  - Extend Governance Core to block or warn based on lineage tags and classification metadata.
- [ ] **Phase 5: Impact Analysis**
  - Surface upstream and downstream impact for tables, columns, jobs, scripts, datasets, reports, subscriptions, owners, and stewards.
- [ ] **Phase 6: Quality & Freshness Stewardship**
  - Tie `EXPECT` and validation outcomes, freshness, SLAs, and quality trends to lineage targets.
  - **Quality Gate Attestation:** Expose quality-gate expectation run results directly on visual report cards and designer views in the Report Portal as dynamic "Verified Data" attestation badges.
- [ ] **Phase 7: External Catalog Sync**
  - Add stable external IDs, conflict rules, and reconciliation reports for external catalog integration.

---

## Visual Reporting & Dashboard Designer

*Improves interactive visual editing, page-level auto-interactions, and compiled snapshot formatting in the Report Portal and VS Code extension.*

### Future Candidate Phases

#### Phase 1: Visual Layout & Interaction Enhancements
- [ ] **Snapshot-Backed Layout Designing:** Allow the Report Designer to load and deserialize the last successfully compiled `.etlsnap` package. Visuals render on the grid canvas with historical snapshot data instead of empty wireframe placeholders, giving a "live-like" design experience without hitting production databases.
  - *Scope notes:* snapshot rows are **real data** — the designer must apply the **same row-level security as viewing** (RLS-filtered/sampled/redacted snapshot), so a designer never sees rows they could not see in the report. Cap/sample large snapshots to avoid loading millions of rows into the browser canvas.
- [ ] **Page-Level Auto-Interactions** *(near-term candidate — needs finer scoping)*: a page-level default (e.g. `CREATE PAGE Overview AS DASHBOARD ( INTERACTIONS ( DEFAULT = FILTER ) )`) that auto-wires cross-visual filtering on click selections between visuals sharing common source fields, reducing visual-level `INTERACTIONS` boilerplate.
  - *Scope notes:* auto-wiring is only useful if it is predictable — prefer **lineage-based** field matching over name matching, make every auto-wire **visible and per-visual overridable** in the designer, and provide a clean opt-out. This is the detail that decides whether the feature delights or surprises.

---

## Developer Experience (IDE & Tooling)

*Enhances authoring efficiency, visual design, and code generation within the Report Portal, VS Code Extension, and Terminal UI (TUI).*

> **Shared dependency:** the Portal script editor's schema autocomplete, Smart Snippets, and the
> schema-aware parts of `TEST CONNECTION` all rely on the same capability — **schema introspection**.
> Build one shared, cached, ACL-gated schema-snapshot service (see `PortalEditorStrategy.md` B1) and
> make it the single dependency for all three rather than three parallel introspection paths.

### Future Candidate Phases

#### Phase 1: Visual Diagnostics & Intelligent Code Generation
- [ ] **VS Code Visual Flow (DAG) Webview:** Port the Orchestrator's AST-to-DAG rendering into a VS Code extension panel. "Show Visual Flow" generates a read-only, interactive diagram of the pipeline (flat files → temp tables/queries → database targets), replicating the visual-flow benefit of SSIS.
  - *Scope notes:* largely a reuse/packaging effort — the canonical `renderDag` already exists and the `sync-assets` pipeline already targets VS Code media. Start **read-only + on-demand refresh**; defer live-sync.
- [ ] **Smart Snippets (Schema-Aware Code Generator)** *(near-term candidate — needs finer scoping / slicing)*: extend Language Server autocomplete to generate complex query boilerplate. See [SmartSnippetsSpec.md](Docs/Design/SmartSnippetsSpec.md). Triggering a smart snippet (e.g. `/merge <src> <dest>` or `/upsert <src> <dest>`) launches a schema-mapping wizard:
  - **Key Auto-Detection:** parse schemas and pre-select primary keys, or prompt for custom unique matching keys.
  - **Fuzzy-String Alignment:** propose column alignment (e.g. `src.FullName` → `dest.Name`) using string-similarity, with inline approve/change overrides.
  - **Type Coercion & Audit Presets:** suggest type casts (e.g. `CAST(S.col AS INT)`) for mismatched types and map audit columns (`UpdatedAt` → `GETDATE()`).
  - **Optimized Templates:** generate optimized `MERGE` blocks (change-detecting) or split `UPDATE`/`INSERT` queries for performance-critical targets.
  - *Scope notes:* correctness risk is high — auto-inserted CASTs or auto-mapped keys can silently produce wrong data, so **every inference is a proposal the user approves, never auto-applied** (especially fuzzy alignment and type coercion). **Reuse the shared schema cache**, do not build a third introspection path. Slice it: ship `/merge` with explicit key selection first; add fuzzy alignment and coercion as later, opt-in layers.
- [ ] **Unified Notebook & Script Execution:** Unify the `.etlnb` notebook REPL with plain-text `.etlsql` scripts by treating top-level labels as virtual cell boundaries. See [UnifiedNotebookScriptExecution.md](Docs/Design/UnifiedNotebookScriptExecution.md):
  - **Virtual Cell Slicing:** the Language Server segments flat scripts into execution blocks bounded by top-level labels, rendering CodeLens controls (`[Run Cell]` / `[Run Below]`).
  - **Checkpoint Bootstrapping:** initialize a REPL session directly from an `.etlsnap` package, letting developers debug-resume a failed production pipeline from any label checkpoint.
  - **Stateless JIT Pre-Scanning:** a pre-scan pass evaluates declarations (`DECLARE`, `CREATE CONNECTION`) before running a target cell so a cold start has a valid runtime.
  - *Scope notes:* pre-scan safety is the crux — define a crisp **allowlist of pre-scannable statement types** (declarations/connections only). Pre-scan must **never** auto-run a side-effecting statement (e.g. a `DELETE`/`INSERT` above the target cell); those are skipped, not executed.
- [ ] **First-Class Portal Script Editor:** Upgrade the Portal's script editor from a basic text area to a high-fidelity development interface for SaaS/large-farm environments. See the detailed design spec in [PortalEditorStrategy.md](Docs/Design/PortalEditorStrategy.md). Approach (reassessed 2026-07): **CodeMirror 6 + stateless server-side analysis + a schema API** — *not* Monaco and *not* a per-session Language Server, which conflict with the bounded-resource/multi-tenant model.
  - **Real-engine diagnostics:** keep CodeMirror 6; add a debounced, stateless `POST /api/designer/analyze` that reuses the `ETL-SQL.Analysis` linter (same rules as VS Code/CLI) and renders results as CodeMirror squiggles — no per-session server process.
  - **Schema autocomplete:** a shared, cached, ACL-gated schema-snapshot service plus a stateless completion endpoint feeding CodeMirror autocomplete.
  - **Governed interactive runs:** server-enforced `TOP 100` + short timeouts + a memory ceiling, executed under the logged-in user's RLS/identity context, with every run audited (`AD_HOC_RUN`).
  - **Optional git write-back:** when a git backend is configured, save commits on behalf of the user to preserve the source-controlled-report promise.
