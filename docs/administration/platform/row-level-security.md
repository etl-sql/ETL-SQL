# Row-Level Security

### 4.x Row-Level Security (report data filtering)

Folder and dataset permissions control **which reports a user can open** — the coarse-grained gate.
Row-level security (RLS) is the finer layer: it lets a report author filter the *rows* a viewer sees
based on the viewer's identity, so one report can serve every user their own slice of the data.

**How authors write it.** The engine exposes the authenticated viewer's identity to report SQL as
read-only system variables and predicate functions, populated by the Portal from the signed-in user:

| Primitive | Meaning |
| :--- | :--- |
| `@@CURRENT_USER` / `@@CURRENT_USER_ID` | The viewer's username / id. |
| `@@REAL_USER` | The actual actor — differs from `@@CURRENT_USER` only under admin impersonation. |
| `@@IS_ADMIN` | Whether the effective viewer is an administrator. |
| `HAS_GROUP('name')` | TRUE if the viewer belongs to the group (case-insensitive). |
| `HAS_ROLE('name')` | TRUE if the viewer holds the Portal role. |

Groups come from Portal group membership **and OIDC group claims** (synced at login). A typical
row-filtered report:

```sql
SELECT r.* FROM sales r
WHERE HAS_GROUP('Region:' + r.RegionCode);   -- membership test, not a substring match
```

**Security properties administrators should know:**

- **The identity is not forgeable.** These variables are injected by the Portal from the authenticated
  principal; a script cannot assign them (`SET @@CURRENT_USER = …` is rejected) and report parameters
  cannot populate them.
- **Admins bypass RLS by default.** `HAS_GROUP` / `HAS_ROLE` return TRUE for administrators so they see
  all rows. Set `Portal:Security:AdminBypassRowLevelSecurity` to `false` to filter admins by the same
  predicates as everyone else.
- **Fail-closed.** If no identity is present (e.g. a non-interactive run), `HAS_GROUP` returns FALSE and
  `@@CURRENT_USER` is null, so a well-formed predicate returns **no rows** rather than leaking all rows.
- **No shared snapshot.** A report that references any identity primitive is automatically treated as
  identity-sensitive: it is executed per viewer and its result is **never** cached as a shared snapshot,
  so one user's filtered rows can never be served to another. These reports run fresh on each view
  rather than from the snapshot cache.
- **Predicate integrity depends on report change control.** RLS lives in the report's SQL, so the
  existing publish-permission and published-hash checks are what prevent an author from removing the
  filter. Treat edit/publish rights on RLS reports accordingly.

**Admin impersonation.** An administrator can reproduce what a specific user sees via
`POST /api/reports/{id}/execute-as/{targetUserId}`. The run filters rows as the target user (including
the target's — not the admin's — bypass status), while the audit log records the real admin acting as
the target (`EXECUTE_REPORT_AS`). Impersonated runs are never cached.

> Full design and threat model: `docs/architecture/decisions/RowLevelSecurity.md`.

---

