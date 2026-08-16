# ASOF JOIN

A nearest-match join designed for temporal and fuzzy continuous alignments. For each row on the left side, `ASOF JOIN` returns the single closest row on the right side that satisfies one inequality condition after matching any equality keys.

---

## Syntax

```sql
SELECT <columns...>
FROM <left_table> AS l
ASOF [LEFT] JOIN <right_table> AS r
  ON l.key = r.key
  AND l.timestamp_col >= r.timestamp_col;
```

---

## Semantics & Direction Rules

- **Equality Predicates**: Zero or more equality keys (e.g. `l.sensor_id = r.sensor_id`) narrow the candidate search space per partition.
- **Inequality Predicate**: The `ON` clause must contain **exactly one** inequality (`>=`, `>`, `<=`, `<`).
- **Match Direction**:
  - `>=` / `>`: Selects the **largest** qualifying right value (the most recent record at or before the left timestamp).
  - `<=` / `<`: Selects the **smallest** qualifying right value (the earliest record at or after the left timestamp).
- **Join Modes**:
  - `ASOF JOIN`: Inner match behavior; drops left rows if no qualifying right row exists.
  - `ASOF LEFT JOIN`: Preserves all left rows, filling unmatched right columns with `NULL`.

---

## Examples

### 1. Market Data: Quote Alignment

Attach the most recent bid/ask quote to each trade execution:

```sql
SELECT t.trade_id, t.symbol, t.trade_time, t.price, q.bid, q.ask
FROM #trades AS t
ASOF JOIN #quotes AS q
  ON t.symbol = q.symbol
  AND t.trade_time >= q.quote_time;
```

### 2. Real-World ETL: Sensor Calibration & State Backfill

Align asynchronous IoT sensor readings with the latest calibration parameters from an operational database:

```sql
CREATE CONNECTION pg AS POSTGRES(HOST='pg01.internal', DATABASE='telemetry');
CREATE CONNECTION dest AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Extract raw asynchronous telemetry
SELECT device_id, reading_time, metric_value
INTO #raw_telemetry
FROM pg.device_readings
WHERE reading_time >= DATEADD(HOUR, -24, GETDATE());

-- 2. Extract discrete calibration events
SELECT device_id, calibrated_at, slope, intercept
INTO #calibrations
FROM pg.calibration_events;

-- 3. Align each reading with the active calibration profile at that instant
SELECT 
    r.device_id,
    r.reading_time,
    r.metric_value,
    COALESCE(c.slope, 1.0) AS applied_slope,
    COALESCE(c.intercept, 0.0) AS applied_intercept,
    (r.metric_value * COALESCE(c.slope, 1.0)) + COALESCE(c.intercept, 0.0) AS calibrated_value
INTO #calibrated_output
FROM #raw_telemetry AS r
ASOF LEFT JOIN #calibrations AS c
  ON r.device_id = c.device_id
  AND r.reading_time >= c.calibrated_at;

-- 4. Load aligned measurements into the warehouse
INSERT INTO dest.dbo.CalibratedTelemetry SELECT * FROM #calibrated_output;
```

---

## Remarks & Best Practices

- **Performance**: Equality keys are indexed in memory. Always include partition/device/symbol equality keys when available to minimize search overhead.
- **Validation**: Queries with multiple inequality expressions or missing inequality predicates are rejected at parse time.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [LATERAL Join](lateral.md)
- [ETL Cookbook: Cross-Platform Reconciliation](../../../cookbooks/etl/cross-platform-reconciliation.md)
- [ETL Cookbook: Time Series Gap Filling](../../../cookbooks/etl/time-series-gap-filling.md)
- [Syntax Index](../../../syntax-index.md)
