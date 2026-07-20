# Advanced File Operations

Advanced filesystem statements that replace custom script or program implementations in data
integration pipelines: waiting on file arrival, transcoding, splitting/merging, directory sync, and
integrity verification. For the basic `COPY`/`MOVE`/`RENAME`/`DELETE`/`COMPRESS`/`ENCRYPT` commands see
[FILE Operations](file.md); for directory commands see [DIRECTORY](directory.md).

Each statement has its own page:

| Statement | Description |
| :--- | :--- |
| [WAITFOR FILE UNLOCKED](waitfor-file-unlocked.md) | Block until a file arrives and is no longer being written to. |
| [CONVERT FILE ENCODING](convert-file-encoding.md) | Stream-based transcoding between encodings. |
| [SPLIT FILE](split-file.md) | Split a large text file into chunks by row count or byte size. |
| [MERGE FILES](merge-files.md) | Concatenate multiple files into one, optionally stripping repeated CSV headers. |
| [SYNC DIRECTORY](sync-directory.md) | Mirror a source directory to a destination by modified time and size. |
| [VERIFY FILE INTEGRITY](verify-file-integrity.md) | Compute and validate file hashes. |

## Path resolution and directory connections

ETL-SQL supports path aliasing via connections. If a path string starts with a registered connection
name, the engine resolves it to the connection's base path. This applies to every statement above.

```sql
-- Define a logical name for a physical path
CREATE CONNECTION source_dir AS DIRECTORY('C:\Users\Chuck\Documents\Input');
CREATE CONNECTION backup_dir AS DIRECTORY('D:\Backups\Daily');

-- Use the alias instead of the full path in any file statement or function
COPY DIRECTORY source_dir TO backup_dir;
SELECT * FROM FILE_LIST(source_dir);

-- You can also append sub-paths to the alias
DELETE FILE 'source_dir/stale_lock.txt';
```

This pattern is recommended for scripts that move between environments (Dev/Test/Prod): it isolates the
physical path logic to a single `CREATE CONNECTION` block.

## References

- [FILE Operations](file.md)
- [DIRECTORY](directory.md)
- [TRANSFER](transfer.md)
- [File Operations index](README.md)
