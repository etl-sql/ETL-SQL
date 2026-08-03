# Authorship Is Not Permission

**Status:** decided and implemented for reports (v0.17.0) and datasets (v0.18.0).

## The rule

Creating a resource does not grant standing access to it. Access always resolves from a grant that
someone can revoke.

The failure this prevents is deprovisioning that does not deprovision. When permission resolution
short-circuits on `CreatedBy == userId`, removing a person from every group — or from the directory
entirely — revokes nothing they ever authored, because the comparison keeps succeeding for as long
as the row remembers their id. There is no revocation gesture that can undo it, which is what makes
it different from an over-broad grant.

That regression shipped into a release cycle once, in five places at once. It was caught by two
pre-existing tests during the release gate, after a hand review of the same diff had cleared it. The
full triage is in [v0.17.0-code-review.md](v0.17.0-code-review.md#h-1--fixed-report-authorship-survived-deprovisioning-privilege-persistence).

## How each resource kind satisfies it

Reports and datasets reach the rule differently, because their ACL tables differ.

| Resource | Mechanism |
| :--- | :--- |
| **Report** | Authorship **upgrades** an existing grant. `report.CreatedBy == userId && (folderPerm.HasValue \|\| directPerm.HasValue)` yields `Manage`. A creator who keeps folder access or a report ACL still administers their report; one with neither gets nothing. `ReportAcl` can name a user directly, so a surviving grant is always expressible. |
| **Dataset** | Authorship is **replaced by a real grant**. `DatasetAcl` is group-scoped only, so there was no per-user grant for authorship to upgrade — a creator's only route to their own private dataset was the short-circuit. Removing it alone would have hidden a freshly created dataset from its author. Instead, `DatasetRegistryService` writes an explicit `Owner` row in `DatasetUserAcls` at registration, for the dataset's creator and for the author of the report that owns it, and permission resolution reads grants only. |
| **Folder** | Ownership *is* standing permission, deliberately. `Folder.OwnerId` implies `Manage` with no ACL, because a folder is the thing grants are attached to and its owner administers it. Ownership is transferable, and `AdminController` requires a transfer target before deleting a user who owns folders. |

## Why `DatasetUserAcls` is a separate table

The obvious shape is a nullable `UserId` on `DatasetAcl`, matching `ReportAcl`. That requires
relaxing `DatasetAcl.GroupId` to nullable, which is an `AlterColumn` — rejected by the rolling-expand
migration contract (`MigrationConvergenceTests.PortalMigrations_UpOperationsFollowRollingExpandContract`)
and implemented by SQLite as a full table rebuild. Adding a table is additive and deploys safely
under a rolling upgrade, so dataset user grants live in their own table.

The `AddDatasetUserAcls` migration backfills an `Owner` grant for every dataset that already has a
creator, and for every dataset whose owning report has one. Without that backfill, deleting the
short-circuit would have silently revoked access to every dataset in an existing deployment.

## What revokes a dataset grant

- **Deleting the user.** `DatasetUserAcl.UserId` cascades, so their grants go with the row. This is
  the deprovisioning path the rule exists for.
- **Transferring ownership.** Deleting a user who owns datasets requires a `reassignTo` target;
  the transfer moves `CreatedBy` *and* writes the new owner's `Owner` grant, because access no
  longer follows `CreatedBy`.
- **Removing the row directly.** `GET /api/datasets/{id}/acl` lists group and user grants together,
  each with a `principalKind`, and `DELETE /api/datasets/{id}/acl/user/{userId}` revokes a direct
  grant and invalidates that user's sessions. The Admin dataset permissions panel shows both kinds.
  A grant an administrator cannot see is a grant they cannot account for, and one they cannot revoke
  makes "authorship is revocable" true only in the database.

## Delivery-time re-authorization

Interactive access is not the only way a resource reaches someone. Anything the Portal *sends* has
to re-check the recipient at send time, because the send happens long after the grant was made.

| Path | Behaviour |
| :--- | :--- |
| **Subscriptions** | `SubscriptionDeliveryService.AuthorizeAsync` requires the owner to exist, be active, and hold folder `Read` (or be an admin) before every delivery. |
| **Alerts** | `PortalAlertEvaluationService` applies the same check before evaluating. Unauthorized alerts are skipped whole — evaluating one and suppressing only the dispatch would record a `TRIGGERED` transition nobody was told about, and the notification would never fire again even after access was restored. |
| **Saved views** | Read/write routes resolve report permission first, then narrow to the caller's own rows, so losing report access takes the views with it. |
| **Shared connections** | No authorship path: `PortalConnectionCatalogService` resolves admin, then group ACLs. `CreatedByUserId` is recorded but never consulted for authorization. |

An alert notification carries the value that crossed the threshold, so an alert outliving its
author's access is data leakage, not just stale metadata — which is what made this the one real gap
the audit found.

## Guardrail

`AuthorshipPermissionBoundaryTests` (in `ETL-SQL.Tests/Architecture`) inventories every
`CreatedBy`/`OwnerId` comparison in the Portal along with the reason it is safe, and asserts the
inventory equals the live source. A new comparison fails the build until someone justifies it; a
removed one forces its entry out. The inventory can only shrink or change deliberately.

Reading a diff is not a reliable way to catch this class of bug — that has been tried and it failed.
The inventory is.

## References

- [v0.17.0 code review](v0.17.0-code-review.md) — the original regression and its report-side fix.
- [Row-Level Security](RowLevelSecurity.md) — row filtering, which composes with these grants.
- `TODO.md` → *Portal — Authorship Is Not Permission* — remaining work for connections,
  subscriptions, alerts, and saved views.
