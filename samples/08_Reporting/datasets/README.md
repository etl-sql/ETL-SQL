# Dataset Security Verification Deck

This deck verifies portal-managed datasets without an external database or data
file. The producer uses inline rows; the portal stores the materialized Parquet
files with its configured at-rest key.

The cross-script checks are portal-mode checks. A plain CLI process has no portal
registry, user identity, folder permissions, or persisted dataset catalog, so it
cannot truthfully exercise these authorization cases across separate runs.

## Setup

1. Configure and back up the portal dataset at-rest key as described in the
   [Report Portal Administrator Guide](../../../docs/guides/report-portal-admin.md).
2. Create portal folders `/Dataset Deck/Producer`, `/Dataset Deck/Consumers`,
   and `/Dataset Deck/Imports`.
3. Create two non-admin test users: `dataset-owner` and `dataset-viewer`.
4. Give `dataset-owner` publish/manage rights on Producer and Imports.
5. Give `dataset-viewer` read access to Producer and Consumers, but no dataset
   administration role.
6. Copy `05_export_then_publish.etlsql` to a secure working location and replace
   its `C:\ETL-SQL-Dataset-Deck` output directory and repository key paths with
   absolute paths valid on the portal host.

The RSA key under `samples/10_Kitchen_Sinks/test_key/` is public test material.
Never use it for real data.

## Execution

1. As `dataset-owner`, deploy and run `01_deploy_datasets.etlsql` from Producer.
   `SHOW DATASETS` must list `&sales_public` and `&sales_private`.
2. Deploy `02_report_public_consumer.etlsql` to Consumers and run it as
   `dataset-viewer`. It must return the three public rows even though the
   consumer is in another folder.
3. Run `03_report_private_allowed.etlsql` as `dataset-owner`. It must return the
   two private rows.
4. Run `04_report_private_denied.etlsql` as `dataset-viewer`. `USE DATASET` must
   fail as not found, and the trailing `SELECT` must not execute.
5. Grant `dataset-viewer` read permission on `&sales_private` and rerun step 4.
   It must now return the private rows. Revoke the grant afterward.
6. Grant only the dataset `Refresh` permission to `dataset-viewer`. Confirm the
   user can refresh `&sales_public`, but cannot edit its query, move it, delete
   it, or perform unrelated administrative operations.
7. As `dataset-owner`, run the prepared `05_export_then_publish.etlsql`.
   Both imported datasets must load without resupplying a transport credential.

The published names are globally unique. Delete prior copies before rerunning
the entire transfer script, or change the two `AS &name` values.

## Security Checks

- Copy a portal-managed dataset `.parquet` file outside the portal data root and
  try to open it as ordinary Parquet. It must fail because it remains encrypted
  with the portal at-rest key.
- Retain the password/key-file export if the dataset may need to move again.
  The imported portal cache is deliberately not a portable transport artifact.
- Search portal logs, scheduled-job definitions, and the portal SQLite database
  for `deck-password-change-me`. There must be no persisted match.
- Run the password publish with a wrong password and the key-file publish with a
  nonmatching private key. Each attempt must fail without leaving a catalog row,
  plaintext temporary file, or partial destination cache.
- Remove read access to Producer from `dataset-viewer`. The public consumer must
  then be denied: `PUBLIC` means readable by users who can read the owning
  folder, not anonymous access.

## Local Syntax Check

The files can be parser-checked locally, but scripts `02` through `05` require
portal execution for their intended behavior:

```powershell
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj `
  --filter "FullyQualifiedName~DatasetExampleDeckTests"
```
