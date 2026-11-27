# EXCEL_SHEET_LIST

Returns a table of worksheets (`SheetIndex`, `SheetName`) in a local Excel workbook (`.xlsx`, `.xls`, `.xlsb`) or an existing `EXCEL` connection, without reading cell data.

## Syntax

```sql
EXCEL_SHEET_LIST(path_or_connection)
```

- **path_or_connection** — Local file path to an Excel workbook (validated against the active file policy) or the name of an open `EXCEL` connection.

## Return Columns

- **SheetIndex** — 1-based worksheet position (`DECIMAL`).
- **SheetName** — Worksheet name (`STRING`).

> [!TIP]
> You can also query worksheet metadata directly from any `EXCEL` connection with `SELECT * FROM xl_conn._sheets`.

## Examples

```sql
-- Discover sheets from a workbook path
SELECT SheetIndex, SheetName
FROM EXCEL_SHEET_LIST('C:\Reports\Q4.xlsx')
ORDER BY SheetIndex;

-- Discover sheets from an existing EXCEL connection
CREATE CONNECTION xl_src AS EXCEL(PATH = 'C:\Reports\Q4.xlsx', HEADER = ON);
SELECT SheetIndex, SheetName FROM xl_src._sheets;
```

## References

- [EXCEL Connector](../../connectors/files/excel.md)
- [File & Path Functions](README.md)
- [Syntax Index](../../../syntax-index.md)
