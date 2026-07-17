# SHOW VARIABLES
Displays all declared variables in the current session scope.

## Syntax
```sql
SHOW VARIABLES [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with variable name, data type, and current value for each declared variable. Variables marked as `SECRET` are masked.

## Example
```sql
SET @region = 'West';
SET @threshold = 100;
SET @apiKey = SECRET 'abc123';

-- Display all variables
SHOW VARIABLES;

-- Capture and query
SHOW VARIABLES INTO #vars;
SELECT Name, Value FROM #vars WHERE Name LIKE '%region%';
```

## Notes
- Variables declared with `SECRET` have their values masked in the output.
- Both user-declared and system variables are shown.

## References
- [SHOW Commands](README.md)
