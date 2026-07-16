# PATH_FILENAME
Extracts the filename and extension portion of a path.

**Category:** File / Path

## Syntax
```sql
PATH_FILENAME(path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `VARCHAR` / `STRING` | The file or directory path |

## Returns
`STRING` — The filename and extension. Returns `NULL` if input is `NULL`.

## Example
```sql
SELECT PATH_FILENAME('C:\Data\input.csv');  -- → 'input.csv'
```

## See Also
- [Standard Library — §8.2 Path Operations](../../../guides/getting-started.md#82-path-operations)
- Related: [`PATH_COMBINE`](path_combine.md), [`PATH_EXTENSION`](path_extension.md), [`PATH_DIRECTORY`](path_directory.md)
