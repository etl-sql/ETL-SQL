# FILE_SIZE
Returns the size of a local file in bytes.

**Category:** File / Metadata

## Syntax
```sql
FILE_SIZE(path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `VARCHAR` / `STRING` | The local path of the file |

## Returns
`DECIMAL` — The file size in bytes. Returns `NULL` if the file does not exist, or if path is `NULL`.

## Example
```sql
SELECT FILE_SIZE('C:\Data\input.csv');  -- → 2048
```

## See Also
- [Standard Library — §8.1 File Operations](../../../guides/getting-started.md#81-file-operations)
- Related: [`FILE_HASH`](file_hash.md), [`FILE_MODIFIED`](file_modified.md)
