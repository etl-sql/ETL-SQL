# Author Bookmarks

**Status:** Accepted
**Phase:** 9 — Author Bookmarks
**Date:** 2026-08-23

## Problem

Authors need source-controlled report states that atomically apply parameters, active page, and
supported presentation state. A bookmark is distinct from a user-created Portal saved view and from
transient URL state.

## Three-Part Model

1. **Author bookmarks** — `CREATE BOOKMARK` defines shared, source-controlled report state declared
   in `.rptsql` scripts. They are versioned with the report, visible to all viewers, and applied
   atomically through the cascading-parameter engine.

2. **Portal saved views** — Private per-user snapshots persisted in the Portal database. They use the
   same versioned resolved-state envelope as author bookmarks but are not source-controlled. Each
   saved view records the report revision it was created against for stale reconciliation.

3. **URL state** — Contains only a bookmark or saved-view identifier (`#bookmark=Name` or
   `#view=ViewId`). Arbitrary parameter values, filter state, presentation state, and search terms
   are never placed in URLs, browser history, referrers, logs, or generated share links.

## Canonical Syntax

```sql
CREATE BOOKMARK WestCoastDetail AS (
  TITLE = 'West Coast Detail',
  PARAMETERS (
    @region = 'West',
    @year = 2026
  ),
  PAGE = Detail,
  STATE (
    FilterPanel.COLLAPSED = ON,
    DetailChart.VISIBLE = ON
  ),
  DEFAULT = ON
);
```

### Clauses

| Clause | Required | Description |
|--------|----------|-------------|
| `TITLE` | No | Display name shown in bookmark pickers |
| `PARAMETERS (...)` | No | Typed parameter assignments |
| `PAGE` | No | Target page name |
| `STATE (...)` | No | Named object VISIBLE/COLLAPSED state |
| `DEFAULT = ON` | No | At most one bookmark may be default |

### Action

```sql
ACTIONS (ON_CLICK = APPLY_BOOKMARK(WestCoastDetail))
```

## Resolved Bookmark State (v1)

The first delivery includes:

- Typed parameter/control values
- Active page
- Named visual/container `VISIBLE` state
- Named container/object `COLLAPSED` state

Cross-filter selections are durable only when represented by declared parameters.

### Explicitly Deferred

- Hover and tooltip state
- Animation
- Scroll position
- Maximized visuals
- Table paging and transient search
- Arbitrary CSS class/color mutations
- Matrix expansion
- Sort state
- Drill paths

## Launch Precedence

1. Explicit URL bookmark (`#bookmark=Name`)
2. Explicitly selected Portal saved view
3. User's default Portal saved view
4. Author-defined default bookmark (`DEFAULT = ON`)
5. Declared parameter and navigation defaults

A stale personal view must never prevent the base report from opening.

## Atomic Application Contract

When a bookmark is applied:

1. Resolve the bookmark definition from the manifest.
2. Validate every parameter, page, visual, and container reference against the current manifest.
3. Validate parameter types against declared metadata.
4. Stage all parameter changes together (no sequential browser requests).
5. Run cascading-parameter reconciliation through `ReportInteractionRefresher`.
6. Stage affected visual refreshes.
7. Validate page and presentation state references.
8. Publish one completed manifest via reference swap.
9. Roll back everything on failure (restore snapshot variables).

## Portal Saved View Convergence

### Entity Changes

`SavedReportView` gains:

- `StateJson` — Versioned resolved-state envelope (same schema as bookmark state)
- `ScriptHash` — SHA-256 of the report script at view-creation time

Existing `ParametersJson` and `FiltersJson` remain for backward compatibility. New views populate
`StateJson`; loading prefers `StateJson` when present.

### Stale Reconciliation

When a saved view's `ScriptHash` differs from the current report:

- Unknown parameters are silently dropped
- Unknown page/visual/container references produce warnings but do not block
- The base report always opens successfully

### Workflow

- Save As — creates a new named view from current state
- Update — overwrites an existing view
- Make Default — sets this view as the user's auto-apply default
- Delete — removes the view
- Reset to Report Default — clears the active view and applies launch precedence

### Distinguishing Author Bookmarks from Saved Views

The picker shows two sections:
- **Author bookmarks** — from the manifest, read-only, shared
- **My saved views** — from the database, per-user, editable

## Offline Behavior

Author bookmarks serialize into `layout.json` in the `.etlsnap` package. Offline replay applies
bookmarks through the same atomic contract without Portal access. Personal saved views are not
included in offline snapshots.

The reader-facing host is `etl-sql-report offline`, which decrypts the package and writes a
self-contained HTML viewer that sets `window.__ETLSNAP__`. One consequence is worth recording: a
file opened from disk has an opaque origin, so `history.replaceState` throws and the identifier-only
`#bookmark=` hash is not written. The state still applies atomically; only the shareable-link
convenience is unavailable, and the failure is swallowed rather than aborting the application.

## URL Privacy

- `#bookmark=WestCoastDetail` — identifier only
- `#view=42` — saved view ID only
- No parameter values, filter state, search terms, or presentation state in URLs
- `hashchange` listener applies the referenced bookmark or view atomically

## Validation Rules

| Rule | Diagnostic |
|------|-----------|
| Duplicate bookmark identifiers | Error |
| Multiple `DEFAULT = ON` bookmarks | Error |
| Reference to undefined page | Error |
| Reference to undefined visual/container in STATE | Error |
| Reference to undeclared parameter | Error |
| Parameter type mismatch | Warning |
| `APPLY_BOOKMARK` referencing unknown bookmark | Error |

## Dependencies

- Cascading parameter engine (Phase 6)
- Manifest serialization
- Report Builder round-trip (Phase 1)
- LSP completion/hover/snippet infrastructure

## References

- [Cascading Slicers ADR](cascading-slicers-and-atomic-parameters.md)
- [Report-SQL Guide](../../guides/feature-guides/report-sql.md)
- [Language Syntax Standards](../standards/language-syntax-standards.md)
