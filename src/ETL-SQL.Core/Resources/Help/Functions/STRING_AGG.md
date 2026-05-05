# STRING_AGG
Concatenates non-NULL values using a separator.

Syntax:
  STRING_AGG(expr, sep)

```sql
SELECT STRING_AGG(Name, ', ') FROM Employees;
```
