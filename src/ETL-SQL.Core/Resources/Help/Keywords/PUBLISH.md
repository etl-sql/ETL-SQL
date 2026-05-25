# PUBLISH BUNDLE
Stores a versioned script bundle in the Orchestrator lockbox.

## Syntax
```sql
PUBLISH BUNDLE 'bundle-name'
FROM 'C:\ETL\bundle-folder'
ENTRY 'main.etlsql'
WITH (PASSWORD = 'publish-password', ENCRYPT = MACHINE);
```

## Notes
- Directory sources include every `.etlsql` and `.rptsql` file under the source directory.
- Single-file sources include the entry file and literal relative `RUN SCRIPT 'child.etlsql'` dependencies recursively.
- Dynamic `RUN SCRIPT @path` dependencies cannot be published; use live file mode.
- If content is unchanged from the latest version, the existing version is reused.
- Published copies remove `USE PASSWORD` statements after secrets are re-encrypted for the Orchestrator lockbox.
- Run published scripts with `RUN SCRIPT 'orch://bundle-name@version/main.etlsql';`.

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
