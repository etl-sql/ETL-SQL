# Architecture Decision Record: Pipelines and Queries as Portal Catalog Documents

**Status:** Accepted 2026-09-30: D1 A (one document with a kind), D2 A (run through the Orchestrator), D3 as recommended
**Target:** v0.20.0 (§4 "Make Portal ETL documents first-class")

---

## 1. Context

Studio in the Portal authors only Report-SQL (`.rptsql`). Its Home and New menus disable pipelines
and queries (`.etlsql`) and send the author to the Workstation Editor or VS Code. In a restricted
Enterprise or SaaS deployment neither is available, so an author there cannot build a pipeline at
all. Studio is meant to be the only editor such a deployment needs
([ETL-SQL Studio](etl-sql-studio.md)).

What the Portal already has for reports, and a pipeline needs too:

| Concern | Report today |
| --- | --- |
| Storage | `Report` row + script file at `ScriptPath` under the tenant's script root |
| Where it lives | A folder; folder and report ACLs (`Read`, `Execute`, `Author`, `Manage`) |
| Editing | Edit lease, optimistic `Version` with `If-Match`, recovery drafts, optional source-control write-back |
| Running | A report is *viewed*: `DashboardService` runs it read-mostly for readers |
| Interactive run in Studio | `POST /api/designer/run`: one read-only `SELECT`, capped rows, 15 s, audited as `AD_HOC_RUN` |

A pipeline differs in one way that matters: running it **writes** (to connections, files, tables)
and may call connectors and native SQL. The existing guardrails
([Portal editor strategy §5](portal-editor-strategy.md)) were written to keep interactive runs
read-only; a pipeline run is not.

## 2. Decisions to make

### D1. How a pipeline is stored

- **A (recommended). One catalog document with a kind.** Add `Kind` (`Report` | `Pipeline`) to the
  existing `Report` row, default `Report`, and store `.etlsql` scripts the same way. Folders, ACLs,
  leases, versions, drafts, source control, audit, search, and tenant isolation all apply unchanged.
  Surfaces that only make sense for reports (the viewer, subscriptions, alerts, share links, embed
  tokens, snapshots) refuse a `Pipeline` explicitly. A query is a pipeline opened in the script
  projection; it is the same file type, so it is not a third kind.
- **B.** A sibling `Pipeline` entity with its own ACL, version, lease, and draft tables. Clean
  naming, but every piece of governance above is built a second time and must be kept in step.

The rolling-expand migration contract allows A (a new column with a default).

### D2. How a pipeline runs from the Portal

- **A (recommended). Through the Orchestrator.** *Run* submits the saved version to the connected
  Orchestrator as a job run under the author's federated identity, in its sandbox, and Studio follows
  the run's status and log. Scheduling is the same handoff with a schedule. Nothing that writes runs
  inside the Portal process. With no Orchestrator connected, authoring, validation, and preview still
  work, and *Run* says what is missing.
- **B.** Run inside the Portal under a new `PipelineRun` capability, with limits and audit. Fastest
  feedback, but it puts arbitrary writes, file operations, and native SQL inside the Portal's own
  process and trust boundary, next to the secret store, which the sandbox work exists to avoid.
- **C.** No run from the Portal in this release: author, validate, preview, save, and schedule only.

Under every option, previews stay what they are today: one read-only statement at a time.

### D3. Who may do what

Recommended, reusing what exists:

| Action | Requires |
| --- | --- |
| See and open a pipeline | `Read` on its folder, `ScriptRead` capability |
| Create one | `Manage` on the folder (as for a report), `ScriptSave` |
| Edit and save | `Author` on the folder or pipeline, `ScriptSave`, the edit lease |
| Run or schedule (D2 A) | `Execute` on the pipeline **and** the Orchestrator's own run grant for the job |

No new capability is needed unless D2 B is chosen.

## 3. Proposed slices

1. **Storage and authoring.** `Kind` column; create, open, save, and reopen `.etlsql` in the Portal;
   New menu and Home enabled; leases, drafts, and source control apply; report-only surfaces refuse
   a pipeline. Proof: a Portal journey under an ordinary author creates, edits on the canvas, saves,
   and reopens a pipeline.
2. **Run through the Orchestrator** (if D2 A). Run the saved version, follow status and log, cancel;
   refuse clearly with no Orchestrator. Proof: a journey that runs a MOCKDB pipeline to completion
   and reads its ASSERT result.
3. **Schedule handoff.** Reuse slice 2's submission with a schedule.

## 4. Out of scope

- Multi-file pipelines (`RUN SCRIPT` across catalog documents) beyond resolving paths inside the
  tenant's script root.
- Migrating existing Orchestrator `ScriptRoot` files into the catalog.
