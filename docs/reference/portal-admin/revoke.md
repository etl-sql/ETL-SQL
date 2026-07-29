REVOKE removes portal permissions, share/embed tokens, or user authentication tokens.

Syntax:
```sql
EXECUTE portal BEGIN
  REVOKE MANAGE ON FOLDER '/Finance' FROM GROUP 'Analysts';
  REVOKE READ ON DATASET 'DailySales' FROM GROUP 'Analysts';
  REVOKE SHARE LINK 'External Review' FOR REPORT 'Daily Sales';
  REVOKE EMBED TOKEN 'Finance Wallboard' FOR REPORT 'Daily Sales';
  REVOKE TOKENS FOR USER 'alice';
END;
```

Notes:
- `REVOKE TOKENS` invalidates refresh tokens. Existing access tokens expire on their normal JWT lifetime.
- Use `DISCONNECT USER '<name>'` when you want to revoke active refresh sessions for a user.
- Share and embed token revocation is report-scoped and uses the resource name created by `CREATE SHARE LINK` or `CREATE EMBED TOKEN`.

References:
- [Portal Admin Commands](README.md)
