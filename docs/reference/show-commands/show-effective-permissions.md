# SHOW EFFECTIVE PERMISSIONS
Displays resolved portal permissions for a user, report, or folder.

## Syntax
```sql
SHOW EFFECTIVE PERMISSIONS FOR USER|REPORT|FOLDER '<target>' [INTO #table];
```

## Parameters
- **USER|REPORT|FOLDER** — The type of target to inspect permissions for.
- **'target'** — The name of the user, report, or folder.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with permission name, source (direct grant, group, or inherited), and scope for each effective permission.

## Example
```sql
EXECUTE portal BEGIN
    -- Check a user's effective permissions
    SHOW EFFECTIVE PERMISSIONS FOR USER 'jsmith';

    -- Check permissions on a report
    SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Monthly Sales Dashboard' INTO #perms;
    SELECT Permission, Source, Scope FROM #perms;

    -- Check folder-level permissions
    SHOW EFFECTIVE PERMISSIONS FOR FOLDER '/Finance';
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Resolves inherited permissions through folder hierarchy and group memberships.
- Useful for auditing and troubleshooting access control.

## References
- [SHOW Commands](README.md)
