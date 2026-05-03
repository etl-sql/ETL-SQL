# DIRECTORY
Connects to a local or UNC file-system folder. SELECT returns a listing of files and subdirectories with their metadata.

Syntax:
  CREATE CONNECTION <name> ON DIRECTORY(
    PATH    = 'C:\data\exports',
    CREATE  = ON | OFF,
    FILTER  = '*.csv',
    RECURSE = ON | OFF
  );

Options:
  PATH    — folder path (required)
  CREATE  — create the directory if it does not exist (default OFF)
  FILTER  — file name glob pattern (e.g. '*.csv', '*.txt')
  RECURSE — include subdirectory contents in SELECT output (default OFF)

```sql
CREATE CONNECTION Exports ON DIRECTORY(
  PATH    = 'C:\data\exports',
  CREATE  = ON,
  FILTER  = '*.csv'
);

-- List CSV files modified in the last day
SELECT name, size, modified_at
  INTO #files
  FROM Exports
  WHERE modified_at >= DATEADD(DAY, -1, GETDATE());

PRINT 'Files found: ' + @@ROWCOUNT;
```

For file-level operations (copy, move, delete, compress, encrypt) use the FILE and DIRECTORY operation keywords rather than this connector.
