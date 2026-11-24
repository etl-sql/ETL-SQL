# SCD2

Merges an incoming snapshot (`FROM #source`) into a Type-2 Slowly Changing Dimension table (`#target`), managing `EffectiveFrom`, `EffectiveTo`, and `IsCurrent` across initial loads, unchanged rows, changed tracked attributes, and new keys.

## Syntax

```sql
TRANSFORM #dim_target
FROM #incoming_snapshot
USING SCD2 (
    KEY_COLS = '<key_col1, key_col2>',
    TRACK_COLS = '<track_col1, track_col2>',
    [EFFECTIVE_DATE = '<yyyy-MM-dd>',]
    [EFFECTIVE_FROM_COL = 'EffectiveFrom',]
    [EFFECTIVE_TO_COL = 'EffectiveTo',]
    [IS_CURRENT_COL = 'IsCurrent']
);
```

## Options

- **KEY_COLS** — Comma-separated business key columns (required; must be unique within `#incoming_snapshot`).
- **TRACK_COLS** — Comma-separated tracked attribute columns whose changes close the prior version (`EffectiveTo = EFFECTIVE_DATE`, `IsCurrent = 0`) and open a new current version (`EffectiveFrom = EFFECTIVE_DATE`, `EffectiveTo = NULL`, `IsCurrent = 1`).
- **EFFECTIVE_DATE** — Date for newly opened and closed versions (defaults to current UTC date).
- **EFFECTIVE_FROM_COL / EFFECTIVE_TO_COL / IS_CURRENT_COL** — Optional custom SCD2 metadata column names (defaults: `EffectiveFrom`, `EffectiveTo`, `IsCurrent`).

## Examples

```sql
-- Initial load
TRANSFORM #dim_customer
FROM #day1_customers
USING SCD2 (
    KEY_COLS = 'CustomerId',
    TRACK_COLS = 'Tier, City',
    EFFECTIVE_DATE = '2026-01-01'
);

-- Subsequent incremental snapshot
TRANSFORM #dim_customer
FROM #day2_customers
USING SCD2 (
    KEY_COLS = 'CustomerId',
    TRACK_COLS = 'Tier, City',
    EFFECTIVE_DATE = '2026-02-01'
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
