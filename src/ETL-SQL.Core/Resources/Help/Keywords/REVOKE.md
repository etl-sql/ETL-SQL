REVOKE removes portal permissions, share/embed tokens, or user authentication tokens.

Syntax:
```sql
EXECUTE portal BEGIN
  REVOKE MANAGE ON FOLDER '/Finance' FROM GROUP 'Analysts';
  REVOKE READ ON DATASET 'DailySales' FROM GROUP 'Analysts';
  REVOKE SHARE LINK '<token>';
  REVOKE EMBED TOKEN '<token>';
  REVOKE TOKENS FOR USER 'alice';
END;
```

Notes:
- `REVOKE TOKENS` invalidates refresh tokens. Existing access tokens expire on their normal JWT lifetime.
- Use `DISCONNECT USER '<name>'` when you want to revoke active refresh sessions for a user.
- Share and embed token lookup is global; duplicate matching tokens are treated as an error.

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
