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
- [Standard Library — §8.2 Path Operations](../../../../../Docs/Reference/Standard_Library.md#82-path-operations)
- Related: [`PATH_COMBINE`](PATH_COMBINE.md), [`PATH_FILENAME`](PATH_FILENAME.md), [`PATH_EXTENSION`](PATH_EXTENSION.md)
