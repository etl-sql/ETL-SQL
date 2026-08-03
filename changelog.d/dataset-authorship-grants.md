### Security

- Dataset access no longer treats authorship as standing permission. `DatasetPermissionService` and `ReportDependencyService` short-circuited on `CreatedBy == userId`, so removing a user from every group — or from the directory — left every dataset they had ever created fully open to them, with no revocation gesture that could undo it. Permission resolution now reads grants only: a dataset's creator, and the author of the report that owns it, receive an explicit `Owner` grant in the new `DatasetUserAcls` table when the dataset is registered, so access can be revoked by deleting a row. Deleting a user cascades their grants away, and transferring ownership before deleting a user now moves the grant along with `CreatedBy`. The migration backfills a grant for every dataset that already has a creator, so nobody loses access on upgrade.

  Per-user dataset grants are a sibling table rather than a nullable `UserId` on `DatasetAcl`, because relaxing that column is an `AlterColumn` — rejected by the rolling-expand migration contract and implemented by SQLite as a table rebuild. The rule and its per-resource mechanisms are recorded in `docs/architecture/decisions/AuthorshipIsNotPermission.md`.

  One behavior change worth knowing: deleting the report that owns a private dataset used to revoke the report author's access, because that access was derived from `OwningReport.CreatedBy`. It no longer does — their grant is durable and explicit. The orphaned dataset therefore stays reachable by its author rather than becoming administrator-only, and the grant can be revoked.

### Fixed

- Fixed the PostgreSQL model snapshot, which had not been regenerated since the alert-notification and share-link-name migrations. Any new PostgreSQL migration scaffolded against it re-proposed operations those migrations had already applied — a migration that would have failed against every migrated database — and one entity carried an index over a column it does not have.
