# PATINDEX
Returns the position of the first match of a LIKE pattern in a string.

Syntax:
  PATINDEX(pattern, s)

```sql
SELECT PATINDEX('%[0-9]%', 'abc123');
```
