# Advanced File Operations

Advanced filesystem statements that replace custom script or program implementations in data
integration pipelines: waiting on file arrival, transcoding, splitting/merging, directory sync, and
integrity verification. For the basic `COPY`/`MOVE`/`RENAME`/`DELETE`/`COMPRESS`/`ENCRYPT` commands see
[FILE Operations](file.md); for directory commands see [DIRECTORY](directory.md).

## Path resolution and directory connections

ETL-SQL supports path aliasing via connections. If a path string starts with a registered connection
name, the engine resolves it to the connection's base path.

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

## WAITFOR FILE UNLOCKED

Blocks pipeline execution until a file arrives on the filesystem and is fully unlocked (not being
written to by another process).

```sql
WAITFOR FILE UNLOCKED '<path>' [WITH(TIMEOUT = <seconds>, POLL_INTERVAL_MS = <ms>)];
```

- **TIMEOUT** — Maximum seconds to wait before throwing a timeout exception (default: `30`).
- **POLL_INTERVAL_MS** — Polling check interval in milliseconds (default: `500`).

## CONVERT FILE ENCODING

Performs stream-based transcoding from one encoding standard to another.

```sql
CONVERT FILE ENCODING '<source>' TO '<destination>' WITH(FROM_ENCODING = '<enc>', TO_ENCODING = '<enc>' [, OVERWRITE = ON|OFF]);
```

- **FROM_ENCODING** (required) — Source encoding (e.g. `UTF8`, `ANSI`, `ASCII`, `UNICODE`, `UTF32`).
- **TO_ENCODING** (required) — Target encoding.
- **OVERWRITE** — Replace the destination if it already exists (default: `ON`).

## SPLIT FILE

Splits a larger text file into multiple chunk files based on row count or byte size.

```sql
SPLIT FILE '<source>' TO '<destination_dir>' WITH(LIMIT_TYPE = 'ROWS'|'SIZE', LIMIT_VALUE = <val> [, PREFIX = '<prefix>', OVERWRITE = ON|OFF]);
```

- **LIMIT_TYPE** (required) — Split strategy, `ROWS` or `SIZE`.
- **LIMIT_VALUE** (required) — Number of rows or a size limit (e.g. `1000` for ROWS, `50MB`/`100KB` for SIZE).
- **PREFIX** — Name prefix for generated part files (default: `part_`).
- **OVERWRITE** — Replace existing part files in the destination directory (default: `ON`).

## MERGE FILES

Concatenates multiple files (supports wildcards or array inputs) into a single destination file.

```sql
MERGE FILES '<source_pattern>' TO '<destination>' [WITH(HEADER = ON|OFF, OVERWRITE = ON|OFF)];
```

- **HEADER** — If `ON`, assumes files are CSVs and strips the header row from subsequent files during the merge (default: `ON`).
- **OVERWRITE** — Overwrite the destination file if it exists (default: `ON`).

## SYNC DIRECTORY

Mirrors a source directory to a destination directory, doing fast file transfers based on modified
times and sizes.

```sql
SYNC DIRECTORY '<source_dir>' TO '<destination_dir>' [WITH(DELETE_EXTRA = ON|OFF, OVERWRITE = ON|OFF, RECURSIVE = ON|OFF)];
```

- **DELETE_EXTRA** — Delete files in the destination that do not exist in the source (default: `OFF`).
- **OVERWRITE** — Overwrite modified/changed files (default: `ON`).
- **RECURSIVE** — Traverse directories recursively (default: `OFF`).

## VERIFY FILE INTEGRITY

Computes file hashes and validates them against expected hex strings or a companion checksum file.

```sql
VERIFY FILE INTEGRITY '<source>' WITH(EXPECTED_HASH = '<hash>' | HASH_FILE = '<path>' [, ALGORITHM = 'SHA256'|'SHA1'|'MD5'|'SHA512']);
```

- **EXPECTED_HASH** or **HASH_FILE** (one required) — A direct expected hash string, or the path to a companion checksum file (e.g. `.sha256`).
- **ALGORITHM** — Hash computation algorithm (default: `SHA256`).

## References

- [FILE Operations](file.md)
- [DIRECTORY](directory.md)
- [TRANSFER](transfer.md)
- [File Operations index](README.md)
