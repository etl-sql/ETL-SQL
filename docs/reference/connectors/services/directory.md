# DIRECTORY
Connects to a local or UNC file-system folder. SELECT returns a listing of files and subdirectories with their metadata.

Syntax:
  CREATE CONNECTION <name> AS DIRECTORY(
    PATH    = 'C:\data\exports',
    CREATE  = ON | OFF
  );

Options:
- **PATH** — folder path (required)
- **CREATE** — create the directory if it does not exist (default OFF)

```sql
CREATE CONNECTION Exports AS DIRECTORY(
  PATH    = 'C:\data\exports',
  CREATE  = ON
);

-- List CSV files modified in the last day
SELECT FileName, Size, LastModified
  INTO #files
  FROM Exports
  WHERE Extension = '.csv'
    AND LastModified >= DATEADD(DAY, -1, GETDATE());

PRINT 'Files found: ' + @@ROWCOUNT;
```

For file-level operations (copy, move, delete, compress, encrypt) use the FILE and DIRECTORY operation keywords rather than this connector.

References:
- [Data Connectors](../../../guides/administration.md)
