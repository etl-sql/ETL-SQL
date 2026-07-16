# Portal Group Management
Create and manage user groups to simplify folder permission assignments inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE GROUP 'GroupName';
  DROP GROUP 'GroupName';
  ADD USER 'username' TO GROUP 'GroupName';
END;
```

## Examples
```sql
-- Create a group for the finance department
EXECUTE portal BEGIN
  CREATE GROUP 'Finance';
END;

-- Add users to the group
EXECUTE portal BEGIN
  ADD USER 'jsmith' TO GROUP 'Finance';
  ADD USER 'aparker' TO GROUP 'Finance';
END;

-- Grant folder access to the group instead of individual users
EXECUTE portal BEGIN
  GRANT 'Finance Reports' TO GROUP 'Finance' WITH (ACCESS = READ);
END;

-- Remove the group (users are not deleted)
EXECUTE portal BEGIN
  DROP GROUP 'Finance';
END;
```

## Notes
- Groups simplify permission management — grant access to a group rather than granting permissions to each user individually.
- A user can belong to multiple groups; their effective permissions are the union of all group and individual grants.
- Dropping a group removes all its memberships but does not delete the member users or their individual permission grants.
- There is no `REMOVE USER FROM GROUP` command; to remove a membership, drop and recreate the group, or manage memberships via the portal admin UI.
- Use `SHOW EFFECTIVE PERMISSIONS FOR USER` to inspect the resolved permissions for a user across all their groups.
- See: PORTAL_PERMISSIONS, PORTAL_USER, PORTAL_SHOW

References:
- [Data Connectors](../../guides/administration.md)
- [Portal Admin Commands](README.md)
