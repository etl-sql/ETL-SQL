FAVORITE marks portal reports as favorites.

Syntax:
```sql
EXECUTE portal BEGIN
  FAVORITE REPORT 'Daily Sales';
  FAVORITE REPORT 'Daily Sales' FOR USER 'alice';
  UNFAVORITE REPORT 'Daily Sales';
  UNFAVORITE REPORT 'Daily Sales' FOR USER 'alice';
  SELECT * INTO #favorites FROM eng.favorites(50);
  SELECT * FROM eng.favorites('alice');
END;
```

Notes:
- Without `FOR USER`, the favorite applies to the account used by the PORTAL connection.
- `FOR USER` requires an admin connection because it writes another user's favorites.

References:
- [Portal Admin Commands](README.md)
