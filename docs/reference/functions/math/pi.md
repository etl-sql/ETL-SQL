# PI

Returns the mathematical constant pi.

## Syntax

```sql
PI()
```

## Parameters

None.

## Returns

Returns a decimal value approximately equal to `3.141592653589793`.

## Null Behavior

`PI()` takes no arguments and never returns `NULL`.

## Examples

```sql
SELECT PI() AS pi_value;
```

```sql
SELECT 2 * PI() * radius AS circumference
FROM #circles;
```

## References

- [Functions](../README.md)
- [DEGREES](degrees.md)
- [RADIANS](radians.md)
