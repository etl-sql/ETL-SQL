# Report Versioning and Promotion

> **Applies to:** Team · Enterprise · SaaS

Manage dataset at-rest encryption keys, perform in-place Portal upgrades safely, rotate keys, and recover from interrupted rotation or orphaned dataset files.

> [!TIP]
> See [Publishing Reports](publishing.md) for the overview hub, or [Report Publishing Workflows](report-publishing-workflows.md) for day-to-day publishing tasks.

---

## Dataset At-Rest Key Lifecycle

Production portals require `Portal:Dataset:AtRestKey` — a base64 value decoding to at least 32 bytes. Generate it with a cryptographically secure random generator, store it in the portal's secret manager, and set a non-secret `Portal:Dataset:AtRestKeyVersion` such as `2026-01`.

At startup, the Portal validates the current key, every `PreviousAtRestKeys` entry, and `LegacyAtRestKeyVersion`. Startup is fatal when a required key is missing, is not valid base64, decodes to fewer than 32 bytes, reuses the current version as a previous version, or names a legacy version that cannot be resolved.

> [!CAUTION]
> `Portal:Dataset:AllowMachineFallback=true` is supported only for deliberate development/standalone use. It creates host-bound caches that cannot be restored on another host.

---

## Backup and Restore

A **complete** portal backup is one coordinated set:

- Portal database (`portal.db` or PostgreSQL)
- Orchestrator database (`etlsql.db` or PostgreSQL)
- `Portal:ScriptRootPath`
- `Portal:SnapshotDirectory`
- `Portal:DatasetRootPath`
- Data Protection key ring
- Configuration (JWT secret, dataset at-rest key/versions, Orchestrator API key)

**Backup procedure:**
1. Stop writes or take a coordinated snapshot.
2. Back up all items in the set above.
3. Restore all items as one unit — do not start the Portal with only the database or dataset directory.
4. Start the Portal and verify dataset reads before retiring the backup.

> [!IMPORTANT]
> **Dataset cache files are referenced by absolute path in the catalog.** Restore `Portal:DatasetRootPath` to its **original absolute path** (or rewrite the catalog paths) — a dataset whose cache moves to a different directory will not be found, and startup storage reconciliation will treat the moved file as an orphan.

---

## Rotating the At-Rest Key

To rotate from `v1` to `v2`:

1. Configure the new key and version in `appsettings.json`:

```json
{
  "Dataset": {
    "AtRestKey": "<new-v2-base64-key>",
    "AtRestKeyVersion": "v2",
    "PreviousAtRestKeys": {
      "v1": "<old-v1-base64-key>"
    },
    "LegacyAtRestKeyVersion": "v1",
    "AllowMachineFallback": false
  }
}
```

2. Restart the Portal, then call `POST /api/admin/datasets/rotate-at-rest-key`.

Rotation processes datasets in stable ID order and commits each file and version independently. A failed dataset keeps its old file and version; rerun the same endpoint to resume. Readers can use both current and configured previous versions during the rotation window.

3. After the response reports no failures and every dataset row records `v2`: take a new backup, remove `LegacyAtRestKeyVersion`, and remove `v1` from `PreviousAtRestKeys`. Do not retire the old key until old backups have expired or their recovery procedure retains that key separately.

### Interrupted Rotation

Rotation is resumable per dataset. If the request is cancelled or a dataset fails:

1. Keep the current and previous key mappings unchanged.
2. Restart the Portal. Startup reconciliation removes abandoned `.rotate-*`, `.tmp-*`, and `.bak-*` staging files under `DatasetRootPath`.
3. Review the rotation response and Portal logs for failed dataset names.
4. Correct missing files, permissions, or key-version mappings.
5. Call `POST /api/admin/datasets/rotate-at-rest-key` again. Datasets already at the target version are skipped.
6. Retire the previous key only after every catalog row reports the target version.

---

## In-Place Upgrades

On startup, the Portal runs any pending EF Core schema migrations automatically. Upgrades are **forward-only**: an in-place upgrade preserves authentication, folder permissions, durable execution jobs, subscriptions, datasets and their at-rest key version, and audit history.

Rolling upgrade migrations follow an expand/migrate/contract discipline. New columns are added nullable with defaults, so pre-upgrade rows remain valid.

**Upgrade procedure:**
1. **Take a complete coordinated backup first.** This backup *is* your rollback path.
2. Stop the Portal (and Orchestrator service) so no writes are in flight during migration.
3. Deploy the new binaries and start the Portal. Pending migrations apply automatically before it serves requests.
4. Verify after startup: admin login, a representative protected report, a dataset read (confirms the at-rest key still decrypts caches), and that scheduled subscriptions/jobs are still present.

> [!WARNING]
> **Rollback is restore-from-backup, not a down-migration.** EF migrations ship `Down` methods, but reverting a partially-applied or completed upgrade by running them against production data is **not a supported recovery path**. Redeploy the previous binaries and restore the pre-upgrade coordinated backup. Keep the pre-upgrade backup until the new release has been verified in production.

---

## Dataset Orphan Reconciliation

The Portal runs dataset storage reconciliation automatically during startup (before serving requests). It is intentionally limited to the top level of `DatasetRootPath`:

- Abandoned transaction and rotation staging files are deleted.
- Catalog rows with an empty path or a missing managed cache file are deleted.
- Unreferenced files matching the managed `<safe-name>_<id>.parquet` naming pattern are deleted.
- Files outside `DatasetRootPath`, nested files, and files that do not match the managed naming pattern are not adopted or deleted.

**Operator procedure:**
1. Back up `portal.db` and `DatasetRootPath` before manually repairing catalog or filesystem state.
2. Stop the Portal and inspect both sides together. Do not rename managed files to make them appear referenced; their stable dataset ID is part of the filename contract.
3. Restore a missing referenced cache from the coordinated backup before startup. If no valid cache exists, allow reconciliation to remove the stale row, then republish or rerun the producing report.
4. Move suspected unmanaged files outside `DatasetRootPath` before startup if they need investigation.
5. Start the Portal and inspect `DatasetStorageMaintenance` log entries for each removed row or file.
6. Query `eng.tables` and exercise representative reads after reconciliation.

---

## Related

- [Publishing Reports](publishing.md) — overview hub
- [Report Publishing Workflows](report-publishing-workflows.md) — publishing procedures and metadata tags
- [Embed Tokens and Sharing](embed-tokens-and-sharing.md) — share links, embed tokens, saved views
- [Portal Configuration](../platform/config/portal-configuration.md) — `Portal:Dataset:*` and `Portal:KeyManagement:*` keys
- [Backup, Monitoring, and Health](../platform/backup-and-monitoring.md)
- [Portal Administration](README.md)
