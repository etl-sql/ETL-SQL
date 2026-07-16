# PERCENTILE_CONT
Returns the continuous interpolated percentile value within a group or window.

**Category:** Window

## Syntax
```sql
PERCENTILE_CONT(fraction) WITHIN GROUP (ORDER BY expression)
PERCENTILE_CONT(fraction) WITHIN GROUP (ORDER BY expression) OVER (PARTITION BY col1, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `fraction` | `DECIMAL` | Percentile in [0.0, 1.0] — e.g., `0.5` for median |

## Returns
`FLOAT` — The interpolated value at the given percentile.

## Remarks
- `PERCENTILE_CONT(0.5)` is equivalent to `MEDIAN`.
- For discrete (non-interpolated) percentile, use `PERCENTILE_DISC`.

## Example
```sql
-- Overall median price
SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY price) AS median_price
  FROM #products;

-- Median per category (window form)
SELECT category, price,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY price) OVER (PARTITION BY category) AS cat_median
FROM #products;
```

## See Also
- [Standard Library — §13.5 Distribution Functions](../../../guides/getting-started.md#135-distribution-functions)
- Related: [`PERCENTILE_DISC`](percentile_disc.md), [`MEDIAN`](median.md), [`NTILE`](../window/ntile.md)
