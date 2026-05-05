# CONCAT_WS
Concatenates values with a separator, skipping NULLs.

Syntax:
  CONCAT_WS(sep, a, b, ...)

```sql
SELECT CONCAT_WS(', ', 'Alice', NULL, 'Bob');
```
