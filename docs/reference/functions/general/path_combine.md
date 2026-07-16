# PATH_COMBINE
Combines multiple path segments into a single file/directory path securely.

**Category:** File / Path

## Syntax
```sql
PATH_COMBINE(p1, p2 [, ...])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `p1` | `VARCHAR` / `STRING` | The first path segment |
| `p2` | `VARCHAR` / `STRING` | The second path segment |
| `...` | `VARCHAR` / `STRING` | Additional path segments |

## Returns
`STRING` — The combined path string. Skips `NULL` segments. Returns `NULL` if all segments are `NULL`.

## Example
```sql
SELECT PATH_COMBINE('C:\Data', 'SubDir', 'file.txt'); -- → 'C:\Data\SubDir\file.txt'
```

## See Also
- [Standard Library — §8.2 Path Operations](../../../guides/getting-started.md#82-path-operations)
- Related: [`PATH_FILENAME`](path_filename.md), [`PATH_EXTENSION`](path_extension.md), [`PATH_DIRECTORY`](path_directory.md)
