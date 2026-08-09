# Dates, Times, and Time Zones in ETL-SQL

This reference defines ETL-SQL temporal values, timezone conversion, relative-date resolution,
precision, formatting, and the information retained or discarded at connector boundaries.

---

## 1. Choose the correct temporal model

ETL-SQL distinguishes three concepts that must not be used interchangeably:

| Concept | ETL-SQL / CLR representation | Meaning |
| :--- | :--- | :--- |
| Calendar or wall-clock value | `DATE`, `TIME`, `DATETIME`, `TIMESTAMP` / `DateTime` or `TimeSpan` | A local reading with no durable timezone identity |
| Instant with numeric offset | `DATETIMEOFFSET` / `DateTimeOffset` | An absolute instant plus the offset used to display it |
| Named timezone | IANA or Windows timezone ID supplied to `AT TIME ZONE` or `RELDATE` | A rule set such as `America/Chicago`, including historical DST rules |

`DateTimeOffset` does not retain a named timezone. `2026-07-02 10:00:00 -05:00` records `-05:00`,
not whether the source zone was `America/Chicago`, `America/Bogota`, or another region. Persist the
IANA timezone ID in a separate string column when that identity is business data.

---

## 2. Engine representations and casts

- `DATE`, `DATETIME`, and `TIMESTAMP` use CLR `DateTime`.
- `TIME` uses CLR `TimeSpan`.
- `DATETIMEOFFSET` uses CLR `DateTimeOffset` and preserves its numeric offset inside the engine,
  including native column batches and spill/reload.

```sql
SELECT CAST('2026-07-02 10:05:51.1234567 -05:00' AS DATETIMEOFFSET) AS observed_at;
SELECT CAST('2026-07-02 10:05:51.1234567' AS DATETIME(3)) AS millisecond_time;
```

An offset-bearing string cast to `DATETIMEOFFSET` retains the supplied offset. A string without an
offset uses the operating system's local offset; portable scripts should therefore supply an offset
or use `AT TIME ZONE` explicitly.

Casting `DateTimeOffset` to `DATETIME` or `TIMESTAMP` keeps its displayed wall-clock fields and drops
the offset. It does not convert the instant to UTC first. Convert to UTC explicitly before dropping
the offset when UTC wall-clock fields are required.

---

## 3. `AT TIME ZONE`

`AT TIME ZONE` converts an instant to the requested named timezone and returns `DATETIMEOFFSET`.
ETL-SQL treats a timezone-neutral input as UTC; this is an engine rule and differs from SQL Server,
which attaches the target zone to an offset-free input.

```sql
SELECT '2026-07-02 15:00:00' AT TIME ZONE 'America/Chicago' AS chicago_time;
SELECT CAST('2026-07-02 10:00:00 -05:00' AS DATETIMEOFFSET)
       AT TIME ZONE 'UTC' AS utc_time;
```

Unknown or invalid timezone IDs raise an execution error. ETL-SQL accepts platform-supported IANA
and Windows IDs and maps the abbreviations listed under RELDATE below.

---

## 4. Timezone-aware `RELDATE`

RELDATE values are strings resolved at execution time. Quote the complete expression, especially when
it contains a timezone:

```sql
DECLARE @yesterday_local RELDATE = 'D-1';
DECLARE @yesterday_ny    RELDATE = 'D-1 America/New_York';
DECLARE @utc_now         RELDATE = 'N UTC';
```

Without a timezone suffix, RELDATE retains its established behavior and resolves to a timezone-neutral
`DATETIME` using the server-local calendar (`NU` uses UTC). With an explicit timezone suffix, it resolves
to `DATETIMEOFFSET`; calendar boundaries are calculated in that zone and the offset applicable on the
resolved date is retained.

| Alias | Named timezone used |
| :--- | :--- |
| `UTC`, `GMT` | `UTC` |
| `EST`, `EDT` | `America/New_York` |
| `CST`, `CDT` | `America/Chicago` |
| `MST`, `MDT` | `America/Denver` |
| `PST`, `PDT` | `America/Los_Angeles` |
| `CET`, `CEST` | `Europe/Paris` |
| `BST` | `Europe/London` |
| `JST` | `Asia/Tokyo` |
| `AEST`, `AEDT` | `Australia/Sydney` |

These aliases select regional rules; for example, both `EST` and `EDT` select `America/New_York`, and
the offset is chosen from the resolved date. Use an IANA ID in new scripts to avoid abbreviation
ambiguity.

### DST gaps and folds

- A nonexistent local time in a spring-forward gap is rejected.
- For an ambiguous local time in a fall-back fold, ETL-SQL deterministically chooses the smaller UTC
  offset (normally the standard-time occurrence).
- Period anchors normally resolve at midnight, but historical timezone changes can also affect midnight.

See [RELDATE](../functions/datetime/reldate.md) for all relative-date anchors and arithmetic rules.

---

## 5. Connector boundary matrix

The engine preserves a `DateTimeOffset` until a destination's native model requires normalization.

| Connector / destination type | Write behavior | Read behavior | Information loss |
| :--- | :--- | :--- | :--- |
| SQL Server `DATETIMEOFFSET` | Bind `DateTimeOffset` directly | Provider returns `DateTimeOffset` | Named zone is not stored |
| PostgreSQL `TIMESTAMPTZ` | Normalize to UTC before binding/COPY | Return `DateTimeOffset` with offset `+00:00` | Original offset and named zone are not stored |
| Oracle `TIMESTAMP WITH TIME ZONE` | Bind as `TimeStampTZ` | Combine Oracle wall time with its provider offset | Region identity may become a numeric offset |
| Oracle `TIMESTAMP WITH LOCAL TIME ZONE` | Oracle normalizes to database/session rules | Return the session-local reading and offset | Original offset and named zone are not retained |
| Snowflake `TIMESTAMP_TZ` | Pass `DateTimeOffset` through the ADO.NET provider | Provider-defined `DateTimeOffset`/timestamp value | Snowflake retains offset, not the named zone |
| BigQuery `TIMESTAMP` | Normalize to UTC `DateTime` | UTC instant | Offset and named zone are not stored |
| MySQL `TIMESTAMP` / `DATETIME` | Provider/server conversion rules apply; prefer UTC for `TIMESTAMP` | Normally timezone-neutral `DateTime` | Numeric offset and named zone are not retained |
| SQLite | Provider serializes a dynamic value, normally ISO text | Type depends on schema/provider conversion | No native timezone type; declare a storage convention |
| ODBC | Driver-dependent | Driver-dependent | Verify the selected driver and target type |
| Files (`CSV`, JSON, XML, Parquet, Avro) | Format-dependent | Format-dependent | Text formats need an explicit round-trip format and offset |

PostgreSQL `TIMESTAMPTZ` stores an instant, not an offset or timezone. Session timezone can affect SQL
display formatting, but ETL-SQL normalizes its parameter and read boundaries to UTC.

For systems without a timezone-aware type, use two columns when the local interpretation matters:

```sql
-- Instant plus the business timezone whose rules produced the local view
OccurredAtUtc DATETIMEOFFSET,
TimeZoneId    VARCHAR(100)
```

---

## 6. Fractional-second precision

Engine casts accept `DATETIME(p)`, `TIMESTAMP(p)`, and `DATETIMEOFFSET(p)`, where `p` must be in
the CLR range `0..7`. ETL-SQL truncates extra fractional digits; it does not round them.

```sql
SELECT CAST('2026-07-02 10:05:51.1234567' AS DATETIME(3));
-- 2026-07-02 10:05:51.123
```

Remote type names are dialect-specific and must not be copied between providers:

| Provider | Typical precision-aware types |
| :--- | :--- |
| SQL Server | `DATETIME2(p)`, `DATETIMEOFFSET(p)`; `DATETIME` has fixed legacy precision |
| PostgreSQL | `TIMESTAMP(p)`, `TIMESTAMPTZ(p)` |
| Oracle | `TIMESTAMP(p)`, `TIMESTAMP(p) WITH TIME ZONE` |
| Snowflake | `TIMESTAMP_NTZ(p)`, `TIMESTAMP_LTZ(p)`, `TIMESTAMP_TZ(p)` |
| BigQuery | `TIMESTAMP` uses microsecond precision |

Inside an `EXECUTE connection BEGIN ... END` block, use the target database's native type names.

---

## 7. Formatting and parsing

`FORMAT(value, format)` uses .NET format strings with invariant culture:

```sql
SELECT FORMAT(@observed_at, 'yyyy-MM-ddTHH:mm:ss.fffffffzzz') AS offset_text;
SELECT FORMAT(@observed_at, 'O') AS round_trip_text;
```

Use the round-trip `O` format when a text file must retain an offset. A display format is presentation,
not a storage contract; formatting a datetime produces a string and removes temporal type information.

`TO_DATE` currently accepts a value but does not implement its documented optional format argument.
Use ISO 8601 input for portable parsing. Database-native formatting functions such as Oracle `TO_CHAR`,
PostgreSQL `TO_CHAR`, MySQL `DATE_FORMAT`, and SQLite `strftime` belong inside target-specific `EXECUTE`
blocks rather than portable engine expressions.

---

## 8. Engine date functions

Portable engine functions include `GETDATE`, `GETUTCDATE`, `CURRENT_DATE`, `CURRENT_TIMESTAMP`,
`SYSDATE`, `DATEADD`, `DATEDIFF`, `DATEPART`, `DATE_PART`, `DATENAME`, `EXTRACT`, `DATE_TRUNC`, `TRUNC`,
`TO_DATE`, `TO_TIMESTAMP`, `DATEFROMPARTS`, `DATETIMEFROMPARTS`, `DATETIME2FROMPARTS`,
`DATETIMEOFFSETSFROMPARTS`, `EOMONTH`, `ISDATE`, `RELDATE`, and `AT TIME ZONE`.

See [Date & Time Functions](../functions/datetime/README.md) for signatures
and examples, and [AT TIME ZONE](../statements/expressions-and-operators.md) for expression syntax.

