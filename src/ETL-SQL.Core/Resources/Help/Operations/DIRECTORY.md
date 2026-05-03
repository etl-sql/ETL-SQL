# DIRECTORY Operations
File-system directory management commands. These operate on the local or UNC file system without requiring a CREATE CONNECTION.

Syntax:
  CREATE   DIRECTORY 'path';
  DELETE   DIRECTORY 'path';
  RENAME   DIRECTORY 'old_path' TO 'new_name';
  MOVE     DIRECTORY 'src_path' TO 'dest_path';
  COPY     DIRECTORY 'src_path' TO 'dest_path';
  COMPRESS DIRECTORY 'src_path' TO 'dest.zip';
  ENCRYPT  DIRECTORY 'src_path' TO 'dest_path' PASSWORD('passphrase');
  DECRYPT  DIRECTORY 'src_path' TO 'dest_path' PASSWORD('passphrase');

```sql
-- Ensure an output folder exists
CREATE DIRECTORY 'C:\exports\2024\q4';

-- Archive last quarter's exports
COMPRESS DIRECTORY 'C:\exports\2024\q3' TO 'C:\archives\2024_q3.zip';
DELETE DIRECTORY 'C:\exports\2024\q3';

-- Rotate to a dated folder
DECLARE @dest VARCHAR = 'C:\exports\' + FORMAT(GETDATE(), 'yyyy-MM-dd');
CREATE DIRECTORY @dest;
COPY DIRECTORY 'C:\exports\latest' TO @dest;
```

Paths are resolved through the engine's path security policy. Relative paths are resolved from the script's working directory.
