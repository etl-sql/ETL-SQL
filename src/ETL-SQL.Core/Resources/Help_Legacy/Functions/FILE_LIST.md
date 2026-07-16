# FILE_LIST
Returns a table of files in a local directory matching optional criteria.

**Category:** File

## Syntax
```sql
FILE_LIST(path)
FILE_LIST(path, recursive)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `STRING` | Absolute path to the directory to list |
| `recursive` | `BIT` | Optional: `TRUE` to include subdirectories recursively (default: FALSE) |

## Returns
Table with columns: `NAME` (STRING), `PATH` (STRING), `EXTENSION` (STRING), `SIZE` (DECIMAL bytes), `LASTMODIFIED` (DATETIME), `ISREADONLY` (BIT), `CREATIONTIME` (DATETIME).

## Remarks
- Zero-Trust: `path` must pass `IExecutionContext.ResolvePath()` validation.
- Use `WHERE EXTENSION = '.csv'` to filter by file type.

## Example
```sql
-- List all CSVs in a directory
SELECT NAME, SIZE, LASTMODIFIED
INTO #incoming
FROM FILE_LIST('C:\Data\Incoming\')
WHERE EXTENSION = '.csv'
ORDER BY LASTMODIFIED DESC;

-- Recursive inventory
SELECT * FROM FILE_LIST('C:\Reports\', TRUE) WHERE SIZE > 1048576;  -- > 1 MB
```

## See Also
- [Standard Library — §14. File System Functions](../../../../../Docs/Reference/Standard_Library.md#14-file-system-functions)
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
- Related: [`REMOTE_FILE_LIST`](REMOTE_FILE_LIST.md), [`FILE_EXISTS`](FILE_EXISTS.md)
