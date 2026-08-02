# SET @variable
Assigns a value to a declared or implicitly declared session variable.

## Syntax
```sql
SET @variable = <expression>;
```

## Parameters
- **@variable** — The variable name, prefixed with `@`. Case-insensitive. Created implicitly on first assignment if not previously declared with `DECLARE`.
- **expression** — Any scalar expression: a literal, function call, arithmetic expression, or subquery returning a single value.

## Example
```sql
-- Simple literal assignment
SET @region = 'North';
SET @threshold = 500;

-- Expression assignment
SET @cutoff = DATEADD(DAY, -30, GETDATE());
SET @label = 'Report_' + FORMAT(GETDATE(), 'yyyyMMdd');

-- Subquery assignment
SET @maxId = (SELECT MAX(id) FROM #staging);
```

## Notes
- Variables persist for the lifetime of the session.
- Variable names are case-insensitive: `@Region` and `@region` refer to the same variable.
- Use `DECLARE @var <type>` for explicit typing; `SET` with an implicit declare infers the type from the expression.
- To mark a variable as secret, use `SET @var = SECRET '<value>'`.
- See also: `DECLARE`, `eng.variables`.

## References
- [SET Commands](README.md)
