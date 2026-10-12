# Studio Private Unpublished Drafts

**Decision:** ST12, 2026-10-12. Defines the policy ST13–ST15 implement; it does not claim any of
them is built.

## Problem

Portal Studio can keep work in two places today, and both need a catalog document first:

- A **catalog document**. Creating one needs `ScriptSave`, `ReportPublish` and `Manage` on a folder.
- A **recovery draft**: unsaved edits to a catalog document the caller may author, kept for 7 days.

A learner with neither publish rights nor a writable folder can open Studio and build a report, but
Save answers "Not Saved" and the work lives only in that browser tab.

## Decisions

Decided with the owner on 2026-10-12.

| Question | Decision |
| :--- | :--- |
| Where a draft lives | Its own table, keyed by tenant and owner, with its own draft ID. It is not a catalog document and has no catalog report ID until it is published. |
| Who owns it | The Portal host. Script text is never kept in browser storage. |
| Who may read it | Its owner, inside the owner's tenant. To every other caller, administrators included, it answers exactly as a draft that does not exist. |
| Who may keep one | A caller with `StudioAccess` and `ScriptRead`. Neither `ReportPublish` nor a folder grant is needed. |
| Retention | Discarded after 90 days without a save. Removed with the owner's account. |
| How many | 25 per owner. A further create is refused, not queued. |
| Practice access | The read-only preview Studio already gives unsaved text, under the caller's existing capability. No Orchestrator run and no schedule until the draft is published. |
| Revisions | Every save has a version number and names the version it started from; a stale save is refused as a conflict. The last 10 saved texts are kept. |
| Publishing | The existing catalog create action, with its existing checks. Success removes the draft and records its history in the audit trail; a refusal leaves the draft as it was. |

**What "history" means on publish.** The catalog keeps no earlier versions of a document, so the
published document carries the draft's last saved text and nothing before it. The audit entry
`PUBLISH_PRIVATE_DRAFT` records the draft's ID and creation time, the version published, and for
every kept save its version number, time and content hash. The text of earlier saves is discarded
with the draft. An author who wants an earlier text copies it out before publishing.

## Settings

All four live under `Portal:Studio` in `appsettings.json`.

| Setting | Default | Meaning |
| :--- | :--- | :--- |
| `PrivateDrafts` | `true` | Set to `false` where unpublished script text must not be stored. Existing drafts can still be deleted. |
| `PrivateDraftRetentionDays` | `90` | Days a draft is kept after its last save. |
| `PrivateDraftRevisionsKept` | `10` | Saved texts kept per draft, newest first. |
| `PrivateDraftsPerUser` | `25` | Drafts one owner may hold. |

A value below 1 is treated as 1.

## What a draft may hold

A draft has had none of the checks a catalog save runs, so the same rule as a recovery draft
applies: text that assigns a credential as a plain literal is refused and not kept
(`StudioDraftPolicy`). The script size limit is the catalog's (`DesignerLimits:MaxScriptCharacters`).

A draft is a report or a pipeline. The kind is fixed when the draft is created, because it decides
which catalog document publishing creates.

## What a draft is not

- **Not listed.** It does not appear in the catalog, the report library, search, reader routes, the
  configuration export or a promotion.
- **Not executable.** Nothing schedules it, no Orchestrator job names it, and the pipeline run and
  schedule routes address catalog documents only. Preview executes the text the browser sends, as it
  does for any unsaved document; it does not read the draft.
- **Not shared.** There is no grant, lease or approval on a draft. One owner edits it, so a version
  check is the whole of its concurrency control.
- **Not a recovery draft.** A recovery draft holds edits that were never saved. A private draft is
  saved work. ST14 adds recovery of unsaved edits *to* a private draft.

## Consequences

- An administrator cannot read a learner's draft through the Portal. Support reads the database
  under the deployment's own access controls, and the audit trail records creation, deletion and
  publication without the text.
- Turning `PrivateDrafts` off stops new saves at once and leaves stored drafts to expire.
- Publishing is the only way a draft's text reaches a place other people can read. That step keeps
  every existing check: capability, folder `Manage`, name uniqueness and source-control validation.

## Acceptance

- ST13: a caller without folder `Manage` or `ReportPublish` creates, saves, closes and reopens a
  draft; another principal and another tenant get the same answer as for a missing draft.
- ST14: unsaved edits to that draft survive a browser restart and a renewed sign-in; a draft that
  moved on is shown as a conflict; a refused text is reported, not silently dropped.
- ST15: publishing keeps the text and records the draft's revisions; an attempt without the
  required rights leaves the draft intact.

## Source basis

- [StudioController.RecoveryDrafts](../../../src/ETL-SQL.Portal/Controllers/StudioController.RecoveryDrafts.cs):
  the existing per-author, per-report recovery store and its refusals.
- [StudioController](../../../src/ETL-SQL.Portal/Controllers/StudioController.cs): the catalog create
  action publishing reuses.
- [StudioDraftPolicy](../../../src/ETL-SQL.Reporting/Authoring/StudioDraftPolicy.cs): the plaintext
  credential rule.
- [Report authoring inventory](studio-report-authoring-inventory.md) R20 and
  [Script/ETL inventory](studio-script-etl-authoring-inventory.md) E22: the gaps this closes.
