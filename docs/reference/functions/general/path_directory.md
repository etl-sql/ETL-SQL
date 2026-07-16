# PATH_DIRECTORY
Extracts the directory information portion of a path.

**Category:** File / Path

## Syntax
```sql
PATH_DIRECTORY(path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `VARCHAR` / `STRING` | The file or directory path |

## Returns
`STRING` — The directory path, or `NULL` if input is `NULL` or contains no directory information.

## Example
```sql
SELECT PATH_DIRECTORY('C:\Data\SubDir\input.csv');  -- → 'C:\Data\SubDir'
```

## See Also
- [Standard Library — §8.2 Path Operations](../../../guides/getting-started.md#82-path-operations)
- Related: [`PATH_COMBINE`](path_combine.md), [`PATH_FILENAME`](path_filename.md), [`PATH_EXTENSION`](path_extension.md)
