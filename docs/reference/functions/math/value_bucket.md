# VALUE_BUCKET

Categorizes a numeric value into caller-defined bands using comma-separated thresholds and labels.

## Syntax

```sql
VALUE_BUCKET(value, thresholds, labels)
```

## Parameters

- **value** - Numeric value to categorize.
- **thresholds** - String of comma-separated numeric upper bounds, in ascending order (e.g. `'100,500,1000'`).
- **labels** - String of comma-separated band names (e.g. `'Small,Medium,Large,Enterprise'`).

## Returns

Returns the label of the first band whose threshold is greater than or equal to `value`.
Thresholds are **inclusive** upper bounds.

Supply one more label than thresholds to name the overflow band. Given
`thresholds = '100,500'` and `labels = 'Low,Mid,High'`:

| Value | Returned label |
| :--- | :--- |
| 100 or less | `Low` |
| 101 to 500 | `Mid` |
| Greater than 500 | `High` |

## Null Behavior

Returns `NULL` when `value` is `NULL` or non-numeric, or when `thresholds` or `labels` contains no
usable entries.

## Remarks

- Thresholds must be listed in ascending order; the first match wins, so an unordered list makes
  later bands unreachable.
- If `labels` is shorter than `thresholds`, the last label is reused for the remaining bands.
- Non-numeric threshold entries are ignored rather than raising an error.
- Use [`AGE_BUCKET`](age_bucket.md) for the standard 30/60/90/120-day ageing bands.

## Examples

```sql
SELECT VALUE_BUCKET(order_total, '100,500,1000', 'Small,Medium,Large,Enterprise') AS size_band
FROM #orders;
```

```sql
-- Score banding for a data-quality dashboard
SELECT
  dataset_name,
  VALUE_BUCKET(quality_score, '50,80,95', 'Poor,Fair,Good,Excellent') AS quality_band
FROM #dataset_scores;
```

## References

- [Functions](../README.md)
- [AGE_BUCKET](age_bucket.md)
