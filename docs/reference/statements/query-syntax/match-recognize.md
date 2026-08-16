# MATCH_RECOGNIZE

Row pattern recognition for complex event processing (CEP) over ordered datasets. Identifies sequential patterns across time-series, log streams, and transactional records using regular-expression-like sequence definitions.

---

## Syntax

```sql
SELECT <output_columns...>
FROM <source_table>
MATCH_RECOGNIZE (
  [PARTITION BY <partition_columns...>]
  ORDER BY <sort_columns...>
  MEASURES
    <measure_expression_1> AS <alias_1>,
    <measure_expression_2> AS <alias_2>
  [ONE ROW PER MATCH | ALL ROWS PER MATCH]
  PATTERN (<pattern_expression>)
  DEFINE
    <variable_1> AS <boolean_condition_1>,
    <variable_2> AS <boolean_condition_2>
) AS <match_alias>;
```

---

## Clauses & Pattern Syntax

### Core Clauses
- **`PARTITION BY`** — Divides the dataset into independent evaluation streams (e.g. per device, ticker, or user).
- **`ORDER BY`** — Establishes the strict chronological sequence for pattern evaluation.
- **`MEASURES`** — Calculates summary metrics from the matching window (e.g. start/end timestamps, peak values).
- **`PATTERN`** — Regular expression composed of pattern variables defined in `DEFINE`.
- **`DEFINE`** — Predicates that define the classification criteria for each pattern variable.

### Pattern Operators
| Operator | Example | Semantics |
| :--- | :--- | :--- |
| Concatenation | `A B C` | Variable `A` immediately followed by `B`, then `C` |
| `+` (One or more) | `A+` | Matches one or more consecutive occurrences of `A` |
| `*` (Zero or more) | `A*` | Matches zero or more consecutive occurrences of `A` |
| `?` (Optional) | `A?` | Matches zero or one occurrence of `A` |
| Quantifier `{n,m}` | `A{3,5}` | Matches between `n` and `m` repetitions of `A` |
| Quantifier `{n,}` | `A{3,}` | Matches at least `n` repetitions of `A` |

---

## Navigation & Aggregate Functions

Inside `MEASURES` and `DEFINE`, contextual navigation functions inspect row state within the match:

- **`FIRST(column)`** — Value of the column in the first row of the matched pattern.
- **`LAST(column)`** — Value of the column in the final row of the matched pattern.
- **`PREV(column)`** — Value of the column in the immediately preceding row within the partition.
- **`NEXT(column)`** — Value of the column in the immediately succeeding row within the partition.

---

## Examples

### 1. Market Volatility: Detecting Price Spikes

Detect stocks undergoing three or more consecutive price rises followed by an immediate drop:

```sql
SELECT 
    ticker,
    spike_start,
    spike_peak,
    spike_end,
    starting_price,
    peak_price,
    ending_price
FROM #daily_stock_prices
MATCH_RECOGNIZE (
    PARTITION BY ticker
    ORDER BY trade_date
    MEASURES
        FIRST(trade_date) AS spike_start,
        LAST(trade_date)  AS spike_end,
        FIRST(open_price) AS starting_price,
        MAX(high_price)   AS peak_price,
        LAST(close_price) AS ending_price
    PATTERN (UP{3,} DOWN)
    DEFINE
        UP   AS close_price > PREV(close_price),
        DOWN AS close_price < PREV(close_price)
) AS spikes;
```

### 2. Production IoT: Predictive Maintenance & Warning Sequence Escalation

Identify equipment that generated 3 consecutive temperature warnings followed by a critical pressure anomaly within industrial telemetry:

```sql
CREATE CONNECTION iot_source AS POSTGRES(HOST='iot.internal', DATABASE='telemetry');
CREATE CONNECTION alerts_dw  AS MSSQL(SERVER='dw.internal', DATABASE='operations');

-- 1. Extract recent raw device readings into staging
SELECT equipment_id, reading_time, temp_c, pressure_psi, status_flag
INTO #staged_telemetry
FROM iot_source.sensor_readings
WHERE reading_time >= DATEADD(HOUR, -12, GETDATE());

-- 2. Detect multi-step overheating and failure patterns
SELECT 
    m.equipment_id,
    m.anomaly_start,
    m.incident_end,
    m.warning_count,
    m.max_temperature,
    m.critical_pressure,
    'CRITICAL_OVERHEAT_SEQUENCE' AS alert_type,
    GETDATE() AS detected_at
INTO #detected_incidents
FROM #staged_telemetry
MATCH_RECOGNIZE (
    PARTITION BY equipment_id
    ORDER BY reading_time
    MEASURES
        FIRST(reading_time)  AS anomaly_start,
        LAST(reading_time)   AS incident_end,
        COUNT(WARN.*)        AS warning_count,
        MAX(WARN.temp_c)     AS max_temperature,
        LAST(FAIL.pressure_psi) AS critical_pressure
    PATTERN (NORMAL WARN{3,} FAIL)
    DEFINE
        NORMAL AS temp_c < 85.0 AND pressure_psi < 120.0,
        WARN   AS temp_c >= 85.0 AND pressure_psi < 150.0,
        FAIL   AS pressure_psi >= 150.0
) AS m;

-- 3. Ingest confirmed incidents into the dispatch incident table
INSERT INTO alerts_dw.dbo.EquipmentIncidents (
    EquipmentId, AnomalyStart, IncidentEnd, WarningCount, MaxTemp, CriticalPressure, AlertType, DetectedAt
)
SELECT equipment_id, anomaly_start, incident_end, warning_count, max_temperature, critical_pressure, alert_type, detected_at
FROM #detected_incidents;
```

---

## Remarks & Performance Guidelines

- **In-Memory Partitioning**: `MATCH_RECOGNIZE` executes in memory over sorted partition windows. Filter extraneous noise rows upstream using `WHERE` to minimize memory overhead.
- **Output Cardinality**: Defaults to `ONE ROW PER MATCH`. Unmatched records are excluded from the output.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [ASOF JOIN](asof-join.md)
- [Window Functions](window.md)
- [ETL Cookbook: IoT Regex Filtering](../../../cookbooks/etl/iot-regex-filtering.md)
- [ETL Cookbook: Time Series Gap Filling](../../../cookbooks/etl/time-series-gap-filling.md)
- [Syntax Index](../../../syntax-index.md)
