# Portal Permission Management (GRANT / REVOKE)
Grant and revoke folder access for users and groups inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  GRANT 'FolderName' TO USER 'username' WITH (ACCESS = READ | WRITE | ADMIN);
  GRANT 'FolderName' TO GROUP 'GroupName' WITH (ACCESS = READ | WRITE | ADMIN);
  REVOKE 'FolderName' FROM USER 'username';
  REVOKE 'FolderName' FROM GROUP 'GroupName';
END;
```

## Examples
```sql
-- Grant read access to an individual user
EXECUTE portal BEGIN
  GRANT 'Finance' TO USER 'jsmith' WITH (ACCESS = READ);
END;

-- Grant write access to a department group
EXECUTE portal BEGIN
  GRANT 'Finance' TO GROUP 'Finance' WITH (ACCESS = WRITE);
END;

-- Grant admin access to a folder owner
EXECUTE portal BEGIN
  GRANT 'Finance' TO USER 'aparker' WITH (ACCESS = ADMIN);
END;

-- Revoke access for a user who has moved teams
EXECUTE portal BEGIN
  REVOKE 'Finance' FROM USER 'jsmith';
END;

-- Revoke a group's access to a folder
EXECUTE portal BEGIN
  REVOKE 'Finance' FROM GROUP 'Finance';
END;

-- Inspect effective permissions for a user
EXECUTE portal BEGIN
  SHOW EFFECTIVE PERMISSIONS FOR USER 'jsmith' INTO #perms;
END;
SELECT * FROM #perms;
```

## Notes
- ACCESS levels control what a user or group can do within a folder:
- - `READ` — view and open reports in the folder.
- - `WRITE` — publish new reports and update existing ones in the folder.
- - `ADMIN` — manage folder permissions (GRANT / REVOKE) and rename or move the folder.
- Permissions are inherited by all sub-folders; a grant on `Finance` also applies to `Finance/Archive`.
- A user's effective permission is the highest access level granted through any combination of user and group grants.
- `REVOKE` removes a specific grant; if the user retains access through a group membership, they will still have access.
- Use `SHOW EFFECTIVE PERMISSIONS FOR USER` to resolve the combined permissions from all user and group grants before making changes.
- Folder admin (ADMIN access) does not grant portal-level admin rights — only the ADMIN role on the user account confers full portal control.
- See: PORTAL_USER, PORTAL_GROUP, PORTAL_FOLDER, PORTAL_SHOW

References:
- [Data Connectors](../../guides/administration.md)
- [Grammar](../../guides/getting-started.md)
