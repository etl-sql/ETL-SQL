# IS [NOT] DISTINCT FROM
Null-safe comparison operator. Treats `NULL` as an ordinary comparable value instead of producing `UNKNOWN`, so it never yields `NULL`.

## Syntax
```sql
<expression> IS DISTINCT FROM <expression>
<expression> IS NOT DISTINCT FROM <expression>
```

## Semantics
- `a IS DISTINCT FROM b` returns `TRUE` when the operands differ, **including** when exactly one side is `NULL`; it returns `FALSE` when they are equal or **both** `NULL`.
- `a IS NOT DISTINCT FROM b` is the logical negation: a null-safe equality (`NULL IS NOT DISTINCT FROM NULL` is `TRUE`).

| `a` | `b` | `IS DISTINCT FROM` | `IS NOT DISTINCT FROM` |
| :-- | :-- | :--: | :--: |
| `1` | `1` | `FALSE` | `TRUE` |
| `1` | `2` | `TRUE` | `FALSE` |
| `1` | `NULL` | `TRUE` | `FALSE` |
| `NULL` | `NULL` | `FALSE` | `TRUE` |

## Example
Detect changed rows where a `NULL` to value transition counts as a change (plain `<>` would drop those rows under three-valued logic):
```sql
SELECT s.id
FROM #staging s
JOIN #target t ON s.id = t.id
WHERE s.value IS DISTINCT FROM t.value;
```

Null-safe equality in a predicate (matches rows where `notes` is `NULL`, unlike `notes = @expected`):
```sql
SELECT * FROM #data WHERE notes IS NOT DISTINCT FROM @expected;
```

## Notes
- Uses the same value/type comparison rules as `=` for non-`NULL` operands; only the `NULL` handling differs.
- Honors the session `CASE_SENSITIVE` setting for string comparison, exactly like `=`.
- Equivalent rewrites: `a IS DISTINCT FROM b` is `NOT (a IS NOT DISTINCT FROM b)`; `a IS NOT DISTINCT FROM b` is `(a = b) OR (a IS NULL AND b IS NULL)`.

References:
- [Statements](../README.md)


## References

- [Expressions and Operators](../expressions-and-operators.md)
- [Statements](../README.md)
