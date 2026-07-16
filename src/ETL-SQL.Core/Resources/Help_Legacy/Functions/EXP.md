# EXP
Returns e (Euler's number) raised to the specified power.

**Category:** Math

## Syntax
```sql
EXP(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` | The exponent |

## Returns
`FLOAT` — e^number (approximately 2.71828^number).

## Example
```sql
SELECT EXP(0);    -- → 1.0
SELECT EXP(1);    -- → 2.71828...
SELECT EXP(LOG(x)) AS original FROM #values;   -- LOG and EXP are inverses
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`LOG`](LOG.md), [`POWER`](POWER.md)
