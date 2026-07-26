# AGE_BUCKET

Groups a number of days into a standard ageing band, for aged receivables, open-item, and staleness reporting.

## Syntax

```sql
AGE_BUCKET(days)
```

## Parameters

- **days** - Numeric age in days, typically produced by `DATEDIFF`.

## Returns

Returns a `VARCHAR` band:

| Input range | Returned band |
| :--- | :--- |
| Less than 0 | `Current` |
| 0 to 30 | `0-30` |
| 31 to 60 | `31-60` |
| 61 to 90 | `61-90` |
| 91 to 120 | `91-120` |
| Greater than 120 | `120+` |

## Null Behavior

Returns `NULL` when `days` is `NULL` or non-numeric.

## Remarks

- A negative age returns `Current`, so a not-yet-due invoice does not fall into the `0-30` band.
- Bands are fixed. Use [`VALUE_BUCKET`](value_bucket.md) when you need your own thresholds and labels.
- Band boundaries are inclusive of the upper value, so exactly 30 days is `0-30`.

## Examples

```sql
SELECT AGE_BUCKET(DATEDIFF('day', invoice_date, GETDATE())) AS ageing_band
FROM #open_invoices;
```

```sql
-- Aged receivables summary
SELECT
  AGE_BUCKET(DATEDIFF('day', due_date, GETDATE())) AS ageing_band,
  COUNT(*)   AS invoice_count,
  SUM(amount) AS outstanding
FROM #open_invoices
GROUP BY AGE_BUCKET(DATEDIFF('day', due_date, GETDATE()));
```

## References

- [Functions](../README.md)
- [VALUE_BUCKET](value_bucket.md)
- [DATEDIFF](../datetime/datediff.md)
