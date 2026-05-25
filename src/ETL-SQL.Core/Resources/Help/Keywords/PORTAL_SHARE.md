# Portal Share Links and Embed Tokens
Create and revoke unauthenticated share links and embed tokens for portal reports inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE SHARE LINK FOR REPORT 'Sales Dashboard' [WITH (EXPIRES_AT = '2025-12-31')];
  SHOW SHARE LINKS FOR REPORT 'Sales Dashboard';
  REVOKE SHARE LINK 'token_value';

  CREATE EMBED TOKEN FOR REPORT 'Sales Dashboard' [WITH (EXPIRES_AT = '2025-12-31')];
  SHOW EMBED TOKENS FOR REPORT 'Sales Dashboard';
  REVOKE EMBED TOKEN 'token_value';
END;
```

## Examples
```sql
-- Create a share link with no expiry
EXECUTE portal BEGIN
  CREATE SHARE LINK FOR REPORT 'Sales Dashboard';
END;

-- Create a share link that expires at year-end
EXECUTE portal BEGIN
  CREATE SHARE LINK FOR REPORT 'Sales Dashboard' WITH (EXPIRES_AT = '2025-12-31');
END;

-- List all active share links for a report
EXECUTE portal BEGIN
  SHOW SHARE LINKS FOR REPORT 'Sales Dashboard' INTO #links;
END;
SELECT token, created_at, expires_at FROM #links;

-- Revoke a specific share link using its token value
EXECUTE portal BEGIN
  REVOKE SHARE LINK 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...';
END;

-- Create an embed token for use in an external application
EXECUTE portal BEGIN
  CREATE EMBED TOKEN FOR REPORT 'Sales Dashboard' WITH (EXPIRES_AT = '2025-06-30');
END;

-- List all embed tokens for a report
EXECUTE portal BEGIN
  SHOW EMBED TOKENS FOR REPORT 'Sales Dashboard' INTO #tokens;
END;
SELECT token, created_at, expires_at FROM #tokens;

-- Revoke an embed token
EXECUTE portal BEGIN
  REVOKE EMBED TOKEN 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...';
END;
```

## Notes
- Share links grant unauthenticated, read-only access to a specific report. Anyone with the link URL can view the report without logging in.
- Embed tokens are intended for embedding reports in external applications via the portal embed API. They carry the same read-only scope as share links but are passed as bearer tokens rather than URL tokens.
- Both token types can carry an optional `EXPIRES_AT` date (ISO 8601 date or datetime). Omitting `EXPIRES_AT` creates a non-expiring token.
- Revoking a token immediately invalidates all active browser sessions or embed contexts using it.
- `SHOW SHARE LINKS` and `SHOW EMBED TOKENS` return the full token value, creation timestamp, expiry date (if set), and the identity that created the token.
- Share links and embed tokens are independent of portal user permissions — a shared report can be viewed by anyone with the link regardless of folder access grants.
- See: PORTAL_REPORT, PORTAL_PERMISSIONS, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
