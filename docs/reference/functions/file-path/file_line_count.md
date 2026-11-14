# FILE_LINE_COUNT

Streams a local file once and returns the number of lines, optionally skipping header lines.

## Syntax

```sql
FILE_LINE_COUNT(path [, header_rows])
```

## Parameters

- **path** — `STRING`. Local file path to inspect. Relative paths are resolved through the execution context.
- **header_rows** — Optional non-negative integer (`INT` / `BIGINT`), default `0`. Number of leading lines to skip (for example `1` for a column header row, or `n` for multi-line banners).

## Returns

Returns the remaining line count as a `DECIMAL` (numeric) value, clamped at `0` when the file has fewer than `header_rows` lines.

## Null Behavior

Returns `NULL` when `path` is `NULL`, `header_rows` is `NULL`, or the file does not exist.

## Remarks

- Recognizes `\n`, `\r\n`, and `\r` line endings.
- A trailing newline terminates the final line without adding an extra empty line; a non-empty final line without a trailing newline counts as a line; an empty file (`0` bytes) returns `0`.
- Streams the file in chunks without loading the whole file into memory and honors cancellation.
- Throws an error when `header_rows` is negative or not an integer.
- Subject to the same path boundary and file authorization rules as `FILE_SIZE`.

## Example

```sql
DECLARE @data_rows BIGINT = FILE_LINE_COUNT('staging/orders.csv', 1);
PRINT 'Data rows: ' + CAST(@data_rows AS VARCHAR);
```

## References

- [File and Path Functions](README.md)
- [FILE_SIZE](file_size.md)
- [FILE_EXISTS](file_exists.md)
