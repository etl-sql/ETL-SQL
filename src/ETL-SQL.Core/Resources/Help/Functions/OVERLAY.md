# OVERLAY
SQL standard form for replacing a portion of a string.

Syntax:
  OVERLAY(s PLACING ins FROM pos FOR len)

```sql
SELECT OVERLAY('Hello World' PLACING 'SQL' FROM 7 FOR 5);
```
