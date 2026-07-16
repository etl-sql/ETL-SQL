# PATH_EXTENSION
Extracts the extension portion of a path (including the period `.`).

**Category:** File / Path

## Syntax
```sql
PATH_EXTENSION(path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `VARCHAR` / `STRING` | The file path |

## Returns
`STRING` — The extension (e.g. `.csv`), or an empty string if there is no extension. Returns `NULL` if input is `NULL`.

## Example
```sql
SELECT PATH_EXTENSION('C:\Data\input.csv');  -- → '.csv'
```

## See Also
- [Standard Library — §8.2 Path Operations](../../../guides/getting-started.md#82-path-operations)
- Related: [`PATH_COMBINE`](path_combine.md), [`PATH_FILENAME`](path_filename.md), [`PATH_DIRECTORY`](path_directory.md)
