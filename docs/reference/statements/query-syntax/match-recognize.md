# MATCH_RECOGNIZE
Identifies sequences of rows that match a pattern, similar to a regex applied to ordered result sets.

## Syntax
```sql
SELECT <output_cols>
FROM <source>
MATCH_RECOGNIZE (
  PARTITION BY <col>, ...
  ORDER BY <col>
  MEASURES
    <aggregate_or_nav_expr> AS <alias>, ...
  PATTERN (<pattern_expr>)
  DEFINE
    <var_name> AS <condition>,
    ...
) AS <alias>;
```

## Clauses

| Clause | Description |
|---|---|
| `PARTITION BY` | Groups rows before pattern matching, like a window OVER clause |
| `ORDER BY` | Defines the sequence order for pattern evaluation |
| `MEASURES` | Computes values from the matched rows; available in the output |
| `PATTERN` | The sequence pattern using variable names defined in DEFINE |
| `DEFINE` | Conditions each pattern variable must satisfy |

## Pattern Operators

| Operator | Meaning |
|---|---|
| `A B` | Sequence: A followed by B |
| `A \| B` | Alternation: A or B |
| `A*` | Zero or more A |
| `A+` | One or more A |
| `A?` | Optional A |
| `A{n,m}` | Between n and m repetitions of A |

## Examples
```sql
-- Detect user sessions: login, activity, logout
SELECT *
FROM #events
MATCH_RECOGNIZE (
  PARTITION BY user_id
  ORDER BY event_time
  MEASURES
    FIRST(event_time) AS session_start,
    LAST(event_time)  AS session_end,
    COUNT(*)          AS event_count
  PATTERN (START ACTIVITY+ END)
  DEFINE
    START    AS event_type = 'login',
    ACTIVITY AS event_type NOT IN ('login', 'logout'),
    END      AS event_type = 'logout'
) AS sessions;

-- Detect price spikes: three or more consecutive up days followed by a drop
SELECT *
FROM #prices
MATCH_RECOGNIZE (
  PARTITION BY ticker
  ORDER BY trade_date
  MEASURES
    FIRST(trade_date) AS spike_start,
    LAST(trade_date)  AS spike_end,
    MAX(close_price)  AS peak
  PATTERN (UP{3,} DOWN)
  DEFINE
    UP   AS close_price > PREV(close_price),
    DOWN AS close_price < PREV(close_price)
) AS spikes;
```

## Notes
- `PREV(col)` and `NEXT(col)` reference adjacent rows within the current pattern match context.
- Results include one row per matched pattern instance. Unmatched rows are excluded from output.
- MATCH_RECOGNIZE is processed in-memory on sorted partitions. Pre-filter large datasets before matching.
- PARTITION BY is optional; omit it to match across the entire result set as a single sequence.
- See: SELECT, WITH, PIVOT

References:
- [Grammar](../../../guides/getting-started.md)
