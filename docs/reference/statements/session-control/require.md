# REQUIRE
Declares a minimum ETL-SQL engine version required to run this script. Fails fast with a clear error if the runtime is too old.

## Syntax
```sql
REQUIRE >= '2.4.0';
```

## Operators
| Operator | Meaning |
|---|---|
| `>=` | minimum version (most common) |
| `=` | exact version match |
| `>` | strictly newer than |

## Example
```sql
-- At the top of the script, before any other statements
REQUIRE >= '2.4.0';

DECLARE @start RELDATE INPUT = 'M-1';

SELECT * FROM MyDB.dbo.Orders
WHERE OrderDate >= @start
INTO #orders;
```

## Notes
- Best practice: place `REQUIRE` as the first statement in a script.
- Version format is `MAJOR.MINOR.PATCH` (semantic versioning).
- If the running engine does not satisfy the constraint, execution stops immediately with a version mismatch error — no partial execution occurs.
- See: DECLARE, RUN SCRIPT

References:
- [Grammar](../../../guides/getting-started.md)
