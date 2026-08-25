# DIRECTORY

Treats a local or UNC filesystem folder as a data source for file-management operations (`COPY FILE`,
`DELETE FILE`, etc.) and directory listing via `SELECT`.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute directory path | Yes (structured) |
| `CREATE` | `ON`/`OFF` — create the directory if it doesn't exist (default: `ON`) | No |

## Result-set schema

Querying a `DIRECTORY` connection via `SELECT` returns:

- `FileName` (STRING) — filename with extension.
- `Path` (STRING) — absolute path to the file.
- `Extension` (STRING) — file extension (including dot).
- `Size` (DECIMAL) — file size in bytes.
- `LastModified` (DATETIME) — last write time.
- `IsReadOnly` (BIT) — `TRUE` if the file is read-only.
- `CreationTime` (DATETIME) — time the file was created.

## Authentication

Directory connector uses host process file system permissions.

## Examples

```sql
CREATE CONNECTION data_dir AS DIRECTORY('C:\Data\Incoming', CREATE=ON);

-- List all files in the directory as a result set
SELECT FileName, Size, LastModified FROM data_dir;
```

## Troubleshooting

- **Access Denied**: Ensure process identity has read/write permissions on the target directory.
- **Path Resolution**: Relative paths resolve against script root via `context.ResolvePath()`.

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [DIRECTORY operations](../../file-operations/directory.md)
