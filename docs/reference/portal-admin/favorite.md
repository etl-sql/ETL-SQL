FAVORITE marks portal reports as favorites.

Syntax:
```sql
EXECUTE portal BEGIN
  FAVORITE REPORT 'Daily Sales';
  FAVORITE REPORT 'Daily Sales' FOR USER 'alice';
  UNFAVORITE REPORT 'Daily Sales';
  UNFAVORITE REPORT 'Daily Sales' FOR USER 'alice';
  SHOW FAVORITES LIMIT 50 INTO #favorites;
  SHOW FAVORITES FOR USER 'alice' LIMIT 50;
END;
```

Notes:
- Without `FOR USER`, the favorite applies to the account used by the REPORTPORTAL connection.
- `FOR USER` requires an admin connection because it writes another user's favorites.

References:
- [Grammar](../../guides/getting-started.md)
