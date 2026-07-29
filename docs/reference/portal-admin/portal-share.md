# Portal Share Links and Embed Tokens
Create and revoke unauthenticated share links and embed tokens for portal reports inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE SHARE LINK 'External Review' FOR REPORT 'Sales Dashboard' [EXPIRES '2025-12-31'];
  SHOW SHARE LINKS FOR REPORT 'Sales Dashboard';
  REVOKE SHARE LINK 'External Review' FOR REPORT 'Sales Dashboard';

  CREATE EMBED TOKEN 'Wallboard' FOR REPORT 'Sales Dashboard' [EXPIRES '2025-12-31'];
  SHOW EMBED TOKENS FOR REPORT 'Sales Dashboard';
  REVOKE EMBED TOKEN 'Wallboard' FOR REPORT 'Sales Dashboard';
END;
```

## Examples
```sql
-- Create a share link with no expiry
EXECUTE portal BEGIN
  CREATE SHARE LINK 'External Review' FOR REPORT 'Sales Dashboard';
END;

-- Create a share link that expires at year-end
EXECUTE portal BEGIN
  CREATE SHARE LINK 'Year End Review' FOR REPORT 'Sales Dashboard' EXPIRES '2025-12-31';
END;

-- List all active share links for a report
EXECUTE portal BEGIN
  SHOW SHARE LINKS FOR REPORT 'Sales Dashboard' INTO #links;
END;
SELECT name, token, created_at, expires_at FROM #links;

-- Revoke a specific share link using its name and report
EXECUTE portal BEGIN
  REVOKE SHARE LINK 'External Review' FOR REPORT 'Sales Dashboard';
END;

-- Create an embed token for use in an external application
EXECUTE portal BEGIN
  CREATE EMBED TOKEN 'Finance App' FOR REPORT 'Sales Dashboard' EXPIRES '2025-06-30';
END;

-- List all embed tokens for a report
EXECUTE portal BEGIN
  SHOW EMBED TOKENS FOR REPORT 'Sales Dashboard' INTO #tokens;
END;
SELECT name, token, created_at, expires_at FROM #tokens;

-- Revoke an embed token
EXECUTE portal BEGIN
  REVOKE EMBED TOKEN 'Finance App' FOR REPORT 'Sales Dashboard';
END;
```

## Notes
- Share links grant unauthenticated, read-only access to a specific report. Anyone with the link URL can view the report without logging in.
- Embed tokens are intended for embedding reports in external applications via the portal embed API. They carry the same read-only scope as share links but are passed as bearer tokens rather than URL tokens.
- Both token types can carry an optional `EXPIRES` date (ISO 8601 date or datetime). Omitting `EXPIRES` uses the Portal default expiration.
- Revoking a token immediately invalidates all active browser sessions or embed contexts using it.
- `SHOW SHARE LINKS` and `SHOW EMBED TOKENS` return the stable name, full token value, creation timestamp, expiry date, and the identity that created the token.
- Share links and embed tokens are independent of portal user permissions — a shared report can be viewed by anyone with the link regardless of folder access grants.
- See: PORTAL_REPORT, PORTAL_PERMISSIONS, PORTAL_SHOW

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
