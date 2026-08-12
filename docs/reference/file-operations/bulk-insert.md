# BULK INSERT

BULK INSERT streams a flat file into a table in bounded batches. It validates values at the target
boundary and reports rejected rows, mapping warnings, and successful rows separately.

## Syntax

```sql
BULK INSERT <target_table> [(target_column, ...)]
FROM '<file_path>'
WITH (
  FIELDTERMINATOR = ',',
  ROWTERMINATOR = '\n',
  FIRSTROW = 2,
  HEADER = TRUE,
  MAPPING = 'NAME',
  BATCHSIZE = 1000,
  MAXERRORS = 0
);
```

## Options

- **FIELDTERMINATOR** — Source field delimiter.
- **ROWTERMINATOR** — Source row delimiter.
- **FIRSTROW** — First data row, using 1-based file rows. `FIRSTROW = 2` treats row 1 as a header.
- **HEADER** — `TRUE`/`FALSE`; explicitly identifies whether row 1 contains column names.
- **MAPPING** — `NAME` (default when a header is present) or `POSITION`. Name mapping is
  case-insensitive and all-or-nothing; use `POSITION` for T-SQL ordinal compatibility.
- **BATCHSIZE** — Rows per write batch (default: the engine batch size).
- **MAXERRORS** — Rejected target rows tolerated before aborting (default: `0`).

With a header, ETL-SQL maps requested target columns by name when every name is present. Otherwise
it falls back to ordinal mapping with a warning. A source/target width mismatch also warns: surplus
source columns are ignored and unmatched target columns remain `NULL`. If `FIRSTROW`/`HEADER` is
omitted but the first row exactly matches the requested target names, the load fails rather than
silently inserting the header as data.

```sql
BULK INSERT SalesDB.dbo.StagedOrders FROM 'C:\data\orders_2024.csv'
  WITH (
    BATCHSIZE = 5000,
    MAXERRORS = 10,
    FIRSTROW = 2
  );

PRINT 'Loaded: ' + @@ROWCOUNT;
```

Use a FLATFILE connection for additional parsing controls such as encoding and fixed-width layouts.

## References

- [File Operations](README.md)
- [FLATFILE Connector](../connectors/files/flatfile.md)
