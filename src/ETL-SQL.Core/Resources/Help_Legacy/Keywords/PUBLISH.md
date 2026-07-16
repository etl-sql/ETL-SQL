# PUBLISH
Publishes either a versioned Orchestrator bundle or a portable dataset export.

## Syntax
```sql
PUBLISH BUNDLE 'bundle-name'
FROM 'C:\ETL\bundle-folder'
ENTRY 'main.etlsql'
WITH (PASSWORD = 'publish-password', ENCRYPT = MACHINE);

PUBLISH DATASET
FROM 'C:\Transfer\sales.parquet'
AS &sales_imported
INTO '/Finance/Imported'
ACCESS PRIVATE
ENCRYPT = PASSWORD
PASSWORD = 'transport-secret';

PUBLISH DATASET
FROM 'C:\Transfer\sales.parquet'
AS &sales_imported
INTO '/Finance/Imported'
ACCESS PUBLIC
ENCRYPT = KEYFILE
KEYFILE = 'C:\Transfer\keys\dataset_transport';
```

## Notes
- Directory sources include every `.etlsql` and `.rptsql` file under the source directory.
- Single-file sources include the entry file and literal relative `RUN SCRIPT 'child.etlsql'` dependencies recursively.
- Dynamic `RUN SCRIPT @path` dependencies cannot be published; use live file mode.
- If content is unchanged from the latest version, the existing version is reused.
- Published copies remove `USE PASSWORD` statements after secrets are re-encrypted for the Orchestrator lockbox.
- Run published scripts with `RUN SCRIPT 'orch://bundle-name@version/main.etlsql';`.
- `PUBLISH DATASET` is portal-only. The destination folder must exist and the caller must have folder
  `Manage`; the dataset name must be globally unique.
- The source export is decrypted once with its transport credential, then re-encrypted with the portal
  at-rest key. The published copy is not movable, so retain the original export.
- Failed publication removes its allocated row and partial files. Transport credentials are never persisted.

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
