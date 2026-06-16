# EXCEL
Reads from and writes to Excel workbooks (.xlsx, .xls). Specify a worksheet name and optionally a named range or cell range.

Syntax:
  CREATE CONNECTION <name> AS EXCEL(
    PATH     = 'file.xlsx',
    SHEET    = 'Sheet1',
    RANGE    = 'A1:D100',
    HEADER   = ON | OFF,
    ENCRYPT  = ON | OFF,
    PASSWORD = '<passphrase>'
  );

Options:
- **PATH** — workbook path (required)
- **SHEET** — worksheet name or index (default first sheet)
- **RANGE** — cell range to read/write (e.g. 'A1:F50' or a named range)
- **HEADER** — treat first row as column headers (default ON)
- **ENCRYPT** — encrypt the workbook on write (default OFF)
- **PASSWORD** — workbook password

```sql
CREATE CONNECTION Budget AS EXCEL(
  PATH   = 'C:\finance\budget_2024.xlsx',
  SHEET  = 'Summary',
  HEADER = ON
);

SELECT department, q1, q2, q3, q4
  INTO #budget
  FROM Budget;

PRINT 'Budget rows loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
