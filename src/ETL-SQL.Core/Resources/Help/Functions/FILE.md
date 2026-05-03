FILE Functions
==============

Functions for listing and checking local and remote files.
All local paths are resolved through the script's working directory.

Local File System
-----------------
  FILE_LIST(path)               Return a table of files in the directory at path.
  FILE_LIST(path, TRUE)         Include files in all subdirectories (recursive).
  DIRECTORY(path)               Alias for FILE_LIST.
  DIRECTORY(path, TRUE)         Recursive alias.

Columns returned:
  Name          File name with extension.
  Path          Full absolute path to the file.
  Extension     File extension including the leading dot (e.g. '.csv').
  Size          File size in bytes.
  LastModified  Date and time the file was last written.

```sql
-- List all CSV files in a directory
SELECT Name, Size, LastModified
FROM FILE_LIST('C:\data\exports')
WHERE Extension = '.csv'
ORDER BY LastModified DESC;

-- Find files modified today
SELECT Name, Path
FROM FILE_LIST('C:\data', TRUE)
WHERE LastModified >= CURRENT_DATE;

-- Count files by extension
SELECT Extension, COUNT(*) AS FileCount, SUM(Size) AS TotalBytes
FROM DIRECTORY('C:\data')
GROUP BY Extension;
```

Existence Checks
----------------
  FILE_EXISTS(path)             Return 1 if the file exists, 0 otherwise.
  DIRECTORY_EXISTS(path)        Return 1 if the directory exists, 0 otherwise.

```sql
-- Guard before reading
IF FILE_EXISTS('C:\data\input.csv') = 0
    THROW 50001, 'Input file not found', 1;

-- Create output directory if missing
IF DIRECTORY_EXISTS('C:\data\output') = 0
    EXEC xp_create_subdirectory 'C:\data\output';
```

Remote File Systems
-------------------
  REMOTE_FILE_LIST(conn_name)
      Return a table of files from a remote connection (SFTP, FTP, Azure Blob, S3).
  REMOTE_FILE_LIST(conn_name, path)
      List only the specified remote path.

The connection must be established with CREATE CONNECTION ... AS SFTP / FTP / BLOB.

Columns returned:
  Name          File or object name.
  FullPath      Full remote path or URI.
  Size          Size in bytes (NULL if unavailable).
  LastModified  Last-modified timestamp (NULL if unavailable).
  IsDirectory   TRUE if the entry is a folder/prefix.

```sql
-- Establish a connection
CREATE CONNECTION @sftp AS SFTP
WITH (HOST='files.example.com', USERNAME='user', PASSWORD='s3cr3t');

-- List files and pick only CSVs
SELECT Name, Size
FROM REMOTE_FILE_LIST(@sftp, '/uploads')
WHERE Name LIKE '%.csv'
ORDER BY LastModified DESC;

-- Find yesterday's delivery
DECLARE @yesterday VARCHAR = FORMAT(DATEADD(DAY, -1, CURRENT_DATE), 'yyyyMMdd');
SELECT FullPath
FROM REMOTE_FILE_LIST(@sftp, '/drops')
WHERE Name LIKE '%' + @yesterday + '%';
```

Notes
-----
  - FILE_LIST and DIRECTORY are identical; both are provided for familiarity.
  - FILE_EXISTS and DIRECTORY_EXISTS resolve relative paths from the script location.
  - REMOTE_FILE_LIST requires a live named connection; use CREATE CONNECTION first.
  - For file read/write operations see HELP LOAD, HELP EXPORT, HELP IMPORT.
