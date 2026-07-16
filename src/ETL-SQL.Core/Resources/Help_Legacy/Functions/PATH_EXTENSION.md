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
- [Standard Library — §8.2 Path Operations](../../../../../Docs/Reference/Standard_Library.md#82-path-operations)
- Related: [`PATH_COMBINE`](PATH_COMBINE.md), [`PATH_FILENAME`](PATH_FILENAME.md), [`PATH_DIRECTORY`](PATH_DIRECTORY.md)
