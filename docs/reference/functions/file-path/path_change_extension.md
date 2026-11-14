# PATH_CHANGE_EXTENSION

Replaces or removes the extension of a file path.

## Syntax

```sql
PATH_CHANGE_EXTENSION(path, ext)
```

## Parameters

- **path** — `STRING`. File path to update.
- **ext** — `STRING`. New extension (both `'.bak'` and `'bak'` are accepted), or `''` to remove the extension.

## Returns

Returns the modified path as a `STRING`.

## Null Behavior

Returns `NULL` when `path` or `ext` is `NULL`.

## Remarks

Pure string path manipulation; performs no filesystem I/O and does not rename files on disk. Passing `''` removes the extension without leaving a trailing period.

## Example

```sql
SELECT
    PATH_CHANGE_EXTENSION('staging/orders.csv', '.bak') AS with_dot,
    PATH_CHANGE_EXTENSION('staging/orders.csv', 'parquet') AS without_dot,
    PATH_CHANGE_EXTENSION('staging/orders.csv', '') AS stripped;
```

## References

- [File and Path Functions](README.md)
- [PATH_EXTENSION](path_extension.md)
- [PATH_FILENAME_NO_EXT](path_filename_no_ext.md)
- [PATH_DIRECTORY](path_directory.md)
