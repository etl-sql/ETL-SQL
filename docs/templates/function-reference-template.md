# FUNCTION_NAME

> **Page-type: Reference — function**
> Owns: signature, parameters, return type, null behavior, remarks, and a copy-pasteable example
> for one function.
> Links to (does not restate): other functions it depends on; statement pages for context.
> Required sections: Syntax, Parameters, Returns, Null Behavior, Example, References.

One-sentence description of what the function returns.

## Syntax

```sql
FUNCTION_NAME(argument [, optional_argument])
```

## Parameters

- **argument** — Type and meaning.
- **optional_argument** — Type, default behavior, and valid values.

## Returns

Return type and shape.

## Null Behavior

Describe how `NULL` inputs are handled.

## Remarks

Add dialect notes, determinism notes, collation notes, precision notes, or security notes.

## Example

```sql
SELECT FUNCTION_NAME(column_name) AS result
FROM #stage;
```

## References

- [Standard Library](../reference/functions/README.md)
