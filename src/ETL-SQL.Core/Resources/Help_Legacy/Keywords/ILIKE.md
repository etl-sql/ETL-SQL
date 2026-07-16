# ILIKE
Performs case-insensitive pattern matching.

## Syntax
```sql
string_column ILIKE 'pattern'
```

## Example
Find employees whose email domain ends with `company.com` regardless of character casing:
```sql
SELECT name, email
FROM employees
WHERE email ILIKE '%@company.com';
```

## Notes
- Similar to `LIKE`, but ignores character casing (e.g., `ILIKE '%@company.com'` matches `User@Company.com`, `user@COMPANY.COM`, etc.).
- Wildcards:
  - `%` matches any sequence of zero or more characters.
  - `_` matches any single character.
- For regex pattern matching, use `~` (case-sensitive) or `~*` (case-insensitive).

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md#8-logical-operators-filter-predicates)
