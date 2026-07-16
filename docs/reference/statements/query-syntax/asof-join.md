# ASOF JOIN
A nearest-match join. For each left row it returns the single closest right row satisfying one inequality (after any equality keys) — ideal for aligning time series such as trades to the most recent quote.

## Syntax
```sql
FROM <left>
ASOF [LEFT] JOIN <right>
  ON <equality-keys...> AND <one-inequality>
```

## Semantics
- The `ON` clause must contain **exactly one** inequality (`<`, `<=`, `>`, `>=`) plus zero or more equality predicates.
- Direction follows the operator:
  - `>=` / `>` → the **largest** qualifying right value (most recent at/before the left value).
  - `<=` / `<` → the **smallest** qualifying right value (nearest at/after).
- `ASOF JOIN` drops left rows with no match; `ASOF LEFT JOIN` keeps them with NULLs on the right side.

## Example
```sql
-- Attach the most recent quote (per symbol) at or before each trade
SELECT t.id, t.symbol, t.ts, q.bid
FROM trades t
ASOF JOIN quotes q
  ON t.symbol = q.symbol
  AND t.ts >= q.ts;
```

## Notes
- The right side is buffered and matching is currently O(left × right); add equality keys to narrow candidates on large inputs.
- A missing or multiple inequality predicate is rejected at execution time.

References:
- [Grammar — ASOF JOIN](../../../guides/getting-started.md#561-asof-join)
