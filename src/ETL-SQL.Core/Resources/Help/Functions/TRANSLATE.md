# TRANSLATE
Replaces characters in from_chars with corresponding characters in to_chars.

Syntax:
  TRANSLATE(s, from, to)

```sql
SELECT TRANSLATE('2+3*5', '+*', '-/');
```
