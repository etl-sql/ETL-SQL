# DECODE
Equality switch.

Syntax:
  DECODE(val, s1, r1, ..., def)

```sql
SELECT DECODE(status, 'A', 'Active', 'I', 'Inactive', 'Unknown');
```
