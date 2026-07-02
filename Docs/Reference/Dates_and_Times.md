# Dates, Times, and Time Zones in ETL-SQL

This document serves as the canonical reference manual for date, time, and timezone offset representation, relative date resolution, precision controls, and cross-database translation rules within the **ETL-SQL** engine.

---

## 1. Core Architecture: The Unified DateTime Model

Databases differ widely in how they store timezone-aware date and time values. To act as a portable, agnostic "man in the middle," ETL-SQL handles date and time values using a unified in-memory representation:

```
┌────────────────────────────────────────────────────────┐
│               Target Database Drivers                  │
│  (Microsoft.Data.SqlClient, Npgsql, ODP.NET, etc.)     │
└───────────┬────────────────────────────────┬───────────┘
            │ [Read Boundary]                │ [Write Boundary]
            ▼                                ▲
┌────────────────────────────────────────────────────────┐
│             ETL-SQL Engine Intermediate Row            │
│  - Timezone-neutral: CLR DateTime                      │
│  - Timezone-aware:   CLR DateTimeOffset                │
└────────────────────────────────────────────────────────┘
```

1.  **Timezone-Neutral (`DATETIME`, `TIMESTAMP`, `DATE`)**: Represented internally as a CLR `DateTime`.
2.  **Timezone-Aware (`DATETIMEOFFSET`)**: Represented internally as a CLR `DateTimeOffset`. This object stores both the absolute moment in time (UTC) and the specific local offset (e.g., `+02:00` or `-05:00`).

---

## 2. Casting & Type Conversions

### 2.1 Casting to `DATETIMEOFFSET`
When a value is cast to `DATETIMEOFFSET` in a `CAST` or `TRY_CAST` expression, or declared as a `DATETIMEOFFSET` parameter, the engine parses it using `DateTimeOffset.Parse`.
*   If the input string includes a timezone offset (e.g., `'2026-07-02 10:05:51 -05:00'`), the offset is preserved in memory.
*   If the input lacks an offset, the system local timezone offset is assumed by default.

### 2.2 Casting to `DATETIME` or `TIMESTAMP`
When a timezone-aware `DateTimeOffset` is cast to a timezone-neutral `DATETIME` or `TIMESTAMP`, it is converted to a CLR `DateTime` representing the local clock reading at that offset, with its `Kind` set to `DateTimeKind.Unspecified`.

---

## 3. RELDATE (Relative Date) Timezone Suffixes

Relative date expressions (e.g., `D-1`, `N-2H`) are resolved at execution time. By default, period anchors snap using the server's local clock. 

To resolve a relative date under a specific timezone's calendar, you can append a timezone suffix to the relative expression:

```sql
DECLARE @yesterday_est RELDATE = D-1 EST;
DECLARE @today_ny      RELDATE = D America/New_York;
DECLARE @utc_now       RELDATE = N UTC;
```

### 3.1 Resolution Walkthrough
When a relative expression with a timezone suffix is resolved:
1.  **Convert Clock Time**: The engine takes the current absolute moment in time (`DateTimeOffset.UtcNow`) and converts it to the target timezone (e.g., `America/New_York`).
2.  **Period Snapping**: Date boundary snapping (e.g., determining "yesterday" or "first day of the week") is calculated using the calendar dates of the *target timezone*, not the server's local clock.
3.  **Preserve Offset**: The resulting local date is returned as a `DateTimeOffset` containing the correct offset matching that date in the target timezone (including daylight saving offset transitions).

### 3.2 Timezone Suffix Mappings
The engine supports standard IANA timezone region names (e.g., `America/Chicago`, `Europe/London`) and automatically maps common abbreviations to resolve platform-specific discrepancies (Windows vs. Linux):

| Abbreviation | Mapped IANA Timezone ID | Description |
| :--- | :--- | :--- |
| `UTC` / `GMT` | `UTC` | Coordinated Universal Time |
| `EST` / `EDT` | `America/New_York` | Eastern Time |
| `CST` / `CDT` | `America/Chicago` | Central Time |
| `MST` / `MDT` | `America/Denver` | Mountain Time |
| `PST` / `PDT` | `America/Los_Angeles` | Pacific Time |
| `CET` / `CEST` | `Europe/Paris` | Central European Time |
| `BST` | `Europe/London` | British Summer Time |
| `JST` | `Asia/Tokyo` | Japan Standard Time |
| `AEST` / `AEDT`| `Australia/Sydney` | Australian Eastern Time |

---

## 4. Precision Constraints (`DATETIME(x)`)

The parser supports precision constraints (e.g., `DATETIME(3)` or `DATETIMEOFFSET(6)`). 
1.  **SQL Pushdown**: When statements are pushed down to remote databases, the precision modifiers are passed verbatim in the generated SQL, and the target database natively enforces them.
2.  **Local Engine Calculations**: When values are cast to `DATETIME(x)` or `DATETIMEOFFSET(x)` within local engine code, the engine truncates/rounds the fractional seconds of the C# `DateTime` or `DateTimeOffset` value to `x` digits.
    *   *Example*: `CAST('2026-07-02 10:05:51.1234567' AS DATETIME(3))` returns `2026-07-02 10:05:51.123`.

---

## 5. Write-Back Behaviors (No Explicit Casting Required)

When writing `DateTimeOffset` columns back to a destination database, **the user does not need to perform manual `CAST` expressions**. The engine's database connectors intercept CLR standard types and map them automatically to target timezone-aware columns:

```
┌────────────────────────────────────────────────────────┐
│             ETL-SQL DateTimeOffset (CLR)               │
└───────────────────────────┬────────────────────────────┘
                            │ [Auto Parameter Binding]
                            ▼
┌────────────────────────────────────────────────────────┐
│  - SQL Server:      DATETIMEOFFSET                     │
│  - PostgreSQL:      TIMESTAMPTZ (stored as UTC)        │
│  - Oracle:          TIMESTAMP WITH TIME ZONE           │
│  - Snowflake:       TIMESTAMP_TZ                       │
│  - BigQuery:        TIMESTAMP (mapped as UTC Timestamp)│
└────────────────────────────────────────────────────────┘
```

### 5.1 SQL Server (`DATETIMEOFFSET` column)
The SQL Server connector passes the CLR `DateTimeOffset` directly to the `SqlCommand` parameter list. `Microsoft.Data.SqlClient` natively maps this to SQL Server's `DATETIMEOFFSET` type, preserving both the time and local offset.

### 5.2 PostgreSQL (`TIMESTAMPTZ` column)
PostgreSQL's `TIMESTAMPTZ` stores values in UTC internally. The PostgreSQL connector passes the CLR `DateTimeOffset` to `NpgsqlParameter`. `Npgsql` converts the value to UTC and writes it to the database. The database then converts it back to the connection's session timezone upon subsequent reads.

### 5.3 Oracle (`TIMESTAMP WITH TIME ZONE` column)
ODP.NET natively maps CLR `DateTimeOffset` parameters to Oracle's `TIMESTAMP WITH TIME ZONE` type. The connector handles this mapping automatically without requiring the user to construct custom Oracle timezone strings.

### 5.4 Snowflake (`TIMESTAMP_TZ` column)
The Snowflake ADO.NET connector maps standard `DateTimeOffset` values to the Snowflake `TIMESTAMP_TZ` type. The offset information is preserved on insert.

### 5.5 BigQuery (`TIMESTAMP` column)
BigQuery `TIMESTAMP` values are absolute UTC timestamps. The BigQuery connector parses `DateTimeOffset` and extracts the UTC representation (`v.UtcDateTime`), binding it to the parameter list as a `BigQueryDbType.Timestamp`.
