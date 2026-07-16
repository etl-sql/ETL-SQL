# Portal Folder Management
Create, rename, move, and remove folders in the portal navigation tree inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE FOLDER 'FolderName' [UNDER 'ParentFolder'];
  ALTER FOLDER 'FolderName' RENAME TO 'NewName';
  ALTER FOLDER 'FolderName' MOVE TO 'ParentFolder';
  DROP FOLDER 'FolderName';
END;
```

## Examples
```sql
-- Create a top-level folder
EXECUTE portal BEGIN
  CREATE FOLDER 'Finance';
END;

-- Create a nested folder under Finance
EXECUTE portal BEGIN
  CREATE FOLDER 'Archive' UNDER 'Finance';
END;

-- Create a deeply nested folder
EXECUTE portal BEGIN
  CREATE FOLDER 'Q1 2025' UNDER 'Finance/Archive';
END;

-- Rename a folder
EXECUTE portal BEGIN
  ALTER FOLDER 'Finance' RENAME TO 'Finance & Accounting';
END;

-- Move a folder to a new parent
EXECUTE portal BEGIN
  ALTER FOLDER 'Archive' MOVE TO 'Finance & Accounting';
END;

-- Drop an empty folder
EXECUTE portal BEGIN
  DROP FOLDER 'Finance & Accounting/Archive/Q1 2025';
END;
```

## Notes
- Folders organize reports in the portal navigation tree and serve as the unit of permission management.
- The `UNDER` clause specifies the parent folder using its name or slash-delimited path (e.g., `'Finance/Archive'`).
- Omitting `UNDER` creates a top-level folder at the root of the navigation tree.
- `DROP FOLDER` fails if the folder contains any reports — move or drop all reports in the folder first.
- Dropping a folder also removes all permission grants associated with that folder.
- Folder paths used in `GRANT` and `REVOKE` commands must match the current folder name exactly.
- See: PORTAL_PERMISSIONS, PORTAL_REPORT, PORTAL_SHOW

References:
- [Data Connectors](../../guides/administration.md)
- [Grammar](../../guides/getting-started.md)
