# FILE_MODIFIED
Returns the last modification (write) timestamp of a local file.

**Category:** File / Metadata

## Syntax
```sql
FILE_MODIFIED(path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `VARCHAR` / `STRING` | The local path of the file |

## Returns
`DATETIME` — The last write timestamp of the file. Returns `NULL` if the file does not exist, or if path is `NULL`.

## Example
```sql
SELECT FILE_MODIFIED('C:\Data\input.csv');  -- → '2026-05-28 14:00:00'
```

## See Also
- [Standard Library — §8.1 File Operations](../../../guides/getting-started.md#81-file-operations)
- Related: [`FILE_SIZE`](file_size.md), [`FILE_HASH`](file_hash.md)
