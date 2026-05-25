# Portal User Management
Manage portal user accounts, roles, and authentication tokens inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE USER 'username' WITH (PASSWORD = 'pwd', EMAIL = 'user@corp.com', ROLE = 'VIEWER' | 'AUTHOR' | 'ADMIN');
  ALTER USER 'username' SET (PASSWORD = 'newpwd', ACTIVE = ON | OFF);
  DROP USER 'username';
  DISCONNECT USER 'username';
  REVOKE TOKENS FOR USER 'username';
END;
```

## Examples
```sql
-- Create a new read-only user
EXECUTE portal BEGIN
  CREATE USER 'jsmith' WITH (PASSWORD = 'Str0ng!Pass', EMAIL = 'jsmith@corp.com', ROLE = 'VIEWER');
END;

-- Promote a user to AUTHOR and force a password reset
EXECUTE portal BEGIN
  ALTER USER 'jsmith' SET (PASSWORD = 'NewP@ssw0rd', ACTIVE = ON);
END;

-- Disable an account without deleting it
EXECUTE portal BEGIN
  ALTER USER 'jsmith' SET (ACTIVE = OFF);
END;

-- Invalidate all sessions and force re-login for a user
EXECUTE portal BEGIN
  REVOKE TOKENS FOR USER 'jsmith';
END;

-- Disconnect all active refresh sessions for a departing user, then remove the account
EXECUTE portal BEGIN
  DISCONNECT USER 'jsmith';
  DROP USER 'jsmith';
END;

-- List all users with their roles and status
EXECUTE portal BEGIN
  SHOW USERS INTO #users;
END;
SELECT * FROM #users;
```

## Notes
- ROLE values control portal access level:
  - `VIEWER` — read-only access to reports the user has been granted permission to view.
  - `AUTHOR` — can publish and manage reports in folders they have WRITE access to.
  - `ADMIN` — full portal control, including user management and service administration.
- `ALTER USER ... SET (ACTIVE = OFF)` suspends the account without deleting it; the user cannot log in but their permissions and settings are preserved.
- `DISCONNECT USER` revokes all active refresh sessions immediately but does not invalidate authentication tokens.
- `REVOKE TOKENS FOR USER` invalidates all authentication tokens, forcing the user to re-authenticate on their next request.
- To fully lock out a user, call both `DISCONNECT USER` and `REVOKE TOKENS FOR USER` before disabling or dropping the account.
- `SHOW USERS` returns all accounts with their username, email, role, and active status.
- See: PORTAL_PERMISSIONS, PORTAL_GROUP, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
- [Grammar](../../../../../Docs/Reference/Grammar.md)
